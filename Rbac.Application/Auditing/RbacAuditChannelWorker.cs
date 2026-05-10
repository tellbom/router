using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Rbac.Application.Auditing;

/// <summary>
/// 审计事件异步 Worker。
///
/// 从 Application 层提升：ChannelAuditEventEmitter 和 RbacAuditEventWorker 均只依赖
/// BCL（System.Threading.Channels）和 Application.Auditing 层接口，
/// 无 Worker 层特有依赖，因此放在 Application.Auditing 供 Api 和 Worker 共享引用。
///
/// 使用 System.Threading.Channels 的有界 Channel 作为内存队列：
/// - 发射方（RbacAuthorizationFilter / RbacPermissionChecker）通过 ChannelAuditEventEmitter 写入。
/// - 本 Worker 作为 IHostedService 后台消费 Channel，写入审计存储。
///
/// 约束：
/// - 写入失败不影响主请求鉴权结果。
/// - Channel 满时（BoundedChannel）丢弃最旧事件，不阻塞发射方。
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

    /// <summary>全局 Channel Writer，供 ChannelAuditEventEmitter 使用。</summary>
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
                    "Audit event emit failed auditId={Id}", evt.AuditId);
            }
        }

        _logger.LogInformation("RbacAuditEventWorker stopped.");
    }
}

/// <summary>
/// Channel 实现的 IAuditEventEmitter。
/// 将审计事件非阻塞写入 Channel，由 RbacAuditEventWorker 异步消费。
/// 注册为 Singleton，Api 和 Worker 均可使用。
/// </summary>
public sealed class ChannelAuditEventEmitter : IAuditEventEmitter
{
    private readonly ILogger<ChannelAuditEventEmitter> _logger;

    public ChannelAuditEventEmitter(ILogger<ChannelAuditEventEmitter> logger)
        => _logger = logger;

    public Task EmitAsync(RbacAuditEvent auditEvent)
    {
        if (!RbacAuditEventWorker.Writer.TryWrite(auditEvent))
        {
            _logger.LogWarning(
                "Audit channel full, event dropped auditId={Id}", auditEvent.AuditId);
        }
        return Task.CompletedTask;
    }
}
