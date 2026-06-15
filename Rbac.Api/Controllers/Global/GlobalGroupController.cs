using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Global;
using Rbac.Application.Management;
using Rbac.Application.Repositories;
using Rbac.Application.Search;
using Rbac.Application.Security;
using Rbac.Domain.Groups;
using Rbac.Domain.ValueObjects;

namespace Rbac.Api.Controllers.Global;

/// <summary>
/// 跨 project 权限组管理接口。
/// 调用方必须携带 X-Project: __global__ header。
///
/// 读操作：委托现有 IRbacManagementSearchService，target project 来自 query 参数。
/// 写操作：委托 IGlobalManagementService（WriteGuard + WriteService fan-out）。
/// </summary>
[ApiController]
[Route("api/global/group")]
public sealed class GlobalGroupController : ControllerBase
{
    private readonly ICurrentRbacContextAccessor _ctx;
    private readonly IRbacManagementSearchService _search;
    private readonly IRbacManagementWriteService _write;
    private readonly RbacManagementWriteGuard _guard;
    private readonly IGlobalManagementService _globalService;
    private readonly IGroupRepository _groupRepo;
    private readonly IRuleRepository _ruleRepo;
    private readonly IGroupMemberRepository _memberRepo;
    private readonly IApiPermissionMapRepository _apiMapRepo;

    public GlobalGroupController(
        ICurrentRbacContextAccessor ctx,
        IRbacManagementSearchService search,
        IRbacManagementWriteService write,
        RbacManagementWriteGuard guard,
        IGlobalManagementService globalService,
        IGroupRepository groupRepo,
        IRuleRepository ruleRepo,
        IGroupMemberRepository memberRepo,
        IApiPermissionMapRepository apiMapRepo)
    {
        _ctx           = ctx;
        _search        = search;
        _write         = write;
        _guard         = guard;
        _globalService = globalService;
        _groupRepo     = groupRepo;
        _ruleRepo      = ruleRepo;
        _memberRepo    = memberRepo;
        _apiMapRepo    = apiMapRepo;
    }

    // ── 跨项目权限组搜索 ────────────────────────────────────────────

    /// <summary>
    /// GET /api/global/group/list — 跨项目权限组搜索。
    /// query.Project 来自调用方，null 时搜索所有项目。
    /// 权限码：rbac.global.group.manage : access
    /// </summary>
    [HttpGet("list")]
    public async Task<ApiResponse<PagedData<GroupSearchResult>>> List(
        [FromQuery] GroupSearchQuery query, CancellationToken ct)
    {
        // 不覆盖 query.Project：由调用方传入目标项目；null = 全项目搜索
        var data = await _search.SearchGroupsAsync(query, ct);
        return ApiResponse<PagedData<GroupSearchResult>>.Ok(data);
    }

    [HttpPost]
    public async Task<ApiResponse<object>> Create(
        [FromBody] GlobalCreateGroupRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(req.TargetProject)) return Fail(40001, "targetProject 涓嶈兘涓虹┖");
        if (RbacGlobalConstants.IsReservedProject(req.TargetProject)) return Fail(40009, "涓嶅厑璁告搷浣滀繚鐣欑郴缁?project");
        if (string.IsNullOrWhiteSpace(req.GroupCode)) return Fail(40001, "groupCode 涓嶈兘涓虹┖");
        if (string.IsNullOrWhiteSpace(req.GroupName)) return Fail(40001, "groupName 涓嶈兘涓虹┖");

        var project = new ProjectCode(req.TargetProject);
        var group = RbacGroup.Create(
            Guid.NewGuid(),
            new GroupCode(req.GroupCode),
            project,
            req.GroupName,
            string.IsNullOrWhiteSpace(req.ParentGroupCode) ? null : new GroupCode(req.ParentGroupCode));

        var changedFields = new List<string> { "created" };
        if (req.Status is not null)
        {
            if (req.Status == "Disabled" || req.Status == "0") group.Disable();
            else group.Enable();
            changedFields.Add("status");
        }

        if (req.RuleCodes is not null)
        {
            var rules = await ResolveGroupRulesAsync(project, req.RuleCodes, req.ExtraPermissionCodes, ct);
            group.UpdateRules(rules.RuleCodes, rules.PermissionCodes);
            changedFields.Add("ruleCodes");
            changedFields.Add("permissionCodes");
        }

