using System.Security.Claims;

namespace Rbac.Application.Security;

/// <summary>
/// 从已验证的 JWT ClaimsPrincipal 中提取 userid 的契约。
/// 实现由 Rbac.Api 层注册，Application 层只依赖此接口。
/// </summary>
public interface IUserIdentityResolver
{
    /// <summary>
    /// 从 ClaimsPrincipal 提取用户 ID。
    /// 按照配置的 <c>UseridClaim</c> 优先，依次尝试 <c>FallbackUseridClaims</c>。
    /// </summary>
    /// <param name="principal">JWT 中间件验证后的 ClaimsPrincipal。</param>
    /// <returns>
    /// 成功时返回非空 userid 字符串；
    /// 所有 claim 均无效时返回 null（调用方应返回 401）。
    /// </returns>
    string? ResolveUserId(ClaimsPrincipal principal);
}
