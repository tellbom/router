using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rbac.Application.Outbox;

namespace Rbac.Infrastructure.MySql.Outbox;

/// <summary>
/// Outbox 事件持久化实体。
/// 与 <see cref="RbacOutboxEvent"/> 对应，存储在 MySQL rbac_outbox 表。
///
/// 写 MySQL 与写 Outbox 必须同一事务（在 RbacDbContext 内完成）。
/// 禁止生成 EF Core 迁移，schema 由 DBA 通过独立 SQL 脚本管理。
/// </summary>
public sealed class OutboxEventEntity
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string? Userid { get; set; }
    public string? GroupCode { get; set; }

    /// <summary>序列化的事件 payload（JSON）。字段结构见 RbacOutboxEvents.cs。</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>状态：Pending / Processing / Succeeded / Failed。</summary>
    public string Status { get; set; } = OutboxStatus.Pending;

    public int RetryCount { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
}

/// <summary>
/// Outbox EF Core Mapping。注册到 RbacDbContext.OnModelCreating。
/// </summary>
public sealed class OutboxEventMapping : IEntityTypeConfiguration<OutboxEventEntity>
{
    public void Configure(EntityTypeBuilder<OutboxEventEntity> b)
    {
        b.ToTable("rbac_outbox");
        b.HasKey(x => x.EventId);

        b.Property(x => x.EventId).HasColumnName("event_id").HasMaxLength(64).IsRequired();
        b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(64).IsRequired();
        b.Property(x => x.Project).HasColumnName("project").HasMaxLength(64).IsRequired();
        b.Property(x => x.Userid).HasColumnName("userid").HasMaxLength(128);
        b.Property(x => x.GroupCode).HasColumnName("group_code").HasMaxLength(128);
        b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("longtext").IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        b.Property(x => x.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
        b.Property(x => x.NextRetryAt).HasColumnName("next_retry_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.ProcessedAt).HasColumnName("processed_at");

        // 轮询查询优化索引
        b.HasIndex(x => x.Status).HasDatabaseName("ix_outbox_status");
        b.HasIndex(x => new { x.Status, x.NextRetryAt }).HasDatabaseName("ix_outbox_status_retry");
        b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_outbox_created_at");
    }
}

/// <summary>
/// Outbox 写入服务契约。
/// 写 MySQL 聚合根与写 Outbox 必须在同一 DbContext 事务内执行。
/// </summary>
public interface IOutboxWriter
{
    /// <summary>在当前 DbContext 事务内追加 Outbox 事件（不单独 SaveChanges）。</summary>
    void Append(RbacOutboxEvent evt);

    /// <summary>批量追加多个 Outbox 事件。</summary>
    void AppendRange(IEnumerable<RbacOutboxEvent> events);
}

/// <summary>
/// Outbox 查询服务契约（供 Worker 消费使用）。
/// </summary>
public interface IOutboxReader
{
    /// <summary>读取待处理的 Outbox 事件（Status=Pending 且 NextRetryAt &lt;= now）。</summary>
    Task<IReadOnlyList<OutboxEventEntity>> FetchPendingAsync(
        int batchSize = 50, CancellationToken ct = default);

    /// <summary>标记事件处理成功。</summary>
    Task MarkSucceededAsync(string eventId, CancellationToken ct = default);

    /// <summary>标记事件处理失败，递增重试次数，设置下次重试时间。</summary>
    Task MarkFailedAsync(
        string eventId, int retryCount, DateTimeOffset nextRetryAt,
        CancellationToken ct = default);
}