        await _write.SaveGroupAsync(
            group,
            changedFields,
            oldRuleCodes: Array.Empty<string>(),
            oldPermissionCodes: Array.Empty<string>(),
            affectedUserids: Array.Empty<string>(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(new { groupCode = group.GroupCode.Value });
    }

    [HttpPut("{groupCode}")]
    public async Task<ApiResponse<object>> Update(
        string groupCode, [FromBody] GlobalUpdateGroupRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(req.TargetProject)) return Fail(40001, "targetProject 涓嶈兘涓虹┖");
        if (RbacGlobalConstants.IsReservedProject(req.TargetProject)) return Fail(40009, "涓嶅厑璁告搷浣滀繚鐣欑郴缁?project");

        var group = await _guard.LoadGroupByCodeAsync(groupCode, req.TargetProject, ct);
        if (group is null) return Fail(40400, "鏉冮檺缁勪笉瀛樺湪");

        var changedFields = new List<string>();
        var oldRuleCodes = group.RuleCodes.Select(r => r.Value).ToList();
        var oldPermCodes = group.PermissionCodes.Select(p => p.Value).ToList();

        var groupName = req.GroupName ?? req.Name;
        if (groupName is not null && groupName != group.GroupName)
        {
            group.UpdateName(groupName);
            changedFields.Add("groupName");
        }

        if (req.ParentGroupCode is not null)
        {
            group.UpdateParentGroupCode(string.IsNullOrWhiteSpace(req.ParentGroupCode)
                ? null
                : new GroupCode(req.ParentGroupCode));
            changedFields.Add("parentGroupCode");
        }

        if (req.Status is not null && req.Status != group.Status.ToString())
        {
            if (req.Status == "Disabled" || req.Status == "0") group.Disable();
            else group.Enable();
            changedFields.Add("status");
        }

        if (req.RuleCodes is not null)
        {
            var rules = await ResolveGroupRulesAsync(group.Project, req.RuleCodes, req.ExtraPermissionCodes, ct);
            group.UpdateRules(rules.RuleCodes, rules.PermissionCodes);
            changedFields.Add("ruleCodes");
            changedFields.Add("permissionCodes");
        }

        if (changedFields.Count > 0)
        {
            var members = await _memberRepo.FindByGroupCodeAndProjectAsync(
                group.GroupCode.Value, group.Project.Value, ct);
            await _write.SaveGroupAsync(
                group,
                changedFields,
                oldRuleCodes,
                oldPermCodes,
                affectedUserids: members.Select(m => m.Userid.Value).ToList(),
                operatorUserid: ctx.Userid,
                ct);
        }

        return ApiResponse<object>.Ok(null!);
    }

    [HttpDelete("{groupCode}")]
    public async Task<ApiResponse<object>> Delete(
        string groupCode, [FromQuery] string targetProject, CancellationToken ct)
    {
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(targetProject)) return Fail(40001, "targetProject 涓嶈兘涓虹┖");
        if (RbacGlobalConstants.IsReservedProject(targetProject)) return Fail(40009, "涓嶅厑璁告搷浣滀繚鐣欑郴缁?project");

        var group = await _guard.LoadGroupByCodeAsync(groupCode, targetProject, ct);
        if (group is null) return Fail(40400, "鏉冮檺缁勪笉瀛樺湪");

        var allGroups = await _groupRepo.FindByProjectAsync(new ProjectCode(targetProject), ct);
        if (allGroups.Any(g => g.ParentGroupCode?.Value == group.GroupCode.Value))
            return Fail(40009, "璇峰厛鍒犻櫎鎴栬縼绉昏缁勪笅鐨勫瓙鏉冮檺缁?");

        var members = await _memberRepo.FindByGroupCodeAndProjectAsync(
            group.GroupCode.Value, targetProject, ct);
        if (members.Count > 0)
            return Fail(40009, "璇ユ潈闄愮粍涓嬩粛鏈夊叧鑱旂敤鎴凤紝璇峰厛绉婚櫎鎴愬憳");

