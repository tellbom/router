using System.Text.Json.Serialization;

namespace Rbac.Application.Contracts.Compatibility;

/// <summary>
/// 前端兼容 DTO 约定基类。
/// 所有对前端暴露的 DTO 必须：
/// 1. DxE_id 字段类型为 string，不允许 long/number。
/// 2. JSON 序列化使用 snake_case 或与前端约定的 camelCase，不得改变已有字段名。
/// </summary>

/// <summary>
/// 用户信息 DTO，用于登录响应和后台初始化接口。
/// 禁止包含 refreshToken、siteConfig、terminal 字段。
/// </summary>
public sealed class AdminInfoDto
{
    /// <summary>前端兼容业务 ID，必须为 string。</summary>
    [JsonPropertyName("id")]
    public string DxEId { get; init; } = string.Empty;

    [JsonPropertyName("userid")]
    public string Userid { get; init; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; init; } = string.Empty;

    /// <summary>当前 project 下是否为超级管理员。</summary>
    [JsonPropertyName("super")]
    public bool Super { get; init; }

    [JsonPropertyName("project")]
    public string Project { get; init; } = string.Empty;
}

/// <summary>
/// 登录成功响应 DTO。
/// 不包含 refreshToken（由门户统一管理 token 生命周期）。
/// </summary>
public sealed class LoginResultDto
{
    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    /// <summary>登录成功后前端跳转的路由路径。</summary>
    [JsonPropertyName("routePath")]
    public string RoutePath { get; init; } = string.Empty;

    [JsonPropertyName("adminInfo")]
    public AdminInfoDto AdminInfo { get; init; } = new();
}

/// <summary>
/// 后台初始化接口响应 DTO（首页加载）。
/// </summary>
public sealed class BackendIndexDto
{
    [JsonPropertyName("adminInfo")]
    public AdminInfoDto AdminInfo { get; init; } = new();

    [JsonPropertyName("menus")]
    public IReadOnlyList<MenuNodeDto> Menus { get; init; } = Array.Empty<MenuNodeDto>();

    [JsonPropertyName("routePath")]
    public string RoutePath { get; init; } = string.Empty;
}

/// <summary>
/// 菜单节点 DTO，与前端 menus -> authNode -> auth() / v-auth 机制兼容。
/// DxE_id 必须为 string，children 递归。
/// </summary>
public sealed class MenuNodeDto
{
    /// <summary>前端兼容业务 ID，JSON 字段名保持 "id"，类型必须为 string。</summary>
    [JsonPropertyName("id")]
    public string DxEId { get; init; } = string.Empty;

    /// <summary>父节点 DxE_id，根节点为 "0" 或空。</summary>
    [JsonPropertyName("pid")]
    public string Pid { get; init; } = "0";

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>前端路由 name，用于 auth() / v-auth 匹配。</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    /// <summary>节点类型：menu_dir / menu / button。</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>菜单类型：tab / link / iframe。button 节点此字段为空。</summary>
    [JsonPropertyName("menu_type")]
    public string MenuType { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("component")]
    public string Component { get; init; } = string.Empty;

    [JsonPropertyName("extend")]
    public string Extend { get; init; } = string.Empty;

    [JsonPropertyName("keepalive")]
    public bool Keepalive { get; init; }

    /// <summary>子节点列表，递归结构。</summary>
    [JsonPropertyName("children")]
    public IReadOnlyList<MenuNodeDto> Children { get; init; } = Array.Empty<MenuNodeDto>();

    /// <summary>权限码，用于服务端鉴权。</summary>
    [JsonPropertyName("permissionCode")]
    public string PermissionCode { get; init; } = string.Empty;

    /// <summary>规则码，用于菜单树构建和前端 authNode 匹配。</summary>
    [JsonPropertyName("ruleCode")]
    public string RuleCode { get; init; } = string.Empty;
}
