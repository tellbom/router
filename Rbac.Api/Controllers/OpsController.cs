using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nest;
using Rbac.Api.Filters;
using Rbac.Application.Cache;
using Rbac.Application.Outbox;
using Rbac.Infrastructure.Elasticsearch.Reindex;
using Rbac.Infrastructure.MySql.Mapping;

namespace Rbac.Api.Controllers;

/// <summary>
/// Operational endpoints guarded by X-Ops-Key.
/// </summary>
[ApiController]
[Route("ops")]
[ServiceFilter(typeof(OpsAuthorizationFilter))]
public sealed class OpsController : ControllerBase
{
    private static readonly string[] ReindexAliases =
    {
        "rbac_user_index",
        "rbac_group_index",
        "rbac_rule_index",
        "rbac_permission_view_index",
        "rbac_audit_log_index",
    };

    private readonly RbacEsFullReindexService _reindexService;
    private readonly IRbacCacheInvalidator _cacheInvalidator;
    private readonly IOutboxAdminService _outboxAdmin;
    private readonly RbacDbContext _db;
    private readonly IElasticClient _esClient;
    private readonly ILogger<OpsController> _logger;

    public OpsController(
        RbacEsFullReindexService reindexService,
        IRbacCacheInvalidator cacheInvalidator,
        IOutboxAdminService outboxAdmin,
        RbacDbContext db,
        IElasticClient esClient,
        ILogger<OpsController> logger)
    {
        _reindexService = reindexService;
        _cacheInvalidator = cacheInvalidator;
        _outboxAdmin = outboxAdmin;
        _db = db;
        _esClient = esClient;
        _logger = logger;
    }

    [HttpPost("reindex")]
    public async Task<IActionResult> Reindex(
        [FromQuery] string? project = null,
        [FromQuery] string? index = null,
        CancellationToken ct = default)
    {
        _logger.LogWarning(
            "Ops reindex triggered project={Project} index={Index}",
            project ?? "ALL", index ?? "ALL");

        if (!string.IsNullOrWhiteSpace(index))
        {
            var result = await ReindexSingleAsync(index, project, ct);
            return Ok(new
            {
                code = result.IsSuccess ? 0 : 50000,
                msg = result.IsSuccess ? "ok" : result.FailureReason,
                data = new { result.Alias, result.NewIndex, result.DocumentCount },
            });
        }

        var results = new List<ReindexResult>();
        foreach (var alias in ReindexAliases)
        {
            results.Add(await ReindexSingleAsync(alias, project, ct));
        }

        var allSucceeded = results.All(r => r.IsSuccess);
        return Ok(new
        {
            code = allSucceeded ? 0 : 50000,
            msg = allSucceeded ? "ok" : "partial failure",
            data = results.Select(r => new
            {
                r.Alias,
                r.NewIndex,
                r.DocumentCount,
                r.IsSuccess,
                r.FailureReason,
            }),
        });
    }

    [HttpPost("cache-flush")]
    public async Task<IActionResult> CacheFlush(
        [FromQuery] string? project = null,
        CancellationToken ct = default)
    {
        _logger.LogWarning("Ops cache-flush triggered project={Project}", project ?? "ALL");

        var projects = string.IsNullOrWhiteSpace(project)
            ? await GetAllProjectsAsync(ct)
            : new[] { project };

        foreach (var p in projects)
        {
            await _cacheInvalidator.DeleteMenuTreeAsync(p, ct);
            await _cacheInvalidator.DeleteApiMapAsync(p, ct);
            await _cacheInvalidator.IncrProjectVersionAsync(p, ct);
            await _cacheInvalidator.PublishInvalidationAsync(new RbacCacheInvalidationEvent
            {
                Project = p,
                ResourceType = CacheResourceType.All,
                TraceId = HttpContext.TraceIdentifier,
            }, ct);
        }

        return Ok(new
        {
            code = 0,
            msg = "ok",
            data = new { project = project ?? "ALL", flushed = projects },
        });
    }

    [HttpPost("outbox-retry")]
    public async Task<IActionResult> OutboxRetry(
        [FromQuery] string? project = null,
        CancellationToken ct = default)
    {
        _logger.LogWarning("Ops outbox-retry triggered project={Project}", project ?? "ALL");
        var count = await _outboxAdmin.ResetFailedToPendingAsync(project, ct);

        return Ok(new
        {
            code = 0,
            msg = "ok",
            data = new { reset = count, project = project ?? "ALL" },
        });
    }

    [HttpGet("health")]
    public async Task<IActionResult> Health(
        [FromQuery] string? project = null,
        CancellationToken ct = default)
    {
        var outboxCounts = await _outboxAdmin.GetStatusCountsAsync(project, ct);
        var esCounts = new Dictionary<string, long>();

        foreach (var alias in ReindexAliases)
        {
            try
            {
                var resp = await _esClient.CountAsync<object>(c => c.Index(alias), ct);
                esCounts[alias] = resp.IsValid ? resp.Count : -1;
            }
            catch
            {
                esCounts[alias] = -1;
            }
        }

        var hasIssue = outboxCounts.Failed > 0 || esCounts.Values.Any(v => v < 0);
        return Ok(new
        {
            code = 0,
            msg = "ok",
            data = new
            {
                status = hasIssue ? "degraded" : "healthy",
                outbox = outboxCounts,
                elasticsearch = esCounts,
                suggestion = hasIssue ? BuildSuggestion(outboxCounts, esCounts) : null,
            },
        });
    }

    private Task<ReindexResult> ReindexSingleAsync(
        string alias, string? project, CancellationToken ct) =>
        alias switch
        {
            "rbac_user_index" => _reindexService.ReindexUsersAsync(project, ct),
            "rbac_group_index" => _reindexService.ReindexGroupsAsync(project, ct),
            "rbac_rule_index" => _reindexService.ReindexRulesAsync(project, ct),
            "rbac_permission_view_index" => _reindexService.ReindexPermissionViewAsync(project, ct),
            "rbac_audit_log_index" => _reindexService.ReindexAuditLogAsync(project, ct),
            _ => Task.FromResult(ReindexResult.Failure(alias, alias, $"Unknown index: {alias}")),
        };

    private async Task<IReadOnlyList<string>> GetAllProjectsAsync(CancellationToken ct)
    {
        var grants = await _db.ProjectGrants.AsNoTracking().ToListAsync(ct);
        var groups = await _db.Groups.AsNoTracking().ToListAsync(ct);
        var rules = await _db.Rules.AsNoTracking().ToListAsync(ct);
        var maps = await _db.ApiPermissionMaps.AsNoTracking().ToListAsync(ct);

        return grants.Select(g => g.Project.Value)
            .Concat(groups.Select(g => g.Project.Value))
            .Concat(rules.Select(r => r.Project.Value))
            .Concat(maps.Select(m => m.Project.Value))
            .Where(p => p != string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? BuildSuggestion(
        OutboxStatusCounts outbox,
        Dictionary<string, long> esCounts)
    {
        var hints = new List<string>();

        if (outbox.Failed > 0)
            hints.Add($"Outbox has {outbox.Failed} failed events; call POST /ops/outbox-retry after fixing processors.");

        var unavailable = esCounts.Where(kv => kv.Value < 0).Select(kv => kv.Key).ToList();
        if (unavailable.Count > 0)
            hints.Add($"ES indexes unavailable: {string.Join(", ", unavailable)}; check ES and reindex.");

        return hints.Count > 0 ? string.Join(" ", hints) : null;
    }
}
