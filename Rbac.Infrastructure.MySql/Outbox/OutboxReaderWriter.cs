using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rbac.Application.Outbox;
using Rbac.Infrastructure.MySql.Mapping;

namespace Rbac.Infrastructure.MySql.Outbox;

/// <summary>
/// PATCH-08: IOutboxWriter + IOutboxReader 的 EF Core 6 兼容实现。
///
/// 写入（IOutboxWriter）：
///   Append/AppendRange 仅调用 DbContext.Add，不单独 SaveChanges。
///   调用方在写业务聚合根后统一 SaveChanges，保证同一事务。
///
/// 读取（IOutboxReader）：
///   FetchPendingAsync：取 Status=Pending 且 NextRetryAt 已到期的事件，按 CreatedAt 升序。
///   MarkSucceededAsync / MarkFailedAsync 使用 EF Core 6 兼容写法：
///     先 FirstOrDefaultAsync 查实体，修改字段，再 SaveChangesAsync。
///   （ExecuteUpdateAsync 是 EF Core 7 引入的 API，项目 net6.0 不可用。）
///
/// MarkFailedAsync 重试语义：
///   - nextRetryAt != DateTimeOffset.MaxValue → 保持 Status=Pending，写 NextRetryAt，等待重试。
///   - nextRetryAt == DateTimeOffset.MaxValue → Status=Failed（DLQ），不再重试。
/// </summary>
public sealed class OutboxReaderWriter : IOutboxWriter, IOutboxReader
{
    private readonly RbacDbContext _db;
    private readonly ILogger<OutboxReaderWriter> _logger;

    public OutboxReaderWriter(RbacDbContext db, ILogger<OutboxReaderWriter> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── IOutboxWriter ─────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>不调用 SaveChanges，调用方在同一事务内统一提交。</remarks>
    public void Append(RbacOutboxEvent evt)
    {
        _db.OutboxEvents.Add(MapToEntity(evt));
    }

    /// <inheritdoc/>
    public void AppendRange(IEnumerable<RbacOutboxEvent> events)
    {
        _db.OutboxEvents.AddRange(events.Select(MapToEntity));
    }

    // ── IOutboxReader ─────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OutboxEventEntity>> FetchPendingAsync(
        int batchSize = 50, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        return await _db.OutboxEvents
            .Where(x => x.Status == OutboxStatus.Pending
                        && (x.NextRetryAt == null || x.NextRetryAt <= now))
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task MarkSucceededAsync(string eventId, CancellationToken ct = default)
    {
        var entity = await _db.OutboxEvents
            .FirstOrDefaultAsync(x => x.EventId == eventId, ct);

        if (entity is null)
        {
            _logger.LogWarning("MarkSucceeded: eventId={Id} not found", eventId);
            return;
        }

        entity.Status = OutboxStatus.Succeeded;
        entity.ProcessedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task MarkFailedAsync(
        string eventId, int retryCount, DateTimeOffset nextRetryAt,
        CancellationToken ct = default)
    {
        var entity = await _db.OutboxEvents
            .FirstOrDefaultAsync(x => x.EventId == eventId, ct);

        if (entity is null)
        {
            _logger.LogWarning("MarkFailed: eventId={Id} not found", eventId);
            return;
        }

        // DateTimeOffset.MaxValue 作为约定信号：超过最大重试，标记为 DLQ Failed
        var isDlq = nextRetryAt == DateTimeOffset.MaxValue;
        entity.Status = isDlq ? OutboxStatus.Failed : OutboxStatus.Pending;
        entity.RetryCount = retryCount;
        entity.NextRetryAt = isDlq ? null : nextRetryAt;

        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "MarkFailed eventId={Id} isDlq={DLQ} status={S} retryCount={N}",
            eventId, isDlq, entity.Status, retryCount);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private static OutboxEventEntity MapToEntity(RbacOutboxEvent evt) => new()
    {
        EventId = evt.EventId,
        EventType = evt.EventType,
        Project = evt.Project,
        Userid = evt.Userid,
        GroupCode = evt.GroupCode,
        Payload = evt.Payload,
        Status = OutboxStatus.Pending,
        RetryCount = 0,
        NextRetryAt = null,
        CreatedAt = evt.CreatedAt,
        ProcessedAt = null,
    };
}
