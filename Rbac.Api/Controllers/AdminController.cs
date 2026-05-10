using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Backend;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Contracts.Compatibility;
using Rbac.Application.Identity;
using Rbac.Application.Management;
using Rbac.Application.Search;
using Rbac.Application.Security;
using Rbac.Domain.Users;
using Rbac.Domain.ValueObjects;

namespace Rbac.Api.Controllers;

/// <summary>
/// 管理员账号管理 + 后台首页初始化接口。
///
/// 约束：
/// - 所有写操作先通过 RbacManagementWriteGuard 从 MySQL 重新加载聚合根。
/// - project 来自 CurrentRbacContext，不从 Request body 读取。
/// - DxEId 对外必须为 string（由 LongToStringConverter 全局保证）。
/// </summary>
[ApiController]
[Route("api/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly ICurrentRbacContextAccessor _ctx;
    private readonly RbacBackendIndexService _indexService;
    private readonly IRbacManagementSearchService _search;
    private readonly IRbacManagementWriteService _write;
    private readonly RbacManagementWriteGuard _guard;
    private readonly IRbacDxEIdGenerator _idGen;

    public AdminController(
        ICurrentRbacContextAccessor ctx,
        RbacBackendIndexService indexService,
        IRbacManagementSearchService search,
        IRbacManagementWriteService write,
        RbacManagementWriteGuard guard,
        IRbacDxEIdGenerator idGen)
    {
        _ctx = ctx;
        _indexService = indexService;
        _search = search;
        _write = write;
        _guard = guard;
        _idGen = idGen;
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
        var project = RequireContext().Project;
        query.Project = project;
        var data = await _search.SearchUsersAsync(query, ct);
        return ApiResponse<PagedData<UserSearchResult>>.Ok(data);
    }

    // ── 创建管理员 ─────────────────────────────────────────────────

    /// <summary>POST /api/admin — 新增管理员账号。</summary>
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
            new DxEId(_idGen.Generate()),
            new UserId(req.Userid),
            req.Username);

        await _write.SaveAdministratorAsync(
            admin,
            changedFields: new[] { "created" },
            oldStatus: null,
            affectedGroupCodes: Array.Empty<string>(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(new { dxeId = admin.DxEId.Value });
    }

    // ── 禁用/启用 ──────────────────────────────────────────────────

    /// <summary>PUT /api/admin/{dxeId}/status — 变更管理员状态（Active / Disabled）。</summary>
    [HttpPut("{dxeId}/status")]
    public async Task<ApiResponse<object>> ChangeStatus(
        string dxeId, [FromBody] ChangeStatusRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        var admin = await _guard.LoadAdminByDxEIdAsync(dxeId, ct);
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

    /// <summary>PUT /api/admin/{dxeId}/username — 更新管理员显示名称。</summary>
    [HttpPut("{dxeId}/username")]
    public async Task<ApiResponse<object>> UpdateUsername(
        string dxeId, [FromBody] UpdateUsernameRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(req.Username))
            return Fail(40001, "username 不能为空");

        var admin = await _guard.LoadAdminByDxEIdAsync(dxeId, ct);
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

public sealed record CreateAdminRequest(string Userid, string Username);
public sealed record ChangeStatusRequest(string Status);
public sealed record UpdateUsernameRequest(string Username);
