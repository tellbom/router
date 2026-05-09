using Microsoft.Extensions.Logging;
using Rbac.Application.Security;

namespace Rbac.Application.Auditing;

/// <summary>
/// project 校验阶段的审计事件服务。
/// 对以下四种情形产生审计事件：
/// 1. MissingProject  — 请求未携带 project 参数
/// 2. ForgedProject   — requestedProject 与 resolvedProject 不一致（预留扩展点）
/// 3. Unauthorized    — project 存在但用户无授权
/// 4. Authorized      — project 校验通过
///
/// 所有写入必须异步非阻塞，由 IAuditEventEmitter 实现（Channel / 内存队列）。
/// </summary>
public sealed class ProjectAuthorizationAuditService
{
    private readonly IAuditEventEmitter _emitter;
    private readonly ILogger<ProjectAuthorizationAuditService> _logger;

    public ProjectAuthorizationAuditService(
        IAuditEventEmitter emitter,
        ILogger<ProjectAuthorizationAuditService> logger)
    {
        _emitter = emitter;
        _logger = logger;
    }

    /// <summary>请求未携带 project 参数时记录审计。</summary>
    public Task EmitMissingProjectAsync(string userid, string traceId, string clientIp)
    {
        _logger.LogWarning(
            "AuditMissingProject userid={Userid} clientIp={ClientIp} traceId={TraceId}",
            userid, clientIp, traceId);

        return _emitter.EmitAsync(new AuthorizationAuditEvent
        {
            Userid = userid,
            Project = string.Empty,
            RequestedProject = string.Empty,
            TraceId = traceId,
            ClientIp = clientIp,
            Result = "deny",
            Reason = ProjectResolveResult.MissingProject.ToString(),
        });
    }

    /// <summary>
    /// requestedProject 与系统已知 project 不符（伪造 project）时记录审计。
    /// 此事件属于高风险事件，必须保证最终可追踪。
    /// </summary>
    public Task EmitForgedProjectAsync(
        string userid, string requestedProject, string traceId, string clientIp)
    {
        _logger.LogWarning(
            "AuditForgedProject userid={Userid} requestedProject={Project} clientIp={ClientIp} traceId={TraceId}",
            userid, requestedProject, clientIp, traceId);

        return _emitter.EmitAsync(new AuthorizationAuditEvent
        {
            Userid = userid,
            Project = string.Empty,
            RequestedProject = requestedProject,
            TraceId = traceId,
            ClientIp = clientIp,
            Result = "deny",
            Reason = "ForgedProject",
        });
    }

    /// <summary>project 存在但用户未被授权访问时记录审计。</summary>
    public Task EmitUnauthorizedProjectAsync(
        string userid, string requestedProject, string traceId, string clientIp)
    {
        _logger.LogWarning(
            "AuditUnauthorized userid={Userid} project={Project} clientIp={ClientIp} traceId={TraceId}",
            userid, requestedProject, clientIp, traceId);

        return _emitter.EmitAsync(new AuthorizationAuditEvent
        {
            Userid = userid,
            Project = requestedProject,
            RequestedProject = requestedProject,
            TraceId = traceId,
            ClientIp = clientIp,
            Result = "deny",
            Reason = ProjectResolveResult.Unauthorized.ToString(),
        });
    }

    /// <summary>project 校验通过时记录审计（allow）。</summary>
    public Task EmitAuthorizedAsync(
        string userid, string project, string traceId, string clientIp, bool isSuper)
    {
        _logger.LogDebug(
            "AuditAuthorized userid={Userid} project={Project} super={Super} traceId={TraceId}",
            userid, project, isSuper, traceId);

        return _emitter.EmitAsync(new AuthorizationAuditEvent
        {
            Userid = userid,
            Project = project,
            RequestedProject = project,
            TraceId = traceId,
            ClientIp = clientIp,
            Result = "allow",
            Reason = isSuper ? "SuperAuthorized" : "Authorized",
        });
    }
}
