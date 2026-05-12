namespace Rbac.Application.Outbox;

/// <summary>
/// Administrative queries and repairs for the outbox table.
/// </summary>
public interface IOutboxAdminService
{
    Task<IReadOnlyList<OutboxAdminEventDto>> GetFailedEventsAsync(
        string? project = null, int limit = 50, CancellationToken ct = default);

    Task<int> ResetFailedToPendingAsync(
        string? project = null, CancellationToken ct = default);

    Task<OutboxStatusCounts> GetStatusCountsAsync(
        string? project = null, CancellationToken ct = default);
}

public sealed class OutboxAdminEventDto
{
    public string EventId { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public string? Userid { get; init; }
    public string? GroupCode { get; init; }
    public string Status { get; init; } = string.Empty;
    public int RetryCount { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
}

public sealed class OutboxStatusCounts
{
    public long Pending { get; init; }
    public long Succeeded { get; init; }
    public long Failed { get; init; }
}
