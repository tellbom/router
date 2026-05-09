using Microsoft.AspNetCore.Http;

namespace Rbac.Application.Authorization;

/// <summary>
/// API 路由 → permissionCode + action 映射契约。
/// 匹配使用 ASP.NET Core RouteTemplate.TryParse + TemplateMatcher。
/// </summary>
public interface IRbacApiPermissionMapper
{
    Task<ApiPermissionMapping?> ResolveAsync(
        string project,
        HttpContext context,
        CancellationToken ct = default);
}

/// <summary>API 路由对应的权限映射结果。</summary>
public sealed class ApiPermissionMapping
{
    public string PermissionCode { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string MatchedRoutePattern { get; init; } = string.Empty;
}
