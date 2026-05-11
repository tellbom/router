using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rbac.Api.Options;
using Rbac.Application.Authorization;
using Rbac.Application.Security;

namespace Rbac.Api.Filters;

/// <summary>
/// Server-side authorization filter with deny-by-default behavior.
/// Order:
/// 1. Allowlist: anonymous/basic routes.
/// 2. Project authorization: user must be authorized for current project.
/// 3. ProjectAccessAllowlist: project-authorized routes that need no permissionCode.
/// 4. API permission mapping: unmapped routes are denied by default.
/// 5. Permission check.
/// </summary>
public sealed class RbacAuthorizationFilter : IAsyncActionFilter
{
    private readonly ICurrentRbacContextAccessor _contextAccessor;
    private readonly IRbacApiPermissionMapper _permissionMapper;
    private readonly IRbacPermissionChecker _permissionChecker;
    private readonly RbacAllowlistOptions _allowlist;
    private readonly RbacProjectAccessAllowlistOptions _projectAccessAllowlist;
    private readonly ILogger<RbacAuthorizationFilter> _logger;

    public RbacAuthorizationFilter(
        ICurrentRbacContextAccessor contextAccessor,
        IRbacApiPermissionMapper permissionMapper,
        IRbacPermissionChecker permissionChecker,
        IOptions<RbacAllowlistOptions> allowlist,
        IOptions<RbacProjectAccessAllowlistOptions> projectAccessAllowlist,
        ILogger<RbacAuthorizationFilter> logger)
    {
        _contextAccessor = contextAccessor;
        _permissionMapper = permissionMapper;
        _permissionChecker = permissionChecker;
        _allowlist = allowlist.Value;
        _projectAccessAllowlist = projectAccessAllowlist.Value;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var path = httpContext.Request.Path.Value ?? string.Empty;

        if (_allowlist.IsAllowed(path, httpContext.Request.Method))
        {
            _logger.LogDebug("Allowlist hit path={Path}", path);
            await next();
            return;
        }

        var rbacCtx = _contextAccessor.Context;
        if (rbacCtx is null || !rbacCtx.IsProjectAuthorized)
        {
            _logger.LogWarning("Unauthorized project path={Path} userid={U}",
                path, rbacCtx?.Userid ?? "anonymous");
            context.Result = ForbidResult("ProjectNotAuthorized");
            return;
        }

        if (_projectAccessAllowlist.IsAllowed(path))
        {
            _logger.LogDebug("ProjectAccessAllowlist hit path={Path}", path);
            await next();
            return;
        }

        var mapping = await _permissionMapper.ResolveAsync(rbacCtx.Project, httpContext);
        if (mapping is null)
        {
            _logger.LogWarning("No permission mapping path={Path} project={P}", path, rbacCtx.Project);
            context.Result = ForbidResult("NoPermissionMapping");
            return;
        }

        var checkRequest = new PermissionCheckRequest
        {
            Context = rbacCtx,
            PermissionCode = mapping.PermissionCode,
            Action = mapping.Action,
        };

        var result = await _permissionChecker.CheckAsync(checkRequest, httpContext.RequestAborted);

        if (!result.IsAllowed)
        {
            _logger.LogWarning(
                "Permission denied userid={U} project={P} permCode={PC} action={A} reason={R}",
                rbacCtx.Userid, rbacCtx.Project, mapping.PermissionCode, mapping.Action, result.Reason);
            context.Result = ForbidResult(result.Reason ?? "Denied");
            return;
        }

        await next();
    }

    private static ObjectResult ForbidResult(string reason) =>
        new(new { code = 40300, msg = "Forbidden", reason })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
}
