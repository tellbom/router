using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Search;
using Rbac.Application.Security;

namespace Rbac.Api.Controllers;

/// <summary>
/// 只读搜索接口（ES 查询）。
///
/// 所有接口只读，不产生写操作，也不产生 Outbox 事件。
/// project 固定为 CurrentRbacContext.Project，防止跨 project 数据泄漏。
/// </summary>
[ApiController]
[Route("api/search")]
public sealed class SearchController : ControllerBase
{
    private readonly ICurrentRbacContextAccessor _ctx;
    private readonly IRbacManagementSearchService _search;

    public SearchController(
        ICurrentRbacContextAccessor ctx,
        IRbacManagementSearchService search)
    {
        _ctx = ctx;
        _search = search;
    }

    /// <summary>GET /api/search/audit-logs — 查询鉴权审计日志。</summary>
    [HttpGet("audit-logs")]
    public async Task<ApiResponse<PagedData<AuditLogSearchResult>>> AuditLogs(
        [FromQuery] AuditLogSearchQuery query, CancellationToken ct)
    {
        var project = RequireContext().Project;
        query.Project = project;
        return ApiResponse<PagedData<AuditLogSearchResult>>.Ok(
            await _search.SearchAuditLogsAsync(query, ct));
    }

    /// <summary>GET /api/search/permission-view — 查询权限视图（API → permissionCode 映射视图）。</summary>
    [HttpGet("permission-view")]
    public async Task<ApiResponse<PagedData<PermissionViewSearchResult>>> PermissionView(
        [FromQuery] PermissionViewSearchQuery query, CancellationToken ct)
    {
        var project = RequireContext().Project;
        query.Project = project;
        return ApiResponse<PagedData<PermissionViewSearchResult>>.Ok(
            await _search.SearchPermissionViewAsync(query, ct));
    }

    private CurrentRbacContext RequireContext() =>
        _ctx.Context ?? throw new InvalidOperationException("RbacContext missing");
}
