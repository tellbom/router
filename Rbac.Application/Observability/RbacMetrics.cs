using System.Diagnostics.Metrics;

namespace Rbac.Application.Observability;

/// <summary>
/// RBAC 权限中心可观测性指标定义。
///
/// 使用 .NET System.Diagnostics.Metrics（OpenTelemetry 兼容），
/// 所有指标通过 IMeterFactory 注册，由 OpenTelemetry / Prometheus 导出。
///
/// 指标覆盖：
/// - Redis permset hit/miss（鉴权热路径命中率）
/// - FusionCache L1/L2 hit/miss（缓存分层命中率）
/// - Casbin Enforce QPS（策略引擎调用频率）
/// - ES 同步延迟（Outbox 事件从写入到 ES 完成的耗时）
/// - Outbox 重试次数（同步稳定性指标）
/// </summary>
public sealed class RbacMetrics : IDisposable
{
    public const string MeterName = "Rbac.Authorization";

    private readonly Meter _meter;

    // ── Redis permset ─────────────────────────────────────────────

    /// <summary>permset SISMEMBER 命中次数（鉴权热路径）。</summary>
    public readonly Counter<long> PermsetHits;

    /// <summary>permset SISMEMBER 未命中次数（触发 Casbin 兜底）。</summary>
    public readonly Counter<long> PermsetMisses;

    /// <summary>permset 重建次数（懒重建触发）。</summary>
    public readonly Counter<long> PermsetRebuilds;

    /// <summary>permset 重建因版本冲突被丢弃次数。</summary>
    public readonly Counter<long> PermsetRebuildDiscards;

    // ── FusionCache L1 / L2 ───────────────────────────────────────

    /// <summary>FusionCache L1 本地缓存命中次数。</summary>
    public readonly Counter<long> FusionCacheL1Hits;

    /// <summary>FusionCache L1 未命中，回源到 L2 Redis 次数。</summary>
    public readonly Counter<long> FusionCacheL1Misses;

    /// <summary>FusionCache L2 Redis 命中次数。</summary>
    public readonly Counter<long> FusionCacheL2Hits;

    /// <summary>FusionCache L2 未命中，回源到 MySQL 次数。</summary>
    public readonly Counter<long> FusionCacheL2Misses;

    // ── Casbin Enforce ────────────────────────────────────────────

    /// <summary>Casbin Enforce 调用总次数。</summary>
    public readonly Counter<long> CasbinEnforceCalls;

    /// <summary>Casbin Enforce allow 次数。</summary>
    public readonly Counter<long> CasbinEnforceAllows;

    /// <summary>Casbin Enforce deny 次数。</summary>
    public readonly Counter<long> CasbinEnforceDenies;

    /// <summary>Casbin Enforcer reload 次数。</summary>
    public readonly Counter<long> CasbinReloads;

    /// <summary>Casbin Enforcer reload 失败次数。</summary>
    public readonly Counter<long> CasbinReloadFailures;

    /// <summary>Casbin Enforce 耗时（毫秒）直方图。</summary>
    public readonly Histogram<double> CasbinEnforceLatencyMs;

    // ── ES 同步延迟 ───────────────────────────────────────────────

    /// <summary>ES Outbox 增量同步耗时（毫秒）直方图。</summary>
    public readonly Histogram<double> EsSyncLatencyMs;

    /// <summary>ES 全量重建耗时（秒）直方图。</summary>
    public readonly Histogram<double> EsReindexDurationSec;

    /// <summary>ES 同步成功次数。</summary>
    public readonly Counter<long> EsSyncSuccesses;

    /// <summary>ES 同步失败次数。</summary>
    public readonly Counter<long> EsSyncFailures;

    // ── Outbox 重试 ───────────────────────────────────────────────

    /// <summary>Outbox 事件处理成功次数。</summary>
    public readonly Counter<long> OutboxSuccesses;

    /// <summary>Outbox 事件重试次数。</summary>
    public readonly Counter<long> OutboxRetries;

    /// <summary>Outbox 事件标记 Failed 次数（超过最大重试）。</summary>
    public readonly Counter<long> OutboxFinalFailures;

    // ── 鉴权结果 ──────────────────────────────────────────────────

