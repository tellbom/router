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
    private readonly IRbacManagementWriteService _write;
    private readonly RbacManagementWriteGuard _guard;
    private readonly IGlobalManagementService _globalService;
    private readonly IProjectGrantRepository _grantRepo;
    private readonly IAdministratorRepository _adminRepo;
    private readonly IGroupMemberRepository _memberRepo;
    private readonly IGroupRepository _groupRepo;

    public GlobalUserController(
        ICurrentRbacContextAccessor ctx,
        IRbacManagementWriteService write,
        RbacManagementWriteGuard guard,
        IGlobalManagementService globalService,
        IProjectGrantRepository grantRepo,
        IAdministratorRepository adminRepo,
        IGroupMemberRepository memberRepo,
        IGroupRepository groupRepo)
    {
        _ctx           = ctx;
        _write         = write;
        _guard         = guard;
        _globalService = globalService;
        _grantRepo     = grantRepo;
        _adminRepo     = adminRepo;
        _memberRepo    = memberRepo;
        _groupRepo     = groupRepo;
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
        var data = await SearchUsersFromDmAsync(query, ct);
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

    private async Task<PagedData<UserSearchResult>> SearchUsersFromDmAsync(
        UserSearchQuery query, CancellationToken ct)
    {
        var targetProject = RbacGlobalConstants.IsReservedProject(query.Project)
            ? null
            : query.Project;

        var admins = string.IsNullOrWhiteSpace(targetProject)
            ? await _adminRepo.FindByProjectAsync(new ProjectCode("*"), ct)
            : await _adminRepo.FindByProjectAsync(new ProjectCode(targetProject), ct);

        var rows = new List<UserSearchResult>();
        foreach (var admin in admins)
        {
            var grants = await _grantRepo.FindByUseridAsync(admin.Userid, ct);
            if (!string.IsNullOrWhiteSpace(targetProject) &&
                !grants.Any(g => string.Equals(g.Project.Value, targetProject, StringComparison.OrdinalIgnoreCase)))
                continue;

            var projectCodes = grants.Select(g => g.Project.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var superProjects = grants.Where(g => g.IsSuper)
                .Select(g => g.Project.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var groupCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in projectCodes)
            {
                var members = await _memberRepo.FindByUseridAndProjectAsync(
                    admin.Userid.Value, project, ct);
                foreach (var member in members)
                {
                    groupCodes.Add(member.GroupCode.Value);

                    var group = await _groupRepo.FindByGroupCodeAsync(
                        member.GroupCode, member.Project, ct);
                    if (group is not null) groupNames.Add(group.GroupName);
                }
            }

            rows.Add(new UserSearchResult
            {
                Userid = admin.Userid.Value,
                Username = admin.Username,
                Status = admin.Status.ToString(),
                ProjectCodes = projectCodes,
                GroupCodes = groupCodes.ToList(),
                GroupNames = groupNames.ToList(),
                SuperProjects = superProjects,
                IsSuper = !string.IsNullOrWhiteSpace(targetProject)
                    && superProjects.Contains(targetProject, StringComparer.OrdinalIgnoreCase),
            });
        }

        rows = rows.Where(r => MatchesUserQuery(r, query, targetProject)).ToList();

        return new PagedData<UserSearchResult>
        {
            List = rows.Skip(query.Offset).Take(query.PageSize).ToList(),
            Total = rows.Count,
        };
    }

    private static bool MatchesUserQuery(
        UserSearchResult row, UserSearchQuery query, string? targetProject)
    {
        if (!string.IsNullOrWhiteSpace(query.Userid) &&
            !string.Equals(row.Userid, query.Userid, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            !string.Equals(row.Status, query.Status, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(query.GroupCode) &&
            !row.GroupCodes.Contains(query.GroupCode, StringComparer.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(targetProject) &&
            !row.ProjectCodes.Contains(targetProject, StringComparer.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(query.Keyword)) return true;

        return Contains(row.Userid, query.Keyword)
               || Contains(row.Username, query.Keyword)
               || Contains(row.Status, query.Keyword)
               || row.ProjectCodes.Any(v => Contains(v, query.Keyword))
               || row.GroupCodes.Any(v => Contains(v, query.Keyword))
               || row.GroupNames.Any(v => Contains(v, query.Keyword));
    }

    private static bool Contains(string value, string keyword) =>
        value.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static ApiResponse<object> Fail(int code, string msg) =>
        ApiResponse<object>.Fail(code, msg);

    private static ApiResponse<PerProjectResultReport> FailReport(int code, string msg) =>
        ApiResponse<PerProjectResultReport>.Fail(code, msg);
}

// ── Request DTOs ───────────────────────────────────────────────────────

public sealed record GlobalChangeStatusRequest(string Status);

public sealed record GlobalToggleSuperRequest(bool IsSuper);

public sealed record GrantToProjectsRequest(
    IReadOnlyList<string> TargetProjects,
    string? Username = null,
    bool? IsSuper = null);
