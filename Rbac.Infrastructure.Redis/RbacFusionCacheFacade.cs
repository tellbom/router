using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;
using Rbac.Application.Snapshots;

namespace Rbac.Infrastructure.Redis;

/// <summary>
/// FusionCache 统一访问门面。
///
/// 负责封装以下中等粒度对象的 L1+L2 缓存读取：
/// - 用户权限快照（snapshot）
/// - 菜单树（menu-tree）
/// - API 权限映射（api-map）
/// - project 授权关系（user-projects）
///
/// 明确不包办的操作（直接走 StackExchange.Redis）：
/// - permset SISMEMBER（由 RbacPermsetStore 负责）
/// - version 原子递增（INCR）
/// - 分布式锁（SET NX）
/// - Pub/Sub PUBLISH / SUBSCRIBE
/// </summary>
public sealed class RbacFusionCacheFacade
{
    private readonly IFusionCache _cache;
    private readonly ILogger<RbacFusionCacheFacade> _logger;

    // L1 TTL 配置（短，降低 Redis 压力）
    private static readonly TimeSpan SnapshotL1Ttl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MenuTreeL1Ttl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ApiMapL1Ttl = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan UserProjectsL1Ttl = TimeSpan.FromSeconds(60);

    // L2 Redis TTL
    private static readonly TimeSpan SnapshotL2Ttl = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan MenuTreeL2Ttl = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan ApiMapL2Ttl = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan UserProjectsL2Ttl = TimeSpan.FromMinutes(20);

    public RbacFusionCacheFacade(IFusionCache cache, ILogger<RbacFusionCacheFacade> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    // ── 用户权限快照 ──────────────────────────────────────────────

    /// <summary>
    /// 读取用户权限快照。L1 miss → L2 Redis → factory（MySQL 重建）。
    /// factory 为 null 时，未命中返回 null（由调用方决定是否触发重建）。
    /// </summary>
    public async Task<UserPermissionSnapshot?> GetSnapshotAsync(
        string project, string userid,
        Func<FusionCacheFactoryExecutionContext<UserPermissionSnapshot?>, CancellationToken, Task<UserPermissionSnapshot?>>? factory = null,
        CancellationToken ct = default)
    {
        var key = RbacRedisKeys.Snapshot(project, userid);
        return await _cache.GetOrSetAsync<UserPermissionSnapshot?>(
            key,
            factory ?? ((_, _) => Task.FromResult<UserPermissionSnapshot?>(null)),
            BuildOptions(SnapshotL1Ttl, SnapshotL2Ttl, failSafe: true),
            ct);
    }

    /// <summary>驱逐用户快照 L1 缓存（收到 Pub/Sub 失效事件时调用）。</summary>
    public Task EvictSnapshotAsync(string project, string userid) =>
        _cache.RemoveAsync(RbacRedisKeys.Snapshot(project, userid));

    // ── 菜单树 ────────────────────────────────────────────────────

    /// <summary>读取 project 全量启用菜单树。</summary>
    public async Task<T?> GetMenuTreeAsync<T>(
        string project,
        Func<FusionCacheFactoryExecutionContext<T?>, CancellationToken, Task<T?>> factory,
        CancellationToken ct = default) where T : class
    {
        var key = RbacRedisKeys.MenuTree(project);
        return await _cache.GetOrSetAsync<T?>(key, factory,
            BuildOptions(MenuTreeL1Ttl, MenuTreeL2Ttl, failSafe: true), ct);
    }

    /// <summary>驱逐菜单树 L1 缓存。</summary>
    public Task EvictMenuTreeAsync(string project) =>
        _cache.RemoveAsync(RbacRedisKeys.MenuTree(project));

    // ── API 权限映射 ──────────────────────────────────────────────

    /// <summary>读取 project 下 API 权限映射表。</summary>
    public async Task<T?> GetApiMapAsync<T>(
        string project,
        Func<FusionCacheFactoryExecutionContext<T?>, CancellationToken, Task<T?>> factory,
        CancellationToken ct = default) where T : class
    {
        var key = RbacRedisKeys.ApiMap(project);
        return await _cache.GetOrSetAsync<T?>(key, factory,
            BuildOptions(ApiMapL1Ttl, ApiMapL2Ttl, failSafe: true), ct);
    }

    /// <summary>驱逐 API 映射 L1 缓存。</summary>
    public Task EvictApiMapAsync(string project) =>
        _cache.RemoveAsync(RbacRedisKeys.ApiMap(project));

    // ── 用户 project 授权关系 ─────────────────────────────────────

    /// <summary>读取用户可访问的 project 授权关系。</summary>
    public async Task<T?> GetUserProjectsAsync<T>(
        string userid,
        Func<FusionCacheFactoryExecutionContext<T?>, CancellationToken, Task<T?>> factory,
        CancellationToken ct = default) where T : class
    {
        var key = RbacRedisKeys.UserProjects(userid);
        return await _cache.GetOrSetAsync<T?>(key, factory,
            BuildOptions(UserProjectsL1Ttl, UserProjectsL2Ttl, failSafe: false), ct);
    }

    /// <summary>驱逐用户 project 授权 L1 缓存。</summary>
    public Task EvictUserProjectsAsync(string userid) =>
        _cache.RemoveAsync(RbacRedisKeys.UserProjects(userid));

    // ── 私有辅助 ──────────────────────────────────────────────────

    private static FusionCacheEntryOptions BuildOptions(
        TimeSpan l1Ttl, TimeSpan l2Ttl, bool failSafe) =>
        new()
        {
            Duration = l1Ttl,
            DistributedCacheDuration = l2Ttl,
            IsFailSafeEnabled = failSafe,
            FailSafeMaxDuration = failSafe ? TimeSpan.FromHours(2) : TimeSpan.Zero,
            FailSafeThrottleDuration = failSafe ? TimeSpan.FromSeconds(30) : TimeSpan.Zero,
            FactorySoftTimeout = TimeSpan.FromMilliseconds(500),
            FactoryHardTimeout = TimeSpan.FromSeconds(3),
        };
}
