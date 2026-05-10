using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.Logging;
using Rbac.Application.Authorization;
using Rbac.Application.Repositories;
using Rbac.Domain.ValueObjects;
using Rbac.Infrastructure.Redis;

namespace Rbac.Api.Authorization;

/// <summary>
/// PATCH-04: IRbacApiPermissionMapper 的实现。
///
/// 匹配流程：
/// 1. 通过 RbacFusionCacheFacade 从 FusionCache/Redis 读取 project 下所有 Active API 映射。
///    缓存未命中时从 IApiPermissionMapRepository 回源 MySQL。
/// 2. 按 HttpMethod 预过滤，再对每条记录用 ASP.NET Core RouteTemplate + TemplateMatcher 匹配路径。
/// 3. 命中第一条返回 ApiPermissionMapping；未命中返回 null（由 Filter 按 deny-by-default 处理）。
///
/// 注意：RouteTemplate / TemplateMatcher 来自 Microsoft.AspNetCore.Routing，
/// 已随 Microsoft.NET.Sdk.Web 内置引入，不需要额外 PackageReference。
/// </summary>
public sealed class RoutePatternApiPermissionMapper : IRbacApiPermissionMapper
{
    private readonly IApiPermissionMapRepository _repo;
    private readonly RbacFusionCacheFacade _cache;
    private readonly ILogger<RoutePatternApiPermissionMapper> _logger;

    public RoutePatternApiPermissionMapper(
        IApiPermissionMapRepository repo,
        RbacFusionCacheFacade cache,
        ILogger<RoutePatternApiPermissionMapper> logger)
    {
        _repo = repo;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ApiPermissionMapping?> ResolveAsync(
        string project,
        HttpContext context,
        CancellationToken ct = default)
    {
        var method = context.Request.Method.ToUpperInvariant();
        var path   = context.Request.Path.Value ?? string.Empty;

        // 从 FusionCache 取（缓存未命中时回源 MySQL）
        var maps = await _cache.GetApiMapAsync<List<ApiMapCacheEntry>>(
            project,
            async (_ct) =>
            {
                var dbMaps = await _repo.FindActiveByProjectAsync(new ProjectCode(project), _ct);
                return dbMaps.Select(m => new ApiMapCacheEntry(
                    m.HttpMethod,
                    m.RoutePattern,
                    m.PermissionCode.Value,
                    m.Action)).ToList();
            },
            ct);

        if (maps is null || maps.Count == 0)
        {
            _logger.LogDebug("No api-map entries for project={P}", project);
            return null;
        }

        // 按 HttpMethod 过滤后逐条匹配路径
        foreach (var entry in maps.Where(m => m.HttpMethod == method))
        {
            RouteTemplate template;
            try
            {
                template = TemplateParser.Parse(entry.RoutePattern);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid route pattern={Pat}", entry.RoutePattern);
                continue;
            }

            var matcher  = new TemplateMatcher(template, new RouteValueDictionary());
            var routeValues = new RouteValueDictionary();

            if (matcher.TryMatch(path, routeValues))
            {
                _logger.LogDebug(
                    "Matched pattern={Pat} permCode={PC} action={A}",
                    entry.RoutePattern, entry.PermissionCode, entry.Action);

                return new ApiPermissionMapping
                {
                    PermissionCode      = entry.PermissionCode,
                    Action              = entry.Action,
                    MatchedRoutePattern = entry.RoutePattern,
                };
            }
        }

        _logger.LogDebug("No match for method={M} path={Path} project={P}", method, path, project);
        return null;
    }

    // ── 缓存条目（JSON 序列化友好，无 Domain 类型依赖）──────────────

    private sealed record ApiMapCacheEntry(
        string HttpMethod,
        string RoutePattern,
        string PermissionCode,
        string Action);
}
