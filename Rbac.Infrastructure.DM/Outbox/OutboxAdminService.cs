using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rbac.Application.Outbox;
using Rbac.Infrastructure.DM.Mapping;

namespace Rbac.Infrastructure.DM.Outbox;

/// <summary>
/// EF Core implementation of outbox administrative operations.
/// </summary>
public sealed class OutboxAdminService : IOutboxAdminService
{
    private readonly RbacDbContext _db;
    private readonly ILogger<OutboxAdminService> _logger;

    public OutboxAdminService(RbacDbContext db, ILogger<OutboxAdminService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OutboxAdminEventDto>> GetFailedEventsAsync(
        string? project = null, int limit = 50, CancellationToken ct = default)
    {
        var query = _db.OutboxEvents
            .Where(e => e.Status == OutboxStatus.Failed);

        if (!string.IsNullOrWhiteSpace(project))
            query = query.Where(e => e.Project == project);

        return await query
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .Select(e => new OutboxAdminEventDto
            {
                EventId = e.EventId,
                EventType = e.EventType,
                Project = e.Project,
                Userid = e.Userid,
                GroupCode = e.GroupCode,
                Status = e.Status,
                RetryCount = e.RetryCount,
                NextRetryAt = e.NextRetryAt,
                CreatedAt = e.CreatedAt,
                ProcessedAt = e.ProcessedAt,
            })
            .ToListAsync(ct);
    }

    public async Task<int> ResetFailedToPendingAsync(
        string? project = null, CancellationToken ct = default)
    {
        var query = _db.OutboxEvents
            .Where(e => e.Status == OutboxStatus.Failed);

        if (!string.IsNullOrWhiteSpace(project))
            query = query.Where(e => e.Project == project);

        var failed = await query.ToListAsync(ct);
        if (failed.Count == 0) return 0;

        foreach (var evt in failed)
        {
            evt.Status = OutboxStatus.Pending;
            evt.RetryCount = 0;
            evt.NextRetryAt = null;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Reset {Count} failed outbox events to pending project={Project}",
            failed.Count, project ?? "ALL");

        return failed.Count;
    }

    public async Task<OutboxStatusCounts> GetStatusCountsAsync(
        string? project = null, CancellationToken ct = default)
    {
        var query = _db.OutboxEvents.AsQueryable();

        if (!string.IsNullOrWhiteSpace(project))
            query = query.Where(e => e.Project == project);

        var counts = await query
            .GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Count = g.LongCount() })
            .ToListAsync(ct);

        return new OutboxStatusCounts
        {
            Pending = counts.FirstOrDefault(c => c.Status == OutboxStatus.Pending)?.Count ?? 0,
            Succeeded = counts.FirstOrDefault(c => c.Status == OutboxStatus.Succeeded)?.Count ?? 0,
            Failed = counts.FirstOrDefault(c => c.Status == OutboxStatus.Failed)?.Count ?? 0,
        };
    }
}
