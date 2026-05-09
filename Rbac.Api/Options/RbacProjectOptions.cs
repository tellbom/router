namespace Rbac.Api.Options;

/// <summary>
/// project 来源配置选项。绑定到 appsettings.json "Project" 节。
/// 推荐使用 Header（X-Project），其他来源作为兼容降级。
/// </summary>
public sealed class RbacProjectOptions
{
    public const string SectionName = "Project";

    /// <summary>
    /// 默认 project 读取来源。推荐 Header。
    /// </summary>
    public ProjectSource DefaultSource { get; set; } = ProjectSource.Header;

    /// <summary>
    /// Header 来源时使用的 header 名称。默认 X-Project。
    /// </summary>
    public string HeaderName { get; set; } = "X-Project";

    /// <summary>
    /// Query 来源时使用的参数名称。
    /// </summary>
    public string QueryParamName { get; set; } = "project";

    /// <summary>
    /// Route 来源时使用的路由参数名称。
    /// </summary>
    public string RouteParamName { get; set; } = "project";

    /// <summary>
    /// 允许的 project 来源列表。按优先级顺序尝试，第一个非空值生效。
    /// 生产环境建议只保留 Header，避免 Query/Body 来源被伪造。
    /// </summary>
    public IList<ProjectSource> AllowedSources { get; set; } = new List<ProjectSource>
    {
        ProjectSource.Header,
        ProjectSource.Route,
        ProjectSource.Query,
    };
}

/// <summary>
/// project 参数来源枚举。
/// </summary>
public enum ProjectSource
{
    /// <summary>从 HTTP Header 读取（推荐，默认 X-Project）。</summary>
    Header,

    /// <summary>从路由参数读取（例如 /api/{project}/users）。</summary>
    Route,

    /// <summary>从 Query String 读取（例如 ?project=news）。</summary>
    Query,

    /// <summary>从 Request Body 读取（仅 POST/PUT，不推荐热路径使用）。</summary>
    Body,
}
