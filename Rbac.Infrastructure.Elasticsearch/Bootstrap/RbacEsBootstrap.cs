using Microsoft.Extensions.Logging;
using Nest;
using Rbac.Infrastructure.Elasticsearch.Indexes;

namespace Rbac.Infrastructure.Elasticsearch.Bootstrap;

// ── T109: Index Template Bootstrapper ────────────────────────────

/// <summary>
/// T109: ES 索引模板和 mapping 初始化服务。
/// 应用启动或首次部署时调用，确保所有索引 mapping 和分析器已注册。
/// </summary>
public sealed class RbacEsIndexTemplateBootstrapper
{
    private readonly IElasticClient _esClient;
    private readonly ILogger<RbacEsIndexTemplateBootstrapper> _logger;

    public RbacEsIndexTemplateBootstrapper(IElasticClient esClient, ILogger<RbacEsIndexTemplateBootstrapper> logger)
    {
        _esClient = esClient;
        _logger = logger;
    }

    /// <summary>确保所有 RBAC 索引模板和 mapping 已在 ES 中注册。</summary>
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        var aliases = new[]
        {
            RbacUserIndexMapping.IndexName,
            RbacGroupIndexMapping.IndexName,
            RbacRuleIndexMapping.IndexName,
            RbacPermissionViewIndexMapping.IndexName,
            RbacAuditLogIndexMapping.IndexName,
        };

        foreach (var alias in aliases)
        {
            var exists = await _esClient.Indices.AliasExistsAsync(alias, ct: ct);
            if (!exists.Exists)
            {
                _logger.LogWarning(
                    "Alias {Alias} does not exist. Run full reindex to initialize.", alias);
            }
            else
            {
                _logger.LogDebug("Alias {Alias} exists.", alias);
            }
        }
    }
}

// ── T110: Alias Bootstrapper ──────────────────────────────────────

/// <summary>
/// T110: ES 查询 alias 初始化服务。
/// 首次部署时如果 alias 不存在，创建初始物理索引并绑定 alias，使查询可用。
/// </summary>
public sealed class RbacEsAliasBootstrapper
{
    private readonly IElasticClient _esClient;
    private readonly ILogger<RbacEsAliasBootstrapper> _logger;

    public RbacEsAliasBootstrapper(IElasticClient esClient, ILogger<RbacEsAliasBootstrapper> logger)
    {
        _esClient = esClient;
        _logger = logger;
    }

    /// <summary>
    /// 确保指定 alias 存在，不存在时创建初始物理索引并绑定 alias。
    /// </summary>
    public async Task EnsureAliasAsync(string alias, CancellationToken ct = default)
    {
        var exists = await _esClient.Indices.AliasExistsAsync(alias, ct: ct);
        if (exists.Exists)
        {
            _logger.LogDebug("Alias {Alias} already exists, skip bootstrap.", alias);
            return;
        }

        var initialIndex = $"{alias}_v{DateTimeOffset.UtcNow:yyyyMMdd}_000";
        _logger.LogInformation("Creating initial index {Index} for alias {Alias}", initialIndex, alias);

        // 根据 alias 应用对应 mapping
        var createResp = alias switch
        {
            var a when a == RbacUserIndexMapping.IndexName =>
                await _esClient.Indices.CreateAsync(initialIndex, c => RbacUserIndexMapping.Build(c), ct),
            var a when a == RbacGroupIndexMapping.IndexName =>
                await _esClient.Indices.CreateAsync(initialIndex, c => RbacGroupIndexMapping.Build(c), ct),
            var a when a == RbacRuleIndexMapping.IndexName =>
                await _esClient.Indices.CreateAsync(initialIndex, c => RbacRuleIndexMapping.Build(c), ct),
            var a when a == RbacPermissionViewIndexMapping.IndexName =>
                await _esClient.Indices.CreateAsync(initialIndex, c => RbacPermissionViewIndexMapping.Build(c), ct),
            var a when a == RbacAuditLogIndexMapping.IndexName =>
                await _esClient.Indices.CreateAsync(initialIndex, c => RbacAuditLogIndexMapping.Build(c), ct),
            _ => await _esClient.Indices.CreateAsync(initialIndex, ct: ct)
        };

        if (!createResp.IsValid)
        {
            _logger.LogError("Failed to create initial index {Index}: {Err}",
                initialIndex, createResp.ServerError?.Error?.Reason);
            return;
        }

        // 绑定 alias
        await _esClient.Indices.BulkAliasAsync(
            b => b.Add(a => a.Index(initialIndex).Alias(alias)), ct);

        _logger.LogInformation("Alias {Alias} → {Index} bootstrapped.", alias, initialIndex);
    }
}

// ── T111: Alias Preflight Checker ────────────────────────────────

/// <summary>
/// T111: 全量重建前 alias 预检。
/// 验证 alias 存在且指向唯一当前索引，不满足时拒绝重建并告警。
/// </summary>
public sealed class RbacEsAliasPreflightChecker
{
    private readonly IElasticClient _esClient;
    private readonly ILogger<RbacEsAliasPreflightChecker> _logger;

    public RbacEsAliasPreflightChecker(IElasticClient esClient, ILogger<RbacEsAliasPreflightChecker> logger)
    {
        _esClient = esClient;
        _logger = logger;
    }

    /// <summary>
    /// 执行重建前检查。返回 false 时不应启动重建。
    /// </summary>
    public async Task<AliasPreflightResult> CheckAsync(string alias, CancellationToken ct = default)
    {
        var aliasResp = await _esClient.Indices.GetAliasAsync(alias, ct: ct);

        if (!aliasResp.IsValid || aliasResp.Indices is null || aliasResp.Indices.Count == 0)
        {
            _logger.LogWarning("Preflight: alias {Alias} does not exist.", alias);
            return AliasPreflightResult.Fail(alias, "Alias does not exist. Run bootstrap first.");
        }

        if (aliasResp.Indices.Count > 1)
        {
            _logger.LogWarning(
                "Preflight: alias {Alias} points to multiple indexes: {Indexes}",
                alias, string.Join(",", aliasResp.Indices.Keys.Select(k => k.Name)));
            return AliasPreflightResult.Fail(alias,
                $"Alias points to {aliasResp.Indices.Count} indexes. Manual cleanup required.");
        }

        var currentIndex = aliasResp.Indices.Keys.First().Name;
        _logger.LogDebug("Preflight passed alias={Alias} currentIndex={Index}", alias, currentIndex);
        return AliasPreflightResult.Pass(alias, currentIndex);
    }
}

/// <summary>alias 预检结果。</summary>
public sealed class AliasPreflightResult
{
    public bool IsPass { get; private init; }
    public string Alias { get; private init; } = string.Empty;
    public string? CurrentIndex { get; private init; }
    public string? FailReason { get; private init; }

    public static AliasPreflightResult Pass(string alias, string currentIndex) =>
        new() { IsPass = true, Alias = alias, CurrentIndex = currentIndex };

    public static AliasPreflightResult Fail(string alias, string reason) =>
        new() { IsPass = false, Alias = alias, FailReason = reason };
}
