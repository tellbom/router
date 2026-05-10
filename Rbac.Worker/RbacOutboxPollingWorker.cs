using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rbac.Infrastructure.MySql.Outbox;
using Rbac.Worker.Outbox;

namespace Rbac.Worker.Outbox;

/// <summary>
/// PATCH-09: Outbox 轮询消费 Worker。
///
/// 每轮从 MySQL 取最多 50 条 Status=Pending 且 NextRetryAt 已到期的事件，
/// 依次交给 Redis / ES / Casbin 三个处理器。全部成功后标记 Succeeded。
///
/// 重试策略（PATCH-09 修正，与 PATCH-08 MarkFailedAsync 协同）：
/// - 失败时重试次数 &lt; MaxRetry → 保持 Status=Pending，指数退避 NextRetryAt（5, 10, 20, 40, 80 秒）。
/// - 失败次数达到 MaxRetry → 传入 DateTimeOffset.MaxValue，MarkFailedAsync 改 Status=Failed（DLQ）。
///
/// 无消息时 Sleep 5 秒，有消息时立即继续，不固定间隔。
/// </summary>
public sealed class RbacOutboxPollingWorker : BackgroundService
{
    private const int MaxRetry  = 5;
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RbacOutboxPollingWorker> _logger;

    public RbacOutboxPollingWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<RbacOutboxPollingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RbacOutboxPollingWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessBatchAsync(stoppingToken);

                // 无事件时休眠，避免空转
                if (processed == 0)
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxPollingWorker outer loop exception. Retrying in 10s.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("RbacOutboxPollingWorker stopped.");
    }

    // ── 单轮批处理 ────────────────────────────────────────────────

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        // 每批使用独立 Scope，确保 Scoped 服务（DbContext、Repository 等）正确释放
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var reader        = sp.GetRequiredService<IOutboxReader>();
        var redisProc     = sp.GetRequiredService<RbacRedisOutboxProcessor>();
        var esProc        = sp.GetRequiredService<RbacElasticsearchOutboxProcessor>();
        var casbinProc    = sp.GetRequiredService<RbacCasbinOutboxProcessor>();

        var pending = await reader.FetchPendingAsync(BatchSize, ct);

        if (pending.Count == 0) return 0;

        _logger.LogDebug("OutboxPollingWorker batch size={N}", pending.Count);

        foreach (var evt in pending)
        {
            if (ct.IsCancellationRequested) break;
            await ProcessSingleAsync(evt, reader, redisProc, esProc, casbinProc, ct);
        }

        return pending.Count;
    }

    // ── 单条事件处理 ──────────────────────────────────────────────

    private async Task ProcessSingleAsync(
        OutboxEventEntity evt,
        IOutboxReader reader,
        RbacRedisOutboxProcessor redisProc,
        RbacElasticsearchOutboxProcessor esProc,
        RbacCasbinOutboxProcessor casbinProc,
        CancellationToken ct)
    {
        try
        {
            // 三个处理器串行执行（顺序：Redis → ES → Casbin）
            await redisProc.ProcessAsync(evt, ct);
            await esProc.ProcessAsync(evt, ct);
            await casbinProc.ProcessAsync(evt, ct);

            await reader.MarkSucceededAsync(evt.EventId, ct);

            _logger.LogDebug(
                "Outbox succeeded eventId={Id} type={T}", evt.EventId, evt.EventType);
        }
        catch (Exception ex)
        {
            var newRetryCount = evt.RetryCount + 1;

            _logger.LogError(ex,
                "Outbox processing failed eventId={Id} type={T} retry={N}/{Max}",
                evt.EventId, evt.EventType, newRetryCount, MaxRetry);

            DateTimeOffset nextRetryAt;
            if (newRetryCount >= MaxRetry)
            {
                // 超过最大重试 → DLQ（DateTimeOffset.MaxValue 信号）
                nextRetryAt = DateTimeOffset.MaxValue;
                _logger.LogError(
                    "Outbox DLQ eventId={Id} type={T} retries={N}",
                    evt.EventId, evt.EventType, newRetryCount);
            }
            else
            {
                // 指数退避：5, 10, 20, 40, 80 秒
                var delaySeconds = Math.Pow(2, newRetryCount - 1) * 5;
                nextRetryAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
            }

            // MarkFailedAsync：nextRetryAt=MaxValue → Status=Failed(DLQ)；否则 Status=Pending
            await reader.MarkFailedAsync(evt.EventId, newRetryCount, nextRetryAt, ct);
        }
    }
}
