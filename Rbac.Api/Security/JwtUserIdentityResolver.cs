using System.Security.Claims;
using Microsoft.Extensions.Options;
using Rbac.Api.Options;
using Rbac.Application.Security;

namespace Rbac.Api.Security;

/// <summary>
/// PATCH-02: IUserIdentityResolver 的 JWT 实现。
///
/// 按 RbacJwtOptions.UseridClaim 优先，依次尝试 FallbackUseridClaims，
/// 返回第一个非空 claim 值作为 userid。
/// 全部为空时返回 null，由 CurrentRbacContextMiddleware 构建未授权 Context。
/// </summary>
public sealed class JwtUserIdentityResolver : IUserIdentityResolver
{
    private readonly RbacJwtOptions _options;

    public JwtUserIdentityResolver(IOptions<RbacJwtOptions> options)
        => _options = options.Value;

    public string? ResolveUserId(ClaimsPrincipal principal)
    {
        // 优先主 claim
        var val = principal.FindFirstValue(_options.UseridClaim);
        if (!string.IsNullOrWhiteSpace(val)) return val;

        // 依次尝试 fallback claims
        foreach (var claim in _options.FallbackUseridClaims)
        {
            val = principal.FindFirstValue(claim);
            if (!string.IsNullOrWhiteSpace(val)) return val;
        }

        return null;
    }
}
