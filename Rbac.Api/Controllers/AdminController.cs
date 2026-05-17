using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Backend;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Contracts.Compatibility;
using Rbac.Application.Management;
using Rbac.Application.Repositories;
using Rbac.Application.Search;
using Rbac.Application.Security;
using Rbac.Domain.Groups;
using Rbac.Domain.Projects;
using Rbac.Domain.Users;
using Rbac.Domain.ValueObjects;

namespace Rbac.Api.Controllers;

/// <summary>
/// 管理员账号管理 + 后台首页初始化接口。
///
/// 约束：
/// - 所有写操作先通过 RbacManagementWriteGuard 从 MySQL 重新加载聚合根。
/// - project 来自 CurrentRbacContext，不从 Request body 读取。
/// </summary>
[ApiController]
[Route("api/admin")]
public sealed partial class AdminController : ControllerBase
{
    private readonly ICurrentRbacContextAccessor _ctx;
    private readonly RbacBackendIndexService _indexService;
    private readonly IRbacManagementSearchService _search;
    private readonly IRbacManagementWriteService _write;
    private readonly RbacManagementWriteGuard _guard;
    public AdminController(
        ICurrentRbacContextAccessor ctx,
        RbacBackendIndexService indexService,
        IRbacManagementSearchService search,
        IRbacManagementWriteService write,
        RbacManagementWriteGuard guard)
    {
        _ctx = ctx;
        _indexService = indexService;
        _search = search;
        _write = write;
        _guard = guard;
    }

    // ── 后台首页初始化 ─────────────────────────────────────────────

    /// <summary>GET /api/admin/index — 后台首页初始化，返回 adminInfo + menus + routePath。</summary>
    [HttpGet("index")]
    public async Task<ApiResponse<BackendIndexDto>> Index(CancellationToken ct)
    {
        var ctx = RequireContext();
        var data = await _indexService.BuildAsync(ctx, ct);
        return ApiResponse<BackendIndexDto>.Ok(data);
    }

    // ── 管理员列表（ES 查询）────────────────────────────────────────

    /// <summary>GET /api/admin/list — ES 分页查询管理员列表。</summary>
    [HttpGet("list")]
    public async Task<ApiResponse<PagedData<UserSearchResult>>> List(
        [FromQuery] UserSearchQuery query, CancellationToken ct)
    {
        // project 固定为当前上下文，忽略 query.Project 防止越权
        query.Project = RequireContext().Project;
        var data = await _search.SearchUsersAsync(query, ct);
        return ApiResponse<PagedData<UserSearchResult>>.Ok(data);
    }

    // ── 创建管理员 ─────────────────────────────────────────────────

    /// <summary>
    /// POST /api/admin — 新增管理员账号，并原子地将其授权到当前 project（普通用户，isSuper=false）。
    /// 两个聚合根在同一事务内提交，保证账号存在则 project 授权必然存在。
    /// </summary>
    [HttpPost]
    public async Task<ApiResponse<object>> Create(
        [FromBody] CreateAdminRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(req.Userid))
            return Fail(40001, "userid 不能为空");
        if (string.IsNullOrWhiteSpace(req.Username))
            return Fail(40001, "username 不能为空");

        var admin = RbacAdministrator.Create(
            Guid.NewGuid(),
            new UserId(req.Userid),
            req.Username);

        var grant = RbacProjectGrant.Create(
            Guid.NewGuid(),
            admin.Userid,
            new ProjectCode(ctx.Project),
            grantedBy: ctx.Userid,
            isSuper: false);

        await _write.CreateAdministratorWithGrantAsync(admin, grant, ctx.Userid, ct);

        if (req.GroupCode is not null)
        {
            var groupRepo = HttpContext.RequestServices
                .GetRequiredService<IGroupRepository>();

            var targetCodes = req.GroupCode
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var code in targetCodes)
            {
                var group = await groupRepo.FindByGroupCodeAsync(
                    new GroupCode(code), new ProjectCode(ctx.Project), ct);
                if (group is null) continue;

                var member = RbacGroupMember.Create(
                    Guid.NewGuid(), admin.Userid, group.GroupCode, group.Project,
                    grantedBy: ctx.Userid);

                await _write.SaveGroupMemberAsync(
                    member,
                    affectedUserids: new[] { admin.Userid.Value },
                    groupPermissionCodes: group.PermissionCodes.Select(p => p.Value).ToList(),
                    operatorUserid: ctx.Userid,
                    ct);
            }
        }

        return ApiResponse<object>.Ok(new { userid = admin.Userid.Value });
    }

    // ── 禁用/启用 ──────────────────────────────────────────────────

    /// <summary>PUT /api/admin/{userid}/status — 变更管理员状态（Active / Disabled）。</summary>
    [HttpPut("{userid}/status")]
    public async Task<ApiResponse<object>> ChangeStatus(
        string userid, [FromBody] ChangeStatusRequest req, CancellationToken ct)
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

    // ── 更新用户名 ─────────────────────────────────────────────────

    /// <summary>PUT /api/admin/{userid}/username — 更新管理员显示名称。</summary>
    [HttpPut("{userid}/username")]
    public async Task<ApiResponse<object>> UpdateUsername(
        string userid, [FromBody] UpdateUsernameRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(req.Username))
            return Fail(40001, "username 不能为空");

        var admin = await _guard.LoadAdminByUseridAsync(userid, ct);
        if (admin is null) return Fail(40400, "管理员不存在");

        admin.UpdateUsername(req.Username);

        await _write.SaveAdministratorAsync(
            admin,
            changedFields: new[] { "username" },
            oldStatus: null,
            affectedGroupCodes: Array.Empty<string>(),
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

public sealed record CreateAdminRequest(string Userid, string Username, string[]? GroupCode);
public sealed record ChangeStatusRequest(string Status);
public sealed record UpdateUsernameRequest(string Username);
