using NetCasbin;
using Microsoft.Extensions.Logging;
using Rbac.Application.Auditing;
using Rbac.Application.Policies;
using Rbac.Domain.ValueObjects;
using StackExchange.Redis;

namespace Rbac.Infrastructure.Casbin;

/// <summary>
/// Casbin Enforcer 提供者。
///
/// 实现 <see cref="ICasbinPolicySyncService"/>。
/// 维护进程内的"不可变 Enforcer 引用"：
/// - Enforce 请求读取当前原子引用（非阻塞）。
/// - reload 在后台构建新实例，成功后原子替换，失败时保留旧引用。
/// - 每个 project 独立持有自己的 Enforcer 引用槽。
///
/// 线程安全：通过 Interlocked.Exchange 保证原子替换。
/// </summary>
public sealed class CasbinEnforcerProvider : ICasbinPolicySyncService, IDisposable
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

    // ── 热路径：读取当前 Enforcer ─────────────────────────────────

    /// <summary>
    /// 获取 project 的当前 Enforcer。若尚未初始化则同步触发首次 reload。
    /// </summary>
    public async Task<Enforcer> GetEnforcerAsync(string project, CancellationToken ct = default)
    {
        var slot = _slots.GetOrAdd(project, _ => new EnforcerSlot());
        if (slot.Current is null)
            await SyncAsync(new ProjectCode(project), ct);
        return slot.Current!;
    }

    // ── ICasbinPolicySyncService ──────────────────────────────────

    /// <summary>
    /// 从 MySQL 重建 Enforcer 并原子替换引用。
    /// 失败时保留旧引用，不让鉴权进入无策略状态。
    /// </summary>
    public async Task SyncAsync(ProjectCode project, CancellationToken ct = default)
    {
        var slot = _slots.GetOrAdd(project.Value, _ => new EnforcerSlot());
        var oldVersion = slot.LoadedVersion;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 从 MySQL 构建新 Enforcer（工厂保证不复用旧实例）
            var newEnforcer = await _factory.BuildAsync(project, ct);

            // 读取当前 Redis policy version
            var newVersion = (long?)await _redisDb.StringGetAsync(
                RbacRedisKeys.PolicyVersion(project.Value)) ?? 0L;

            // 原子替换（Interlocked.Exchange 保证线程安全）
            Interlocked.Exchange(ref slot.EnforcerRef, newEnforcer);
            Volatile.Write(ref slot.LoadedVersionField, newVersion);

            sw.Stop();
            _logger.LogInformation(
                "Enforcer synced project={P} oldVersion={Old} newVersion={New} elapsedMs={Ms}",
                project.Value, oldVersion, newVersion, sw.ElapsedMilliseconds);

            await _auditEmitter.EmitAsync(new CasbinReloadAuditEvent
            {
                Project = project.Value,
                OldPolicyVersion = oldVersion,
                NewPolicyVersion = newVersion,
                Result = "Succeeded",
                ElapsedMs = sw.ElapsedMilliseconds,
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Enforcer sync FAILED project={P} oldVersion={Old} elapsedMs={Ms}. Keeping old Enforcer.",
                project.Value, oldVersion, sw.ElapsedMilliseconds);

            await _auditEmitter.EmitAsync(new CasbinReloadAuditEvent
            {
                Project = project.Value,
                OldPolicyVersion = oldVersion,
                NewPolicyVersion = oldVersion,
                Result = "Failed",
                FailureReason = ex.Message,
                ElapsedMs = sw.ElapsedMilliseconds,
            });
            // 不更新 slot，保留旧 Enforcer
        }
    }

    public void Dispose() => _slots.Clear();
}
