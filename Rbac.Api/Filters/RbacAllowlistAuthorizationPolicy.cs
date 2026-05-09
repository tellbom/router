using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Rbac.Api.Options;

namespace Rbac.Api.Filters;

/// <summary>
/// 集中 allowlist 授权策略。
///
/// 配合 <see cref="RbacAuthorizationFilter"/> 实现 deny-by-default：
/// - allowlist 命中 → 放行（不要求 JWT / project）。
/// - allowlist 未命中 → 交由 RbacAuthorizationFilter 继续鉴权。
///
/// 所有匿名路由必须在 <see cref="RbacAllowlistOptions"/> 集中注册，
/// 禁止在 Controller 或 Service 中散落配置。
/// </summary>
public sealed class RbacAllowlistAuthorizationPolicy : IAuthorizationRequirement { }

/// <summary>
/// RbacAllowlistAuthorizationPolicy 的处理器。
/// 注册为 ASP.NET Core Authorization 策略使用。
/// </summary>
public sealed class RbacAllowlistAuthorizationHandler
    : AuthorizationHandler<RbacAllowlistAuthorizationPolicy>
{
    private readonly RbacAllowlistOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RbacAllowlistAuthorizationHandler(
        IOptions<RbacAllowlistOptions> options,
        IHttpContextAccessor httpContextAccessor)
    {
        _options = options.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RbacAllowlistAuthorizationPolicy requirement)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            context.Fail();
            return Task.CompletedTask;
        }

        var path = httpContext.Request.Path.Value ?? string.Empty;
        var method = httpContext.Request.Method;

        if (_options.IsAllowed(path, method))
            context.Succeed(requirement);
        // 未命中 allowlist：不 Fail，交由后续 RbacAuthorizationFilter 处理

        return Task.CompletedTask;
    }
}
