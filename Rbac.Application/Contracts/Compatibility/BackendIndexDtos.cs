using System.Text.Json.Serialization;
using Rbac.Application.Contracts.Menus;

namespace Rbac.Application.Contracts.Compatibility;

/// <summary>
/// 后台初始化接口响应 DTO（首页加载 /api/admin/index）。
///
/// 约束：
/// - 不包含 refreshToken、siteConfig、terminal。
/// - menus 使用 MenuNodeDto，DxEId 为 string。
/// - routePath 为前端初始跳转路径。
/// </summary>
public sealed class BackendIndexDto
{
    [JsonPropertyName("adminInfo")]
    public AdminInfoDto AdminInfo { get; init; } = new();

    /// <summary>
    /// 当前用户在当前 project 下可见的菜单树。
    /// 已按 permissionCode 裁剪，前端直接用于构建 authNode。
    /// </summary>
    [JsonPropertyName("menus")]
    public IReadOnlyList<MenuNodeDto> Menus { get; init; } = Array.Empty<MenuNodeDto>();

    /// <summary>
    /// 后台初始化完成后前端应跳转的路由路径。
    /// 通常为第一个有权限的菜单路径，或默认首页。
    /// </summary>
    [JsonPropertyName("routePath")]
    public string RoutePath { get; init; } = string.Empty;
}

/// <summary>
/// 登录接口请求 DTO。
/// </summary>
public sealed class LoginRequestDto
{
    [JsonPropertyName("userid")]
    public string Userid { get; init; } = string.Empty;

    /// <summary>前端携带的 project 标识（服务端必须校验，不直接信任）。</summary>
    [JsonPropertyName("project")]
    public string Project { get; init; } = string.Empty;
}

/// <summary>
/// 统一登录响应 DTO（与 LoginResultDto 相同结构，补充 project 字段）。
///
/// 不包含 refreshToken（公司门户统一管理 token 生命周期）。
/// 不包含 siteConfig / terminal（前端从其他接口获取）。
/// </summary>
public sealed class LoginResponseDto
{
    /// <summary>JWT Token（由公司门户颁发后转发，或直接颁发）。</summary>
    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    /// <summary>登录成功后前端跳转路径。</summary>
    [JsonPropertyName("routePath")]
    public string RoutePath { get; init; } = string.Empty;

    [JsonPropertyName("adminInfo")]
    public AdminInfoDto AdminInfo { get; init; } = new();
}
