// ============================================================
// 新增端点补丁（需求变更）。
// 每个 partial class 只包含新增的 action 和新增的 Request DTO。
// 原有文件（AdminController / GroupController / RuleController）须声明为
// sealed partial class（已在同批修正文件中完成）。
// ============================================================

using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Management;
using Rbac.Application.Repositories;
using Rbac.Application.Security;
using Rbac.Domain.Groups;
using Rbac.Domain.Rules;
using Rbac.Domain.ValueObjects;

namespace Rbac.Api.Controllers;

// ── AdminController 补丁 ──────────────────────────────────────────

public sealed partial class AdminController
{
    // DELETE /api/admin/{dxeId} — 物理删除管理员
    [HttpDelete("{dxeId}")]
    public async Task<ApiResponse<object>> Delete(string dxeId, CancellationToken ct)
    {
        var ctx = RequireContext();

        var admin = await _guard.LoadAdminByDxEIdAsync(dxeId, ct);
        if (admin is null) return ApiResponse<object>.Fail(40400, "管理员不存在");

        await _write.DeleteAdministratorAsync(admin, operatorUserid: ctx.Userid, ct);
        return ApiResponse<object>.Ok(null!);
    }

    // PUT /api/admin/{dxeId} — 完整编辑（username / status / group_arr）
    [HttpPut("{dxeId}")]
    public async Task<ApiResponse<object>> Update(
        string dxeId, [FromBody] UpdateAdminRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        var admin = await _guard.LoadAdminByDxEIdAsync(dxeId, ct);
        if (admin is null) return ApiResponse<object>.Fail(40400, "管理员不存在");

        var changedFields = new List<string>();
        var oldStatus = admin.Status.ToString();

        if (req.Username is not null && req.Username != admin.Username)
        {
            admin.UpdateUsername(req.Username);
            changedFields.Add("username");
        }

        if (req.Status is not null && req.Status != admin.Status.ToString())
        {
            if (req.Status == "Disabled") admin.Disable();
            else admin.Enable();
            changedFields.Add("status");
        }

        if (changedFields.Count > 0)
        {
            await _write.SaveAdministratorAsync(
                admin,
                changedFields,
                oldStatus: oldStatus,
                affectedGroupCodes: Array.Empty<string>(),
                operatorUserid: ctx.Userid,
                ct);
        }

        // group_arr 变更：diff 现有成员 → 增删 GroupMember
        if (req.GroupArr is not null)
        {
            // IGroupMemberRepository 和 IGroupRepository 均通过 ServiceLocator 获取，
            // 避免在 partial class 构造函数中注入（原始文件已定型）
            var memberRepo = HttpContext.RequestServices
                .GetRequiredService<IGroupMemberRepository>();
            var groupRepo = HttpContext.RequestServices
                .GetRequiredService<IGroupRepository>();

            var currentMembers = await memberRepo
                .FindByUseridAndProjectAsync(admin.Userid.Value, ctx.Project, ct);

            var currentCodes = currentMembers
                .Select(m => m.GroupCode.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var targetCodes = req.GroupArr
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 新增
            foreach (var code in targetCodes.Except(currentCodes))
            {
                var group = await groupRepo.FindByGroupCodeAsync(
                    new GroupCode(code), new ProjectCode(ctx.Project), ct);
                if (group is null) continue;

                var newMember = RbacGroupMember.Create(
                    Guid.NewGuid(), admin.Userid, group.GroupCode, group.Project,
                    grantedBy: ctx.Userid);

                await _write.SaveGroupMemberAsync(
                    newMember,
                    affectedUserids: new[] { admin.Userid.Value },
                    groupPermissionCodes: group.PermissionCodes.Select(p => p.Value).ToList(),
                    operatorUserid: ctx.Userid,
                    ct);
            }

            // 删除
            foreach (var member in currentMembers.Where(
                m => !targetCodes.Contains(m.GroupCode.Value)))
            {
                var group = await groupRepo.FindByGroupCodeAsync(
                    member.GroupCode, member.Project, ct);
                var permCodes = group?.PermissionCodes.Select(p => p.Value).ToList()
                    ?? new List<string>();

                await _write.DeleteGroupMemberAsync(
                    member,
                    affectedUserids: new[] { admin.Userid.Value },
                    groupPermissionCodes: permCodes,
                    operatorUserid: ctx.Userid,
                    ct);
            }
        }

        return ApiResponse<object>.Ok(null!);
    }
}

// ── GroupController 补丁 ──────────────────────────────────────────

public sealed partial class GroupController
{
    // DELETE /api/group/{dxeId} — 物理删除权限组（三项前置校验）
    [HttpDelete("{dxeId}")]
    public async Task<ApiResponse<object>> Delete(string dxeId, CancellationToken ct)
    {
        var ctx = RequireContext();

        var group = await _guard.LoadGroupByDxEIdAsync(dxeId, ctx.Project, ct);
        if (group is null) return ApiResponse<object>.Fail(40400, "权限组不存在");

        // 校验 1：是否存在子组（_groupRepo 已在原始 GroupController 注入）
        var allGroups = await _groupRepo.FindByProjectAsync(
            new ProjectCode(ctx.Project), ct);
        if (allGroups.Any(g => g.ParentGroupCode?.Value == group.GroupCode.Value))
            return ApiResponse<object>.Fail(40009, "请先删除或迁移该组下的子权限组");

        // 校验 2 + 3：成员检查、操作者自删检查（通过 IGroupMemberRepository）
        var memberRepo = HttpContext.RequestServices
            .GetRequiredService<IGroupMemberRepository>();

        var members = await memberRepo
            .FindByGroupCodeAndProjectAsync(group.GroupCode.Value, ctx.Project, ct);
        if (members.Count > 0)
            return ApiResponse<object>.Fail(40009, "该权限组下仍有关联用户，请先移除成员");

        var selfInGroup = await memberRepo
            .FindByUseridAndProjectAsync(ctx.Userid, ctx.Project, ct);
        if (selfInGroup.Any(m => m.GroupCode.Value == group.GroupCode.Value))
            return ApiResponse<object>.Fail(40009, "不能删除自己所属的权限组");

        await _write.DeleteGroupAsync(
            group,
            affectedUserids: members.Select(m => m.Userid.Value).ToList(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // PUT /api/group/{dxeId} — 完整编辑权限组
    [HttpPut("{dxeId}")]
    public async Task<ApiResponse<object>> Update(
        string dxeId, [FromBody] UpdateGroupRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        var group = await _guard.LoadGroupByDxEIdAsync(dxeId, ctx.Project, ct);
        if (group is null) return ApiResponse<object>.Fail(40400, "权限组不存在");

        var changedFields = new List<string>();
        var oldRuleCodes  = group.RuleCodes.Select(r => r.Value).ToList();
        var oldPermCodes  = group.PermissionCodes.Select(p => p.Value).ToList();

        if (req.GroupName is not null && req.GroupName != group.GroupName)
        {
            group.UpdateName(req.GroupName);
            changedFields.Add("groupName");
        }

        // req.ParentGroupCode == "" 表示提升为根组（null parent）
        if (req.ParentGroupCode is not null)
        {
            group.UpdateParentGroupCode(
                string.IsNullOrWhiteSpace(req.ParentGroupCode)
                    ? null
                    : new GroupCode(req.ParentGroupCode));
            changedFields.Add("parentGroupCode");
        }

        if (req.Status is not null && req.Status != group.Status.ToString())
        {
            if (req.Status == "Disabled") group.Disable();
            else group.Enable();
            changedFields.Add("status");
        }

        if (req.RuleCodes is not null || req.PermissionCodes is not null)
        {
            var newRuleCodes = (req.RuleCodes ?? oldRuleCodes.ToArray())
                .Select(r => new RuleCode(r)).ToList();
            var newPermCodes = (req.PermissionCodes ?? oldPermCodes.ToArray())
                .Select(p => new PermissionCode(p)).ToList();
            group.UpdateRules(newRuleCodes, newPermCodes);
            changedFields.Add("ruleCodes");
            changedFields.Add("permissionCodes");
        }

        await _write.SaveGroupAsync(
            group, changedFields, oldRuleCodes, oldPermCodes,
            affectedUserids: req.AffectedUserids ?? Array.Empty<string>(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }
}

// ── RuleController 补丁 ───────────────────────────────────────────

public sealed partial class RuleController
{
    // PUT /api/rule/{dxeId} — 完整编辑规则元数据
    [HttpPut("{dxeId}")]
    public async Task<ApiResponse<object>> Update(
        string dxeId, [FromBody] UpdateRuleRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        var rule = await _guard.LoadRuleByDxEIdAsync(dxeId, ctx.Project, ct);
        if (rule is null) return ApiResponse<object>.Fail(40400, "规则不存在");

        var oldPermCode = rule.PermissionCode.Value;

        MenuType? menuType = null;
        if (req.MenuType is not null &&
            Enum.TryParse<MenuType>(req.MenuType, ignoreCase: true, out var mt))
            menuType = mt;

        RuleStatus? status = null;
        if (req.Status is not null &&
            Enum.TryParse<RuleStatus>(req.Status, ignoreCase: true, out var rs))
            status = rs;

        RuleCode? parentRuleCode = req.ParentRuleCode is not null
            ? (string.IsNullOrWhiteSpace(req.ParentRuleCode)
                ? null : new RuleCode(req.ParentRuleCode))
            : null;

        PermissionCode? permCode = req.PermissionCode is not null
            ? new PermissionCode(req.PermissionCode) : null;

        rule.UpdateMenuMeta(
            title: req.Title,
            name: req.Name,
            path: req.Path,
            parentRuleCode: parentRuleCode,
            menuType: menuType,
            url: req.Url,
            component: req.Component,
            extend: req.Extend,
            keepalive: req.Keepalive,
            weigh: req.Weigh,
            status: status,
            permissionCode: permCode,
            parentRuleCodeSpecified: req.ParentRuleCode is not null);

        // 新旧 permCode 均加入受影响列表（可能两值相同，用 HashSet 去重）
        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { oldPermCode, rule.PermissionCode.Value };

        await _write.SaveRuleAsync(
            rule,
            changeKind: "Updated",
            affectedPermissionCodes: affected.ToList(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

}

// ── Request DTOs ──────────────────────────────────────────────────

/// <summary>管理员完整编辑请求。null 字段表示不修改。</summary>
public sealed record UpdateAdminRequest(
    string? Username,
    string? Status,
    string[]? GroupArr);

/// <summary>权限组完整编辑请求。null 字段表示不修改。</summary>
public sealed record UpdateGroupRequest(
    string? GroupName,
    string? ParentGroupCode,   // 空字符串表示提升为根组
    string? Status,
    string[]? RuleCodes,
    string[]? PermissionCodes,
    string[]? AffectedUserids);

/// <summary>规则完整编辑请求。null 字段表示不修改。</summary>
public sealed record UpdateRuleRequest(
    string? Title,
    string? Name,
    string? Path,
    string? ParentRuleCode,    // 空字符串表示提升为根节点
    string? MenuType,
    string? Url,
    string? Component,
    string? Extend,
    bool? Keepalive,
    int? Weigh,
    string? Status,
    string? PermissionCode);
