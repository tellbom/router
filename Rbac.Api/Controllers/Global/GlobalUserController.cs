using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Global;
using Rbac.Application.Management;
using Rbac.Application.Repositories;
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
    private readonly IProjectGrantRepository _grantRepo;
    private readonly RbacUserSearchProjectScopeService _projectScope;

    public GlobalUserController(
        ICurrentRbacContextAccessor ctx,
        IRbacManagementSearchService search,
        IRbacManagementWriteService write,
        RbacManagementWriteGuard guard,
        IGlobalManagementService globalService,
        IProjectGrantRepository grantRepo,
        RbacUserSearchProjectScopeService projectScope)
    {
        _ctx           = ctx;
        _search        = search;
        _write         = write;
        _guard         = guard;
        _globalService = globalService;
        _grantRepo     = grantRepo;
        _projectScope  = projectScope;
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
        var ctx = RequireContext();
        if (!RbacGlobalConstants.IsReservedProject(ctx.Project))
            return ApiResponse<PagedData<UserSearchResult>>.Fail(
                40303, "global 用户端点只允许 X-Project: __global__ 上下文访问");

        var data = await _search.SearchUsersAsync(query, ct);

        if (!string.IsNullOrWhiteSpace(query.Project) && data.List.Count > 0)
            data = await _projectScope.ScopeAsync(
                data, query.Project, preserveCrossProjectGrants: true, ct);

        data = RbacUserSearchProjectScopeService.SetIsSuperForProject(
            data, RbacGlobalConstants.ReservedProjectCode);

        return ApiResponse<PagedData<UserSearchResult>>.Ok(data);
    }

    [HttpPost]
    public async Task<ApiResponse<PerProjectResultReport>> Create(
        [FromBody] GlobalCreateUserRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        if (string.IsNullOrWhiteSpace(req.Userid))
            return FailReport(40001, "userid 涓嶈兘涓虹┖");
        if (string.IsNullOrWhiteSpace(req.Username))
            return FailReport(40001, "username 涓嶈兘涓虹┖");
        if (req.TargetProjects is null || req.TargetProjects.Count == 0)
            return FailReport(40001, "targetProjects 涓嶈兘涓虹┖");

        var report = await _globalService.GrantUserToProjectsAsync(
            req.Userid,
            req.Username,
            req.TargetProjects,
            req.IsSuper,
            ctx.Userid,
            ct);

        return ApiResponse<PerProjectResultReport>.Ok(report);
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

    [HttpPut("{userid}")]
    public async Task<ApiResponse<object>> Update(
        string userid, [FromBody] GlobalUpdateUserRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        var admin = await _guard.LoadAdminByUseridAsync(userid, ct);
        if (admin is null) return Fail(40400, "绠＄悊鍛樹笉瀛樺湪");

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
                oldStatus,
                affectedGroupCodes: Array.Empty<string>(),
                operatorUserid: ctx.Userid,
                ct);
        }

        return ApiResponse<object>.Ok(null!);
    }

    /// <summary>
    /// DELETE /api/global/user/{userid} — 物理删除管理员账号。
    /// rbac_administrator 无 project 字段，删除为全局操作（单次写入）。
    /// 权限码：rbac.global.user.manage : write
    /// </summary>
    [HttpDelete("{userid}")]
    public async Task<ApiResponse<object>> Delete(string userid, CancellationToken ct)
    {
        var ctx = RequireContext();

        var admin = await _guard.LoadAdminByUseridAsync(userid, ct);
        if (admin is null) return Fail(40400, "管理员不存在");

        await _write.DeleteAdministratorAsync(admin, operatorUserid: ctx.Userid, ct);
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
    /// PUT /api/global/user/{userid}/project-grants/{project}/super — 切换用户在指定 project 的 super 状态。
    /// 目标 project 来自 path，不来自 X-Project。
    /// 权限码：rbac.global.user.manage : write
    /// </summary>
    [HttpPut("{userid}/project-grants/{project}/super")]
    public async Task<ApiResponse<object>> ToggleProjectSuper(
        string userid, string project, [FromBody] GlobalToggleSuperRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        if (RbacGlobalConstants.IsReservedProject(project))
            return Fail(40009, "不允许操作保留系统 project");

        var grant = await _grantRepo.FindAsync(
            new UserId(userid),
            new ProjectCode(project),
            ct);
        if (grant is null) return Fail(40400, "未找到授权记录，请先授权用户到此 project");

        var oldSuper = grant.IsSuper;
        string grantKind;
        if (req.IsSuper) { grant.GrantSuper(); grantKind = "SuperGranted"; }
        else { grant.RevokeSuper(); grantKind = "SuperRevoked"; }

        await _write.SaveProjectGrantAsync(
            grant,
            grantKind,
            oldProjects: new[] { project },
            newProjects: new[] { project },
            oldSuper,
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
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

public sealed record GlobalToggleSuperRequest(bool IsSuper);

public sealed record GlobalCreateUserRequest(
    string Userid,
    string Username,
    IReadOnlyList<string> TargetProjects,
    bool? IsSuper = null);

public sealed record GlobalUpdateUserRequest(
    string? Username,
    string? Status);

public sealed record GrantToProjectsRequest(
    IReadOnlyList<string> TargetProjects,
    string? Username = null,
    bool? IsSuper = null);
