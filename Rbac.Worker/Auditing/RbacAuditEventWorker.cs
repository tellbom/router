using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using Rbac.Application.Auditing;
using Rbac.Application.Security;

namespace Rbac.Worker.Auditing;

/// <summary>
/// 审计事件异步 Worker。
///
/// 使用 System.Threading.Channels 的无界 Channel 作为内存队列：
/// - 发射方（RbacAuthorizationFilter / RbacPermissionChecker）写入 Channel（非阻塞）。
/// - 本 Worker 作为 IHostedService 后台消费 Channel，写入 MySQL 审计表和 ES 审计索引。
///
/// 约束：
/// - 写入失败不影响主请求鉴权结果。
/// - deny / error / ForgedProject 等高风险事件必须保证最终可追踪（重试或告警）。
/// - Channel 满时（BoundedChannel 场景）丢弃最旧事件，不阻塞发射方。
/// </summary>
public sealed class RbacAuditEventWorker : BackgroundService
{
    private static readonly Channel<RbacAuditEvent> _channel =
        Channel.CreateBounded<RbacAuditEvent>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    /// <summary>全局 Channel Writer，供发射方使用。</summary>
    public static ChannelWriter<RbacAuditEvent> Writer => _channel.Writer;

    private readonly IAuditEventEmitter _emitter;
    private readonly ILogger<RbacAuditEventWorker> _logger;

    public RbacAuditEventWorker(IAuditEventEmitter emitter, ILogger<RbacAuditEventWorker> logger)
    {
        _emitter = emitter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RbacAuditEventWorker started.");

        await foreach (var evt in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _emitter.EmitAsync(evt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Audit event emit failed auditId={Id} type={T}",
                    evt.AuditId, evt.GetType().Name);
                // 不重新入队，继续消费下一个事件
            }
        }

        _logger.LogInformation("RbacAuditEventWorker stopped.");
    }
}

/// <summary>
/// Channel 实现的 IAuditEventEmitter，发射方注入此实现。
/// 将事件写入 Channel（非阻塞），由 RbacAuditEventWorker 消费。
/// </summary>
public sealed class ChannelAuditEventEmitter : IAuditEventEmitter
{
    private readonly ILogger<ChannelAuditEventEmitter> _logger;

    public ChannelAuditEventEmitter(ILogger<ChannelAuditEventEmitter> logger)
    {
        _logger = logger;
    }

    public ValueTask EmitAsync(RbacAuditEvent auditEvent)
    {
        if (!RbacAuditEventWorker.Writer.TryWrite(auditEvent))
        {
            _logger.LogWarning(
                "Audit channel full, event dropped auditId={Id}", auditEvent.AuditId);
        }
        return ValueTask.CompletedTask;
    }

    Task IAuditEventEmitter.EmitAsync(RbacAuditEvent auditEvent)
    {
        EmitAsync(auditEvent);
        return Task.CompletedTask;
    }
}
