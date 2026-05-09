using Rbac.Application.Contracts.Compatibility;

namespace Rbac.Application.Authentication;

/// <summary>
/// 登录结果构建工厂。
///
/// 负责构建登录成功和鉴权失败两种场景下的兼容响应。
///
/// routePath 行为约定（前端兼容）：
/// - 登录成功：routePath = 后台首页或第一个有权限的菜单路径，前端跳转至此。
/// - 鉴权失败（401/403）：routePath = "/login"，前端重定向至登录页。
///
/// 约束：
/// - 不包含 refreshToken（公司门户统一管理 token 生命周期）。
/// - 不包含 siteConfig / terminal 字段。
/// </summary>
public sealed class RbacLoginResultFactory
{
    /// <summary>登录失败时前端跳转路径。</summary>
    public const string LoginRoutePath = "/login";

    /// <summary>后台默认首页路径（无任何菜单权限时的兜底）。</summary>
    public const string DefaultDashboardPath = "/dashboard";

    /// <summary>
    /// 构建登录成功响应。
    /// token 由公司门户或 JWT 服务颁发后传入，本服务不生成 token。
    /// </summary>
    public LoginResponseDto BuildSuccess(
        string token,
        AdminInfoDto adminInfo,
        string? firstMenuPath = null)
    {
        var routePath = !string.IsNullOrWhiteSpace(firstMenuPath)
            ? firstMenuPath
            : DefaultDashboardPath;

        return new LoginResponseDto
        {
            Token = token,
            RoutePath = routePath,
            AdminInfo = adminInfo,
        };
    }

    /// <summary>
    /// 构建登录失败响应（用户名/密码错误、账号禁用等）。
    /// routePath 固定为 "/login"，前端停留在登录页。
    /// </summary>
    public LoginResponseDto BuildFailure(string reason = "")
    {
        return new LoginResponseDto
        {
            Token = string.Empty,
            RoutePath = LoginRoutePath,
            AdminInfo = new AdminInfoDto(),
        };
    }

    /// <summary>
    /// 构建 project 未授权时的重定向响应（403 场景）。
    /// 前端收到后跳转至登录页或权限不足提示页。
    /// </summary>
    public static string GetUnauthorizedRoutePath() => LoginRoutePath;

    /// <summary>
    /// 构建 JWT 过期时的重定向路径（401 场景）。
    /// </summary>
    public static string GetExpiredTokenRoutePath() => LoginRoutePath;
}
