namespace Rbac.Application.Auditing;

/// <summary>
/// 审计事件发射契约。实现必须异步非阻塞（写入内存队列或 Channel）。
/// 定义在 Rbac.Application.Auditing，是所有审计相关文件的统一依赖点。
/// 由 Rbac.Worker.Auditing.ChannelAuditEventEmitter 实现。
/// </summary>
public interface IAuditEventEmitter
{
    Task EmitAsync(RbacAuditEvent auditEvent);
}
