using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Management;
using Rbac.Application.Search;
using Rbac.Application.Security;
using Rbac.Domain.Permissions;
using Rbac.Domain.ValueObjects;

namespace Rbac.Api.Controllers;

/// <summary>
/// API 路由 → permissionCode 映射管理接口。
///
/// 变更 api-map 后通过 Outbox 触发 Redis api-map 缓存失效 + 版本递增。
/// WriteGuard 先从 MySQL 加载真相，防止 ES 过期数据直接写入。
/// project 来自 CurrentRbacContext。
/// </summary>
[ApiController]
[Route("api/api-map")]
public sealed class ApiMapController : ControllerBase
{
    private readonly ICurrentRbacContextAccessor _ctx;
    private readonly IRbacManagementSearchService _search;
    private readonly IRbacManagementWriteService _write;
    private readonly RbacManagementWriteGuard _guard;
    public ApiMapController(
        ICurrentRbacContextAccessor ctx,
        IRbacManagementSearchService search,
        IRbacManagementWriteService write,
        RbacManagementWriteGuard guard)
    {
        _ctx = ctx;
        _search = search;
        _write = write;
        _guard = guard;
    }

    // ── 权限视图列表（ES）──────────────────────────────────────────

    /// <summary>GET /api/api-map/list — ES 分页查询权限视图。</summary>
    [HttpGet("list")]
    public async Task<ApiResponse<PagedData<PermissionViewSearchResult>>> List(
        [FromQuery] PermissionViewSearchQuery query, CancellationToken ct)
    {
        var project = RequireContext().Project;
        query.Project = project;
        return ApiResponse<PagedData<PermissionViewSearchResult>>.Ok(
            await _search.SearchPermissionViewAsync(query, ct));
    }

    // ── 创建映射 ──────────────────────────────────────────────────

    /// <summary>POST /api/api-map — 新增 API 路由 → permissionCode 映射。</summary>
    [HttpPost]
    public async Task<ApiResponse<object>> Create(
        [FromBody] CreateApiMapRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(req.HttpMethod)) return Fail(40001, "httpMethod 不能为空");
        if (string.IsNullOrWhiteSpace(req.RoutePattern)) return Fail(40001, "routePattern 不能为空");
        if (string.IsNullOrWhiteSpace(req.PermissionCode)) return Fail(40001, "permissionCode 不能为空");
        if (string.IsNullOrWhiteSpace(req.Action)) return Fail(40001, "action 不能为空");

        var map = RbacApiPermissionMap.Create(
            Guid.NewGuid(),
            new ProjectCode(ctx.Project),
            req.HttpMethod,
            req.RoutePattern,
            new PermissionCode(req.PermissionCode),
            req.Action);

        await _write.SaveApiPermissionMapAsync(
            map,
            changeKind: "Created",
            oldPermissionCode: null,
            oldAction: null,
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(new { id = map.Id });
    }

    // ── 更新映射 ──────────────────────────────────────────────────

    /// <summary>PUT /api/api-map/{id} — 更新 API 路由映射。</summary>
    [HttpPut("{id:guid}")]
    public async Task<ApiResponse<object>> Update(
        Guid id, [FromBody] UpdateApiMapRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        var map = await _guard.LoadApiMapByGuidAsync(id, ct);
        if (map is null) return Fail(40400, "API 映射不存在");

        var oldPermCode = map.PermissionCode.Value;
        var oldAction = map.Action;

        map.UpdatePermission(
            new PermissionCode(req.PermissionCode ?? map.PermissionCode.Value),
            req.Action ?? map.Action);

        await _write.SaveApiPermissionMapAsync(
            map,
            changeKind: "Updated",
            oldPermissionCode: oldPermCode,
            oldAction: oldAction,
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // ── 删除映射 ──────────────────────────────────────────────────

    /// <summary>DELETE /api/api-map/{id} — 删除 API 路由映射。</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ApiResponse<object>> Delete(Guid id, CancellationToken ct)
    {
        var ctx = RequireContext();

        var map = await _guard.LoadApiMapByGuidAsync(id, ct);
        if (map is null) return Fail(40400, "API 映射不存在");

        await _write.DeleteApiPermissionMapAsync(
            map,
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

public sealed record CreateApiMapRequest(
    string HttpMethod,
    string RoutePattern,
    string PermissionCode,
    string Action);

public sealed record UpdateApiMapRequest(
    string? PermissionCode,
    string? Action);
