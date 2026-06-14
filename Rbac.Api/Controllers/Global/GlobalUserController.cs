using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Global;
using Rbac.Application.Management;
using Rbac.Application.Search;
using Rbac.Application.Security;
using Rbac.Domain.ValueObjects;

namespace Rbac.Api.Controllers.Global;

/// <summary>
/// 跨 project 用户管理接口。
/// 调用方必须携带 X-Project: __global__ header 并持有对应授权。
/// 授权由现有 RbacAuthorizationFilter 通过 rbac.global.user.manage 权限码验证，无特殊逻辑。
///
/// 目标 project 来自请求 body/path，不来自 CurrentRbacContext.Project（固定为 __global__）。
/// </summary>
[ApiController]
[Route("api/global/user")]
public sealed class GlobalUserController : ControllerBase
{
    private readonly ICurrentRbacContextAccessor _ctx;
    private readonly IRbacManagementSearchService _search;
    private readonly IRbacManagementWriteService _write;
    private readonly RbacManagementWriteGuard _guard;
    private readonly IGlobalManagementService _globalService;

    public GlobalUserController(
        ICurrentRbacContextAccessor ctx,
        IRbacManagementSearchService search,
        IRbacManagementWriteService write,
        RbacManagementWriteGuard guard,
        IGlobalManagementService globalService)
    {
        _ctx           = ctx;
        _search        = search;
        _write         = write;
        _guard         = guard;
        _globalService = globalService;
    }

    // ── 跨项目用户搜索 ──────────────────────────────────────────────

    /// <summary>
    /// GET /api/global/user/list — 跨项目用户搜索。
    /// query.Project 来自调用方，null 时搜索所有项目（ES builder 跳过 null 过滤条件）。
    /// 权限码：rbac.global.user.manage : access
    /// </summary>
    [HttpGet("list")]
    public async Task<ApiResponse<PagedData<UserSearchResult>>> List(
        [FromQuery] UserSearchQuery query, CancellationToken ct)
    {
        // 不覆盖 query.Project：由调用方传入目标项目；null = 全项目搜索
        var data = await _search.SearchUsersAsync(query, ct);
        return ApiResponse<PagedData<UserSearchResult>>.Ok(data);
    }

    // ── 用户状态变更（全局单次写入，非 fan-out）───────────────────

    /// <summary>
    /// PUT /api/global/user/{userid}/status — 变更管理员账号状态。
    /// rbac_administrator 无 project 字段，状态变更为全局操作（单次写入）。
    /// 权限码：rbac.global.user.manage : write
    /// </summary>
    [HttpPut("{userid}/status")]
    public async Task<ApiResponse<object>> ChangeStatus(
        string userid, [FromBody] GlobalChangeStatusRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        var admin = await _guard.LoadAdminByUseridAsync(userid, ct);
        if (admin is null) return Fail(40400, "管理员不存在");

        var oldStatus = admin.Status.ToString();
        if (req.Status == "Disabled") admin.Disable();
        else admin.Enable();

        await _write.SaveAdministratorAsync(
            admin,
            changedFields: new[] { "status" },
            oldStatus: oldStatus,
            affectedGroupCodes: Array.Empty<string>(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // ── 跨项目授权 fan-out ─────────────────────────────────────────

    /// <summary>
    /// POST /api/global/user/{userid}/project-grants — 将用户授权到指定 project 列表（fan-out）。
    /// 已有授权的 project 跳过（幂等）；用户不存在且提供 username 时自动创建账号。
    /// 权限码：rbac.global.user.manage : write
    /// </summary>
    [HttpPost("{userid}/project-grants")]
    public async Task<ApiResponse<PerProjectResultReport>> GrantToProjects(
        string userid, [FromBody] GrantToProjectsRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        if (req.TargetProjects is null || req.TargetProjects.Count == 0)
            return FailReport(40001, "targetProjects 不能为空");

        var report = await _globalService.GrantUserToProjectsAsync(
            userid,
            req.Username,
            req.TargetProjects,
            req.IsSuper,
            ctx.Userid,
            ct);

        return ApiResponse<PerProjectResultReport>.Ok(report);
    }

    /// <summary>
    /// DELETE /api/global/user/{userid}/project-grants/{project} — 撤销用户在指定 project 的授权。
    /// 未授权则跳过（幂等）。
    /// 权限码：rbac.global.user.manage : write
    /// </summary>
    [HttpDelete("{userid}/project-grants/{project}")]
    public async Task<ApiResponse<PerProjectResultReport>> RevokeFromProject(
        string userid, string project, CancellationToken ct)
    {
        var ctx = RequireContext();

        var report = await _globalService.RevokeUserFromProjectsAsync(
            userid,
            new[] { project },
            ctx.Userid,
            ct);

        return ApiResponse<PerProjectResultReport>.Ok(report);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private CurrentRbacContext RequireContext() =>
        _ctx.Context ?? throw new InvalidOperationException("RbacContext missing");

    private static ApiResponse<object> Fail(int code, string msg) =>
        ApiResponse<object>.Fail(code, msg);

    private static ApiResponse<PerProjectResultReport> FailReport(int code, string msg) =>
        ApiResponse<PerProjectResultReport>.Fail(code, msg);
}

// ── Request DTOs ───────────────────────────────────────────────────────

public sealed record GlobalChangeStatusRequest(string Status);

public sealed record GrantToProjectsRequest(
    IReadOnlyList<string> TargetProjects,
    string? Username = null,
    bool IsSuper = false);
