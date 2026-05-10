using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Identity;
using Rbac.Application.Management;
using Rbac.Application.Repositories;
using Rbac.Application.Search;
using Rbac.Application.Security;
using Rbac.Domain.Groups;
using Rbac.Domain.ValueObjects;

namespace Rbac.Api.Controllers;

/// <summary>
/// 权限组管理接口。
///
/// 写操作：WriteGuard 先从 MySQL 加载聚合根，再通过 IRbacManagementWriteService 持久化 + Outbox。
/// 读操作：ES 查询（管理端列表），MySQL 查询（详情/成员列表）。
/// project 来自 CurrentRbacContext，不信任 Request body 中的 project 字段。
/// </summary>
[ApiController]
[Route("api/group")]
public sealed partial class GroupController : ControllerBase
{
    private readonly ICurrentRbacContextAccessor _ctx;
    private readonly IRbacManagementSearchService _search;
    private readonly IRbacManagementWriteService _write;
    private readonly RbacManagementWriteGuard _guard;
    private readonly IRbacDxEIdGenerator _idGen;
    private readonly IGroupRepository _groupRepo;

    public GroupController(
        ICurrentRbacContextAccessor ctx,
        IRbacManagementSearchService search,
        IRbacManagementWriteService write,
        RbacManagementWriteGuard guard,
        IRbacDxEIdGenerator idGen,
        IGroupRepository groupRepo)
    {
        _ctx = ctx;
        _search = search;
        _write = write;
        _guard = guard;
        _idGen = idGen;
        _groupRepo = groupRepo;
    }

    // ── 列表 ──────────────────────────────────────────────────────

    /// <summary>GET /api/group/list — ES 分页查询权限组列表。</summary>
    [HttpGet("list")]
    public async Task<ApiResponse<PagedData<GroupSearchResult>>> List(
        [FromQuery] GroupSearchQuery query, CancellationToken ct)
    {
        query.Project = RequireContext().Project;
        return ApiResponse<PagedData<GroupSearchResult>>.Ok(
            await _search.SearchGroupsAsync(query, ct));
    }

    // ── 创建 ──────────────────────────────────────────────────────

    /// <summary>POST /api/group — 新建权限组。</summary>
    [HttpPost]
    public async Task<ApiResponse<object>> Create(
        [FromBody] CreateGroupRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(req.GroupCode)) return Fail(40001, "groupCode 不能为空");
        if (string.IsNullOrWhiteSpace(req.GroupName)) return Fail(40001, "groupName 不能为空");

        var group = RbacGroup.Create(
            Guid.NewGuid(),
            new DxEId(_idGen.Generate()),
            new GroupCode(req.GroupCode),
            new ProjectCode(ctx.Project),
            req.GroupName,
            parentGroupCode: string.IsNullOrWhiteSpace(req.ParentGroupCode)
                ? null : new GroupCode(req.ParentGroupCode));

