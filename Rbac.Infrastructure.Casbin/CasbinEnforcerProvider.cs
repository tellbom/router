using Casbin;
using Microsoft.Extensions.Logging;
using Rbac.Application.Auditing;
using Rbac.Application.Authorization;
using Rbac.Application.Policies;
using Rbac.Domain.ValueObjects;
using Rbac.Infrastructure.Redis;
using StackExchange.Redis;

namespace Rbac.Infrastructure.Casbin;

/// <summary>
/// Casbin Enforcer 提供者，同時實現 ICasbinPolicySyncService 和 ICasbinEnforcer。
/// 維護進程內的不可變 Enforcer 引用，通過 Interlocked 原子替換。
/// </summary>
public sealed class CasbinEnforcerProvider : ICasbinPolicySyncService, ICasbinEnforcer, IDisposable
{
    private readonly CasbinEnforcerFactory _factory;
    private readonly IDatabase _redisDb;
    private readonly IAuditEventEmitter _auditEmitter;
    private readonly ILogger<CasbinEnforcerProvider> _logger;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, EnforcerSlot> _slots = new();

    public CasbinEnforcerProvider(
        CasbinEnforcerFactory factory,
        IDatabase redisDb,
        IAuditEventEmitter auditEmitter,
        ILogger<CasbinEnforcerProvider> logger)
    {
        _factory = factory;
        _redisDb = redisDb;
        _auditEmitter = auditEmitter;
        _logger = logger;
    }

    // ── ICasbinEnforcer ───────────────────────────────────────────

    public async Task<bool> EnforceAsync(
        string userid, string project, string permissionCode, string action,
        CancellationToken ct = default)
    {
        var enforcer = await GetEnforcerAsync(project, ct);
        return enforcer.Enforce(userid, project, permissionCode, action);
    }

    public Task CheckAndReloadIfNeededAsync(string project, CancellationToken ct = default)
    {
        _ = Task.Run(async () =>
        {
            var slot = _slots.GetOrAdd(project, _ => new EnforcerSlot());
            var redisVersion = (long?)await _redisDb.StringGetAsync(
                RbacRedisKeys.PolicyVersion(project)) ?? 0L;
            if (redisVersion > slot.LoadedVersion)
                await SyncAsync(new ProjectCode(project), ct);
        }, ct);
        return Task.CompletedTask;
    }

    // ── ICasbinPolicySyncService ──────────────────────────────────

    public async Task SyncAsync(ProjectCode project, CancellationToken ct = default)
    {
        var slot = _slots.GetOrAdd(project.Value, _ => new EnforcerSlot());
        var oldVersion = slot.LoadedVersion;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var newEnforcer = await _factory.BuildAsync(project, ct);
            var newVersion = (long?)await _redisDb.StringGetAsync(
                RbacRedisKeys.PolicyVersion(project.Value)) ?? 0L;

            Interlocked.Exchange(ref slot.EnforcerRef, newEnforcer);
            Volatile.Write(ref slot.LoadedVersionField, newVersion);

            sw.Stop();
            _logger.LogInformation("Enforcer synced project={P} oldVersion={Old} newVersion={New} elapsedMs={Ms}",
                project.Value, oldVersion, newVersion, sw.ElapsedMilliseconds);

            await _auditEmitter.EmitAsync(CasbinReloadAuditEventExtensions.Success(
                project.Value, oldVersion, newVersion, sw.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Enforcer sync FAILED project={P} oldVersion={Old} elapsedMs={Ms}",
                project.Value, oldVersion, sw.ElapsedMilliseconds);

            await _auditEmitter.EmitAsync(CasbinReloadAuditEventExtensions.Failure(
                project.Value, oldVersion, ex.Message, sw.ElapsedMilliseconds));
        }
    }

    // ── 內部 ──────────────────────────────────────────────────────

    private async Task<Enforcer> GetEnforcerAsync(string project, CancellationToken ct)
    {
        var slot = _slots.GetOrAdd(project, _ => new EnforcerSlot());
        if (slot.Current is null)
            await SyncAsync(new ProjectCode(project), ct);
        return slot.Current!;
    }

    public void Dispose() => _slots.Clear();
}
