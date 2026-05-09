using Microsoft.AspNetCore.Http;
using Rbac.Application.Auditing;
using Rbac.Application.Security;

namespace Rbac.Api.Filters;

/// <summary>
/// 鉴权过滤器审计事件发射器。
///
/// 由 <see cref="RbacAuthorizationFilter"/> 调用，在 allow / deny / error 时
/// 发射 <see cref="AuthorizationAuditEvent"/>。
///
/// 约束：
/// - 发射操作必须异步非阻塞（不 await，fire-and-forget 或 Channel）。
/// - 审计写入失败不影响主请求鉴权结果。
/// - deny、error、ForgedProject 等高风险事件必须保证最终可追踪。
/// </summary>
public sealed class RbacAuthorizationAuditEmitter
{
    private readonly IAuditEventEmitter _emitter;

    public RbacAuthorizationAuditEmitter(IAuditEventEmitter emitter)
    {
        _emitter = emitter;
    }

    /// <summary>emit allowlist 放行事件（基础访问日志级别）。</summary>
    public void EmitAllowlistHit(HttpContext ctx, string path)
    {
        var evt = BuildBase(ctx, path, result: "allow", reason: "Allowlist");
        FireAndForget(evt);
    }

    /// <summary>emit project 未授权拒绝事件（高风险）。</summary>
    public void EmitProjectUnauthorized(HttpContext ctx, string path, string reason)
    {
        var evt = BuildBase(ctx, path, result: "deny", reason: reason);
        FireAndForget(evt);
    }

    /// <summary>emit 路由无权限映射拒绝事件。</summary>
    public void EmitNoPermissionMapping(HttpContext ctx, string path)
    {
        var evt = BuildBase(ctx, path, result: "deny", reason: "NoPermissionMapping");
        FireAndForget(evt);
    }

    /// <summary>emit 鉴权结果事件（allow / deny / error）。</summary>
    public void EmitCheckResult(
        HttpContext ctx, string path,
        string permissionCode, string action,
        string result, string? reason)
    {
        var evt = BuildBase(ctx, path, result, reason ?? result, permissionCode, action);
        FireAndForget(evt);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private static AuthorizationAuditEvent BuildBase(
        HttpContext ctx,
        string path,
        string result,
        string reason,
        string permissionCode = "",
        string action = "")
    {
        var rbacCtx = ctx.RequestServices
            .GetService(typeof(ICurrentRbacContextAccessor)) as ICurrentRbacContextAccessor;

        return new AuthorizationAuditEvent
        {
            Userid = rbacCtx?.Context?.Userid ?? string.Empty,
            Project = rbacCtx?.Context?.Project ?? string.Empty,
            RequestedProject = rbacCtx?.Context?.RequestedProject ?? string.Empty,
            TraceId = ctx.TraceIdentifier,
            PermissionCode = permissionCode,
            Action = action,
            Result = result,
            Reason = reason,
            ApiPath = path,
            HttpMethod = ctx.Request.Method,
            ClientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
        };
    }

    private void FireAndForget(AuthorizationAuditEvent evt)
    {
        // fire-and-forget：不阻塞主请求
        _ = _emitter.EmitAsync(evt);
    }
}