        await _write.DeleteGroupAsync(
            group,
            affectedUserids: Array.Empty<string>(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // ── 权限组成员管理 ─────────────────────────────────────────────

    /// <summary>
    /// POST /api/global/group/{groupCode}/members — 将用户加入指定 project 内的权限组。
    /// 目标 project 来自 request body（非 X-Project）。
    /// 已是成员则跳过（幂等）。
    /// 权限码：rbac.global.group.manage : write
    /// </summary>
    [HttpPost("{groupCode}/members")]
    public async Task<ApiResponse<PerProjectResultReport>> AddMember(
        string groupCode, [FromBody] GroupMemberRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        if (string.IsNullOrWhiteSpace(req.Userid))
            return FailReport(40001, "userid 不能为空");
        if (string.IsNullOrWhiteSpace(req.TargetProject))
            return FailReport(40001, "targetProject 不能为空");

        var report = await _globalService.AddUserToGroupAsync(
            req.Userid, groupCode, req.TargetProject, ctx.Userid, ct);

        return ApiResponse<PerProjectResultReport>.Ok(report);
    }

    /// <summary>
    /// DELETE /api/global/group/{groupCode}/members/{userid}?targetProject=xxx —
    /// 将用户从指定 project 内的权限组移除。
    /// 不是成员则跳过（幂等）。
    /// 权限码：rbac.global.group.manage : write
    /// </summary>
    [HttpDelete("{groupCode}/members/{userid}")]
    public async Task<ApiResponse<PerProjectResultReport>> RemoveMember(
        string groupCode, string userid,
        [FromQuery] string targetProject,
        CancellationToken ct)
    {
        var ctx = RequireContext();

        if (string.IsNullOrWhiteSpace(targetProject))
            return FailReport(40001, "targetProject 不能为空");

        var report = await _globalService.RemoveUserFromGroupAsync(
            userid, groupCode, targetProject, ctx.Userid, ct);

        return ApiResponse<PerProjectResultReport>.Ok(report);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private CurrentRbacContext RequireContext() =>
        _ctx.Context ?? throw new InvalidOperationException("RbacContext missing");

    private async Task<(IReadOnlyList<RuleCode> RuleCodes, IReadOnlyList<PermissionCode> PermissionCodes)> ResolveGroupRulesAsync(
        ProjectCode project,
        IReadOnlyList<string> requestedRuleCodes,
        IReadOnlyList<string>? extraPermissionCodes,
        CancellationToken ct)
    {
        var ruleCodeSet = requestedRuleCodes
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ruleCodeSet.Contains("*"))
            return (new List<RuleCode> { new("*") }, new List<PermissionCode> { new("*") });

        var allRules = await _ruleRepo.FindActiveByProjectAsync(project, ct);
        var selectedRules = allRules.Where(r => ruleCodeSet.Contains(r.RuleCode.Value)).ToList();
        var derivedPermCodes = selectedRules.Select(r => r.PermissionCode.Value).ToList();

        var validApiPermCodes = (await _apiMapRepo.FindActiveByProjectAsync(project, ct))
            .Select(m => m.PermissionCode.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extraPerms = (extraPermissionCodes ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p) && validApiPermCodes.Contains(p));

        var permissionCodes = derivedPermCodes
            .Union(extraPerms, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PermissionCode(p))
            .ToList();

        return (selectedRules.Select(r => r.RuleCode).ToList(), permissionCodes);
    }

    private static ApiResponse<object> Fail(int code, string msg) =>
        ApiResponse<object>.Fail(code, msg);

    private static ApiResponse<PerProjectResultReport> FailReport(int code, string msg) =>
        ApiResponse<PerProjectResultReport>.Fail(code, msg);
}

// ── Request DTOs ───────────────────────────────────────────────────────

public sealed record GroupMemberRequest(string Userid, string TargetProject);

public sealed record GlobalCreateGroupRequest(
    string TargetProject,
    string GroupCode,
    string GroupName,
    string? ParentGroupCode = null,
    string? Status = null,
    string[]? RuleCodes = null,
    string[]? ExtraPermissionCodes = null);

public sealed record GlobalUpdateGroupRequest(
    string TargetProject,
    string? GroupName = null,
    string? Name = null,
    string? ParentGroupCode = null,
    string? Status = null,
    string[]? RuleCodes = null,
    string[]? ExtraPermissionCodes = null);