    /// <summary>鉴权 allow 次数（含 super、Redis、Casbin 来源）。</summary>
    public readonly Counter<long> AuthorizationAllows;

    /// <summary>鉴权 deny 次数。</summary>
    public readonly Counter<long> AuthorizationDenies;

    /// <summary>鉴权 error 次数（服务不可用降级）。</summary>
    public readonly Counter<long> AuthorizationErrors;

    /// <summary>project 校验失败次数（未授权/伪造）。</summary>
    public readonly Counter<long> ProjectAuthorizationFailures;

    public RbacMetrics()
    {
        _meter = new Meter(MeterName);

        PermsetHits = _meter.CreateCounter<long>("rbac.permset.hits", "hits",
            "Number of Redis permset SISMEMBER cache hits");
        PermsetMisses = _meter.CreateCounter<long>("rbac.permset.misses", "misses",
            "Number of Redis permset SISMEMBER cache misses");
        PermsetRebuilds = _meter.CreateCounter<long>("rbac.permset.rebuilds", "rebuilds",
            "Number of permset lazy rebuild triggers");
        PermsetRebuildDiscards = _meter.CreateCounter<long>("rbac.permset.rebuild.discards", "discards",
            "Number of permset rebuilds discarded due to version conflict");

        FusionCacheL1Hits = _meter.CreateCounter<long>("rbac.fusioncache.l1.hits", "hits",
            "FusionCache L1 in-process cache hits");
        FusionCacheL1Misses = _meter.CreateCounter<long>("rbac.fusioncache.l1.misses", "misses",
            "FusionCache L1 misses, falling back to L2 Redis");
        FusionCacheL2Hits = _meter.CreateCounter<long>("rbac.fusioncache.l2.hits", "hits",
            "FusionCache L2 Redis hits");
        FusionCacheL2Misses = _meter.CreateCounter<long>("rbac.fusioncache.l2.misses", "misses",
            "FusionCache L2 misses, falling back to MySQL");

        CasbinEnforceCalls = _meter.CreateCounter<long>("rbac.casbin.enforce.calls", "calls",
            "Total Casbin Enforce() invocations");
        CasbinEnforceAllows = _meter.CreateCounter<long>("rbac.casbin.enforce.allows", "allows",
            "Casbin Enforce allow results");
        CasbinEnforceDenies = _meter.CreateCounter<long>("rbac.casbin.enforce.denies", "denies",
            "Casbin Enforce deny results");
        CasbinReloads = _meter.CreateCounter<long>("rbac.casbin.reloads", "reloads",
            "Casbin Enforcer reload triggers");
        CasbinReloadFailures = _meter.CreateCounter<long>("rbac.casbin.reload.failures", "failures",
            "Casbin Enforcer reload failures");
        CasbinEnforceLatencyMs = _meter.CreateHistogram<double>("rbac.casbin.enforce.latency", "ms",
            "Casbin Enforce() latency in milliseconds");

        EsSyncLatencyMs = _meter.CreateHistogram<double>("rbac.es.sync.latency", "ms",
            "ES Outbox incremental sync latency in milliseconds");
        EsReindexDurationSec = _meter.CreateHistogram<double>("rbac.es.reindex.duration", "s",
            "ES full reindex duration in seconds");
        EsSyncSuccesses = _meter.CreateCounter<long>("rbac.es.sync.successes", "successes");
        EsSyncFailures = _meter.CreateCounter<long>("rbac.es.sync.failures", "failures");

        OutboxSuccesses = _meter.CreateCounter<long>("rbac.outbox.successes", "events");
        OutboxRetries = _meter.CreateCounter<long>("rbac.outbox.retries", "retries");
        OutboxFinalFailures = _meter.CreateCounter<long>("rbac.outbox.final_failures", "failures");

        AuthorizationAllows = _meter.CreateCounter<long>("rbac.authorization.allows", "allows");
        AuthorizationDenies = _meter.CreateCounter<long>("rbac.authorization.denies", "denies");
        AuthorizationErrors = _meter.CreateCounter<long>("rbac.authorization.errors", "errors");
        ProjectAuthorizationFailures = _meter.CreateCounter<long>(
            "rbac.project.auth.failures", "failures",
            "project authorization failures (unauthorized or forged)");
    }

    public void Dispose() => _meter.Dispose();
}
