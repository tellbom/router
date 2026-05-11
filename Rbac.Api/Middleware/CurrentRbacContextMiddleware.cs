using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Rbac.Application.Security;

namespace Rbac.Api.Middleware;

/// <summary>
/// 请求作用域中间件。在管道早期完成：
/// 1. 从 ClaimsPrincipal 提取 userid。
/// 2. 从请求读取 requestedProject。
/// 3. 调用 IRbacProjectResolver 校验并生成 CurrentRbacContext。
/// 4. 写入 ICurrentRbacContextAccessor，供后续中间件和 Service 读取。
///
/// 必须注册在 UseAuthentication() 之后、路由处理之前。
/// allowlist 路由跳过 project 校验但仍尝试提取 userid（匿名路由 userid 可为空）。
/// </summary>
public sealed class CurrentRbacContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CurrentRbacContextMiddleware> _logger;

    public CurrentRbacContextMiddleware(RequestDelegate next, ILogger<CurrentRbacContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IUserIdentityResolver userResolver,
        IProjectRequestReader projectReader,
        IRbacProjectResolver projectResolver,
        ICurrentRbacContextAccessor contextAccessor)
    {
        var traceId = context.TraceIdentifier;

        // 1. 提取 userid（匿名路由此处可能为 null，由 allowlist filter 决定是否拒绝）
        var userid = userResolver.ResolveUserId(context.User)
            ?? ResolveDevelopmentFakeUserId(context)
            ?? string.Empty;

        // 2. 读取原始 project
        var requestedProject = projectReader.ReadProject(context) ?? string.Empty;

        // 3. 校验并构建 Context
        // 空 userid 跳过校验（让 JWT 中间件的 401 先行），仍构建未授权 Context
        CurrentRbacContext rbacContext;
        if (string.IsNullOrEmpty(userid))
        {
            rbacContext = new CurrentRbacContext
            {
                Userid = string.Empty,
                Project = requestedProject,
                RequestedProject = requestedProject,
                TraceId = traceId,
                IsProjectAuthorized = false,
                IsProjectSuper = false,
            };
        }
        else
        {
            rbacContext = await projectResolver.ResolveAsync(userid, requestedProject, traceId, context.RequestAborted);
        }

        // 4. 写入 accessor，供后续所有组件读取
        contextAccessor.Set(rbacContext);

        _logger.LogDebug(
            "RbacContext set userid={Userid} project={Project} authorized={Authorized} traceId={TraceId}",
            rbacContext.Userid, rbacContext.Project, rbacContext.IsProjectAuthorized, traceId);

        await _next(context);
    }

    private static string? ResolveDevelopmentFakeUserId(HttpContext context)
    {
        var headerUserid = context.Request.Headers["X-Test-Userid"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(headerUserid))
            return headerUserid;

        var value = context.Request.Headers.Authorization.FirstOrDefault();
        const string prefix = "Bearer fake:";
        if (value is null || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var userid = value[prefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(userid) ? null : userid;
    }
}

/// <summary>
/// <see cref="ICurrentRbacContextAccessor"/> 的请求作用域实现。
/// 注册为 Scoped，每个请求独立实例。
/// </summary>
public sealed class HttpContextRbacContextAccessor : ICurrentRbacContextAccessor
{
    private CurrentRbacContext? _context;

    public CurrentRbacContext? Context => _context;

    public void Set(CurrentRbacContext context)
    {
        _context = context;
    }
}
