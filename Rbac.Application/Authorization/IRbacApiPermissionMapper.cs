using Microsoft.AspNetCore.Http;

namespace Rbac.Application.Authorization;

/// <summary>
/// API 路由 → permissionCode + action 映射契约。
///
/// 路由匹配算法规定：
/// - routePattern 使用 ASP.NET Core route template 语法（如 /api/users/{id}）。
/// - 匹配时必须使用 RouteTemplate.TryParse + TemplateMatcher，与框架保持一致。
/// - 禁止使用字符串前缀、手写正则或大小写不一致规则替代框架匹配。
/// - 同一 project + httpMethod + routePattern 只能映射一个 permissionCode + action。
///
/// 缓存策略：
/// - API 映射表通过 FusionCache 缓存（key: rbac:api-map:{project}），TTL 60min。
/// - 变更后通过 Outbox 发布 ApiMapChanged 事件，递增版本并驱逐缓存。
/// </summary>
public interface IRbacApiPermissionMapper
{
    /// <summary>
    /// 根据当前 HTTP 请求解析出对应的 permissionCode 和 action。
    /// </summary>
    /// <param name="project">已校验的项目标识。</param>
    /// <param name="context">当前 HTTP 请求上下文。</param>
    /// <param name="ct"></param>
    /// <returns>
    /// 成功时返回 <see cref="ApiPermissionMapping"/>；
    /// 路由未配置权限映射时返回 null（调用方应按 deny-by-default 处理）。
    /// </returns>
    Task<ApiPermissionMapping?> ResolveAsync(
        string project,
        HttpContext context,
        CancellationToken ct = default);
}

/// <summary>
/// API 路由对应的权限映射结果。
/// </summary>
public sealed class ApiPermissionMapping
{
    /// <summary>权限码，例如 api:system.user.create。</summary>
    public required string PermissionCode { get; init; }

    /// <summary>操作类型，例如 execute / read / create。</summary>
    public required string Action { get; init; }

    /// <summary>匹配到的路由模板，用于调试和审计。</summary>
    public required string MatchedRoutePattern { get; init; }
}
