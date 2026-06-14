using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Global;
using Rbac.Application.Search;
using Rbac.Application.Security;

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
    private readonly IGlobalManagementService _globalService;

    public GlobalGroupController(
        ICurrentRbacContextAccessor ctx,
        IRbacManagementSearchService search,
        IGlobalManagementService globalService)
    {
        _ctx           = ctx;
        _search        = search;
        _globalService = globalService;
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

    private static ApiResponse<PerProjectResultReport> FailReport(int code, string msg) =>
        ApiResponse<PerProjectResultReport>.Fail(code, msg);
}

// ── Request DTOs ───────────────────────────────────────────────────────

public sealed record GroupMemberRequest(string Userid, string TargetProject);
