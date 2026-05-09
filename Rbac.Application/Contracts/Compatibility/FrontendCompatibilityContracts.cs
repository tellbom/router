using System.Text.Json.Serialization;
using Rbac.Application.Contracts.Menus;

namespace Rbac.Application.Contracts.Compatibility;

/// <summary>
/// 管理员基本信息 DTO。用于登录响应和后台初始化接口。
/// 禁止包含 refreshToken、siteConfig、terminal 字段。
/// </summary>
public sealed class AdminInfoDto
{
    /// <summary>前端兼容业务 ID，JSON 字段名为 "id"，必须为 string。</summary>
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
/// 不包含 refreshToken（由公司门户统一管理 token 生命周期）。
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

// MenuNodeDto 定义在 Rbac.Application.Contracts.Menus.RbacMenuDtos（T047）
// 通过 using Rbac.Application.Contracts.Menus 引入，保持向后兼容。
