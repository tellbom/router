using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rbac.Application.Authorization;
using Rbac.Application.Security;
using Rbac.Api.Options;

namespace Rbac.Api.Filters;

/// <summary>
/// 服务端鉴权过滤器。实现 deny-by-default 策略。
///
/// 执行顺序：
/// 1. 路由命中 allowlist → 放行（记录基础访问日志）。
/// 2. CurrentRbacContext 未授权（IsProjectAuthorized = false）→ 403。
/// 3. 路由无法映射到 permissionCode → 403（deny-by-default）。
/// 4. 调用 IRbacPermissionChecker 判断 → allow 或 403。
///
/// allowlist 必须集中配置（RbacAllowlistOptions），不允许散落在 Controller。
/// </summary>
public sealed class RbacAuthorizationFilter : IAsyncActionFilter
{
    private readonly ICurrentRbacContextAccessor _contextAccessor;
    private readonly IRbacApiPermissionMapper _permissionMapper;
    private readonly IRbacPermissionChecker _permissionChecker;
    private readonly RbacAllowlistOptions _allowlist;
    private readonly ILogger<RbacAuthorizationFilter> _logger;

    public RbacAuthorizationFilter(
        ICurrentRbacContextAccessor contextAccessor,
        IRbacApiPermissionMapper permissionMapper,
        IRbacPermissionChecker permissionChecker,
        IOptions<RbacAllowlistOptions> allowlist,
        ILogger<RbacAuthorizationFilter> logger)
    {
        _contextAccessor = contextAccessor;
        _permissionMapper = permissionMapper;
        _permissionChecker = permissionChecker;
        _allowlist = allowlist.Value;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var path = httpContext.Request.Path.Value ?? string.Empty;

        // 1. allowlist 检查
        if (_allowlist.IsAllowed(path, httpContext.Request.Method))
        {
            _logger.LogDebug("Allowlist hit path={Path}", path);
            await next();
            return;
        }

        // 2. project 未授权
        var rbacCtx = _contextAccessor.Context;
        if (rbacCtx is null || !rbacCtx.IsProjectAuthorized)
        {
            _logger.LogWarning("Unauthorized project path={Path} userid={U}",
                path, rbacCtx?.Userid ?? "anonymous");
            context.Result = ForbidResult("ProjectNotAuthorized");
            return;
        }

        // 3. 解析 API 权限映射（deny-by-default：未映射 = 拒绝）
        var mapping = await _permissionMapper.ResolveAsync(rbacCtx.Project, httpContext);
        if (mapping is null)
        {
            _logger.LogWarning("No permission mapping path={Path} project={P}", path, rbacCtx.Project);
            context.Result = ForbidResult("NoPermissionMapping");
            return;
        }

        // 4. 执行鉴权判断
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

    // ── 私有辅助 ──────────────────────────────────────────────────

    private static ObjectResult ForbidResult(string reason) =>
        new(new { code = 40300, msg = "Forbidden", reason })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
}