        await _write.SaveGroupAsync(
            group,
            changedFields: new[] { "created" },
            oldRuleCodes: Array.Empty<string>(),
            oldPermissionCodes: Array.Empty<string>(),
            affectedUserids: Array.Empty<string>(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(new { dxeId = group.DxEId.Value });
    }

    // ── 更新规则/权限码 ────────────────────────────────────────────

    /// <summary>PUT /api/group/{dxeId}/rules — 更新权限组的 ruleCodes + permissionCodes。</summary>
    [HttpPut("{dxeId}/rules")]
    public async Task<ApiResponse<object>> UpdateRules(
        string dxeId, [FromBody] UpdateGroupRulesRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        var group = await _guard.LoadGroupByDxEIdAsync(dxeId, ctx.Project, ct);
        if (group is null) return Fail(40400, "权限组不存在");

        var oldRuleCodes = group.RuleCodes.Select(r => r.Value).ToList();
        var oldPermCodes = group.PermissionCodes.Select(p => p.Value).ToList();

        var newRuleCodes = (req.RuleCodes ?? Array.Empty<string>())
            .Select(r => new RuleCode(r)).ToList();
        var newPermCodes = (req.PermissionCodes ?? Array.Empty<string>())
            .Select(p => new PermissionCode(p)).ToList();

        group.UpdateRules(newRuleCodes, newPermCodes);

        // affectedUserids 由调用方提供；如未提供则从 MySQL 查询
        var affectedUserids = req.AffectedUserids ?? Array.Empty<string>();

        await _write.SaveGroupAsync(
            group,
            changedFields: new[] { "ruleCodes", "permissionCodes" },
            oldRuleCodes,
            oldPermCodes,
            affectedUserids,
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // ── 变更状态 ──────────────────────────────────────────────────

    /// <summary>PUT /api/group/{dxeId}/status — 启用/禁用权限组。</summary>
    [HttpPut("{dxeId}/status")]
    public async Task<ApiResponse<object>> ChangeStatus(
        string dxeId, [FromBody] ChangeGroupStatusRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        var group = await _guard.LoadGroupByDxEIdAsync(dxeId, ctx.Project, ct);
        if (group is null) return Fail(40400, "权限组不存在");

        if (req.Status == "Disabled") group.Disable();
        else group.Enable();

        await _write.SaveGroupAsync(
            group,
            changedFields: new[] { "status" },
            oldRuleCodes: group.RuleCodes.Select(r => r.Value).ToList(),
            oldPermissionCodes: group.PermissionCodes.Select(p => p.Value).ToList(),
            affectedUserids: req.AffectedUserids ?? Array.Empty<string>(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // ── 成员管理 ──────────────────────────────────────────────────

    /// <summary>POST /api/group/{dxeId}/members — 将用户加入权限组。</summary>
    [HttpPost("{dxeId}/members")]
    public async Task<ApiResponse<object>> AddMember(
        string dxeId, [FromBody] GroupMemberRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(req.Userid)) return Fail(40001, "userid 不能为空");

        var group = await _guard.LoadGroupByDxEIdAsync(dxeId, ctx.Project, ct);
        if (group is null) return Fail(40400, "权限组不存在");

        var member = RbacGroupMember.Create(
            Guid.NewGuid(),
            new UserId(req.Userid),
            group.GroupCode,
            group.Project,
            grantedBy: ctx.Userid);

        await _write.SaveGroupMemberAsync(
            member,
            affectedUserids: new[] { req.Userid },
            groupPermissionCodes: group.PermissionCodes.Select(p => p.Value).ToList(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    /// <summary>DELETE /api/group/{dxeId}/members/{userid} — 将用户从权限组移除。</summary>
    [HttpDelete("{dxeId}/members/{userid}")]
    public async Task<ApiResponse<object>> RemoveMember(
        string dxeId, string userid, CancellationToken ct)
    {
        var ctx = RequireContext();

        var group = await _guard.LoadGroupByDxEIdAsync(dxeId, ctx.Project, ct);
        if (group is null) return Fail(40400, "权限组不存在");

        var memberRepo = HttpContext.RequestServices
            .GetRequiredService<IGroupMemberRepository>();
        var member = (await memberRepo.FindByUseridAndProjectAsync(userid, ctx.Project, ct))
            .FirstOrDefault(m => m.GroupCode == group.GroupCode);

        if (member is null) return Fail(40400, "成员关系不存在");

        await _write.DeleteGroupMemberAsync(
            member,
            affectedUserids: new[] { userid },
            groupPermissionCodes: group.PermissionCodes.Select(p => p.Value).ToList(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private CurrentRbacContext RequireContext() =>
        _ctx.Context ?? throw new InvalidOperationException("RbacContext missing");

    private static ApiResponse<object> Fail(int code, string msg) =>
        ApiResponse<object>.Fail(code, msg);
}

// ── Request DTOs ───────────────────────────────────────────────────

public sealed record CreateGroupRequest(
    string GroupCode,
    string GroupName,
    string? ParentGroupCode);

public sealed record UpdateGroupRulesRequest(
    string[]? RuleCodes,
    string[]? PermissionCodes,
    string[]? AffectedUserids);

public sealed record ChangeGroupStatusRequest(
    string Status,
    string[]? AffectedUserids);

public sealed record GroupMemberRequest(string Userid);
