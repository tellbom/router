namespace Rbac.Api.Options;

/// <summary>
/// 匿名/白名单路由集中配置（T107）。
///
/// 规则：
/// - 所有白名单路由必须在此集中注册，禁止在 Controller 或 Service 中散落配置。
/// - allowlist 命中记录基础访问日志，但不做 project 校验。
/// - allowlist 不得包含任何管理操作接口。
///
/// 绑定到 appsettings.json "Allowlist" 节。
/// </summary>
public sealed class RbacAllowlistOptions
{
    public const string SectionName = "Allowlist";

    /// <summary>
    /// 允许匿名访问的路由前缀或精确路径列表。
    /// 支持通配符前缀，例如 "/swagger" 将匹配所有 /swagger/* 路径。
    /// </summary>
    public IList<string> Routes { get; set; } = new List<string>
    {
        "/api/auth/login",
        "/health",
        "/healthz",
        "/swagger",
        "/favicon.ico",
    };

    /// <summary>
    /// 允许匿名访问的 HTTP 方法（通常 OPTIONS 用于 CORS preflight）。
    /// </summary>
    public IList<string> AllowedMethods { get; set; } = new List<string> { "OPTIONS" };

    /// <summary>
    /// 判断请求是否命中白名单。
    /// 路径前缀匹配（大小写不敏感）或 HTTP 方法匹配。
    /// </summary>
    public bool IsAllowed(string path, string method)
    {
        if (AllowedMethods.Any(m => string.Equals(m, method, StringComparison.OrdinalIgnoreCase)))
            return true;

        foreach (var route in Routes)
        {
            if (path.StartsWith(route, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
