using NetCasbin;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using Rbac.Application.Repositories;
using Rbac.Domain.ValueObjects;

namespace Rbac.Infrastructure.Casbin;

/// <summary>
/// Casbin Enforcer 生命周期管理器。
///
/// 实现"不可变引用"策略：
/// 1. 当前 Enforcer 保存在进程内原子引用（Interlocked）中。
/// 2. policy version 变化时后台创建新 Enforcer 实例。
/// 3. 新 Enforcer 从 MySQL 真相表加载 g / p policy。
/// 4. 加载成功后原子替换当前引用，失败时保留旧引用。
/// 5. Enforce 请求始终读取当前引用，不等待 reload，不阻塞热路径。
///
/// reload 过程必须记录 project、旧/新 version、结果、耗时、失败原因。
/// </summary>
public sealed class CasbinPolicyVersionWatcher : IDisposable
{
    private readonly IDatabase _redisDb;
    private readonly ICasbinPolicyRepository _policyRepository;
    private readonly RbacCasbinModelProvider _modelProvider;
    private readonly ILogger<CasbinPolicyVersionWatcher> _logger;

    // 进程内原子 Enforcer 引用（每个 project 独立）
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, EnforcerSlot> _slots = new();

    public CasbinPolicyVersionWatcher(
        IDatabase redisDb,
        ICasbinPolicyRepository policyRepository,
        RbacCasbinModelProvider modelProvider,
        ILogger<CasbinPolicyVersionWatcher> logger)
    {
        _redisDb = redisDb;
        _policyRepository = policyRepository;
        _modelProvider = modelProvider;
        _logger = logger;
    }

    // ── 热路径：读取当前 Enforcer ─────────────────────────────────

    /// <summary>
    /// 获取 project 的当前 Enforcer 引用（原子读取）。
    /// 若尚未初始化，触发同步 reload 并返回。
    /// </summary>
    public async Task<Enforcer> GetEnforcerAsync(string project, CancellationToken ct = default)
    {
        var slot = _slots.GetOrAdd(project, _ => new EnforcerSlot());

        if (slot.Current is null)
        {
            // 首次使用，同步 reload
            await ReloadAsync(project, ct);
        }

        return slot.Current!;
    }

    // ── 版本检测与后台 reload ─────────────────────────────────────

    /// <summary>
    /// 检测 policy version 是否变化，若变化则后台触发 reload。
    /// 由定时器或 Pub/Sub 事件调用，不阻塞调用方。
    /// </summary>
    public async Task CheckAndReloadIfNeededAsync(string project, CancellationToken ct = default)
    {
        var slot = _slots.GetOrAdd(project, _ => new EnforcerSlot());
        var redisVersion = (long?)await _redisDb.StringGetAsync(
            RbacRedisKeys.PolicyVersion(project)) ?? 0L;

        if (redisVersion <= slot.LoadedVersion) return;

        _logger.LogInformation(
            "Policy version changed project={Project} old={Old} new={New}, triggering reload.",
            project, slot.LoadedVersion, redisVersion);

        // 后台 reload，不 await（不阻塞调用方）
        _ = Task.Run(() => ReloadAsync(project, ct), ct);
    }

    /// <summary>
    /// 从 MySQL 真相库重建 Enforcer 并原子替换引用。
    /// 失败时保留旧引用，不让鉴权进入无策略状态。
    /// </summary>
    public async Task ReloadAsync(string project, CancellationToken ct = default)
    {
        var slot = _slots.GetOrAdd(project, _ => new EnforcerSlot());
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var oldVersion = slot.LoadedVersion;

        try
        {
            var projectCode = new ProjectCode(project);

            // 从 MySQL 读取 g / p policy（唯一合法来源）
            var grouping = await _policyRepository.GetGroupingPoliciesAsync(projectCode, ct);
            var permission = await _policyRepository.GetPermissionPoliciesAsync(projectCode, ct);

            // 构建新 Enforcer
            var newEnforcer = _modelProvider.BuildEnforcer(grouping, permission);

            // 读取当前 Redis policy version
            var newVersion = (long?)await _redisDb.StringGetAsync(
                RbacRedisKeys.PolicyVersion(project)) ?? 0L;

            // 原子替换
            Interlocked.Exchange(ref slot.EnforcerRef, newEnforcer);
            Volatile.Write(ref slot.LoadedVersionField, newVersion);

            sw.Stop();
            _logger.LogInformation(
                "Casbin Enforcer reloaded project={Project} oldVersion={Old} newVersion={New} elapsedMs={Ms}",
                project, oldVersion, newVersion, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Casbin Enforcer reload FAILED project={Project} oldVersion={Old} elapsedMs={Ms}. Keeping old Enforcer.",
                project, oldVersion, sw.ElapsedMilliseconds);
            // 保留旧引用，不更新 slot
        }
    }

    public void Dispose()
    {
        _slots.Clear();
    }
}

/// <summary>
/// 单个 project 的 Enforcer 原子持有槽。
/// 使用 Interlocked.Exchange 实现无锁原子替换。
/// </summary>
internal sealed class EnforcerSlot
{
    internal Enforcer? EnforcerRef;
    internal long LoadedVersionField;

    public Enforcer? Current => Volatile.Read(ref EnforcerRef);
    public long LoadedVersion => Volatile.Read(ref LoadedVersionField);
}
