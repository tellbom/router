using Rbac.Application.Observability;
using Rbac.Application.Auditing;
using Rbac.Application.Authorization;
using Rbac.Application.Security;

namespace Rbac.Application.Auditing;

/// <summary>
/// 鉴权结果审计日志写入契约。
///
/// 统一封装 allow / deny / error 三类鉴权结果的审计写入，
/// 确保所有关键信息（userid, project, permissionCode, source, clientIp, traceId）均被记录。
///
/// 实现必须：
/// - 异步非阻塞（写入 Channel 或内存队列）。
/// - 写入失败不影响主请求鉴权结果。
/// - deny / error / ForgedProject 等高风险事件保证最终可追踪。
/// </summary>
public interface IRbacAuthorizationAuditWriter
{
    /// <summary>记录鉴权 allow 事件。</summary>
    Task WriteAllowAsync(
        CurrentRbacContext ctx,
        string permissionCode,
        string action,
        PermissionCheckSource source,
        CancellationToken ct = default);

    /// <summary>记录鉴权 deny 事件。</summary>
    Task WriteDenyAsync(
        CurrentRbacContext ctx,
        string permissionCode,
        string action,
        PermissionCheckSource source,
        string reason,
        CancellationToken ct = default);

    /// <summary>记录鉴权 error 事件（服务不可用等）。</summary>
    Task WriteErrorAsync(
        CurrentRbacContext ctx,
        string permissionCode,
        string action,
        string errorMessage,
        CancellationToken ct = default);
}

/// <summary>
/// 默认实现：通过 IAuditEventEmitter 异步写入审计事件。
/// </summary>
public sealed class RbacAuthorizationAuditWriter : IRbacAuthorizationAuditWriter
{
    private readonly IAuditEventEmitter _emitter;
    private readonly RbacMetrics _metrics;

    public RbacAuthorizationAuditWriter(IAuditEventEmitter emitter, RbacMetrics metrics)
    {
        _emitter = emitter;
        _metrics = metrics;
    }

    public Task WriteAllowAsync(
        CurrentRbacContext ctx, string permissionCode, string action,
        PermissionCheckSource source, CancellationToken ct = default)
    {
        _metrics.AuthorizationAllows.Add(1,
            new("project", ctx.Project), new("source", source.ToString()));

        return _emitter.EmitAsync(BuildEvent(ctx, permissionCode, action, "allow", source.ToString()));
    }

    public Task WriteDenyAsync(
        CurrentRbacContext ctx, string permissionCode, string action,
        PermissionCheckSource source, string reason, CancellationToken ct = default)
    {
        _metrics.AuthorizationDenies.Add(1,
            new("project", ctx.Project), new("reason", reason));

        return _emitter.EmitAsync(BuildEvent(ctx, permissionCode, action, "deny", reason));
    }

    public Task WriteErrorAsync(
        CurrentRbacContext ctx, string permissionCode, string action,
        string errorMessage, CancellationToken ct = default)
    {
        _metrics.AuthorizationErrors.Add(1, new KeyValuePair<string, object?>("project", ctx.Project));

        return _emitter.EmitAsync(BuildEvent(ctx, permissionCode, action, "error", errorMessage));
    }

    private static AuthorizationAuditEvent BuildEvent(
        CurrentRbacContext ctx, string permissionCode,
        string action, string result, string reason) =>
        new()
        {
            Userid = ctx.Userid,
            Project = ctx.Project,
            RequestedProject = ctx.RequestedProject,
            TraceId = ctx.TraceId,
            PermissionCode = permissionCode,
            Action = action,
            Result = result,
            Reason = reason,
        };
}
