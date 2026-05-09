using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;
using Rbac.Application.Cache;
using Rbac.Application.Contracts.Menus;
using Rbac.Application.Snapshots;

namespace Rbac.Infrastructure.Redis;

/// <summary>
/// FusionCache 統一訪問門面，同時實現 Application 層的 IMenuTreeCache。
/// 使用 net6 兼容的 Func&lt;CancellationToken, Task&lt;T?&gt;&gt; factory 簽名。
/// </summary>
public sealed class RbacFusionCacheFacade : IMenuTreeCache
{
    private readonly IFusionCache _cache;
    private readonly ILogger<RbacFusionCacheFacade> _logger;

    private static readonly TimeSpan SnapshotL1Ttl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MenuTreeL1Ttl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ApiMapL1Ttl = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan UserProjectsL1Ttl = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan SnapshotL2Ttl = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan MenuTreeL2Ttl = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan ApiMapL2Ttl = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan UserProjectsL2Ttl = TimeSpan.FromMinutes(20);

    public RbacFusionCacheFacade(IFusionCache cache, ILogger<RbacFusionCacheFacade> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    // ── IMenuTreeCache 實現 ───────────────────────────────────────

    public async Task<IReadOnlyList<MenuNodeDto>?> GetMenuTreeAsync(
        string project,
        Func<CancellationToken, Task<IReadOnlyList<MenuNodeDto>?>> factory,
        CancellationToken ct = default)
    {
        return await _cache.GetOrSetAsync<IReadOnlyList<MenuNodeDto>?>(
            RbacRedisKeys.MenuTree(project),
            factory,
            BuildOptions(MenuTreeL1Ttl, MenuTreeL2Ttl, failSafe: true),
            ct);
    }

    public Task EvictMenuTreeAsync(string project) =>
        _cache.RemoveAsync(RbacRedisKeys.MenuTree(project));

    // ── 快照 ─────────────────────────────────────────────────────

    public async Task<UserPermissionSnapshot?> GetSnapshotAsync(
        string project, string userid,
        Func<CancellationToken, Task<UserPermissionSnapshot?>>? factory = null,
        CancellationToken ct = default)
    {
        return await _cache.GetOrSetAsync<UserPermissionSnapshot?>(
            RbacRedisKeys.Snapshot(project, userid),
            factory ?? ((_) => Task.FromResult<UserPermissionSnapshot?>(null)),
            BuildOptions(SnapshotL1Ttl, SnapshotL2Ttl, failSafe: true),
            ct);
    }

    public Task EvictSnapshotAsync(string project, string userid) =>
        _cache.RemoveAsync(RbacRedisKeys.Snapshot(project, userid));

    // ── API Map ───────────────────────────────────────────────────

    public async Task<T?> GetApiMapAsync<T>(
        string project,
        Func<CancellationToken, Task<T?>> factory,
        CancellationToken ct = default) where T : class
    {
        return await _cache.GetOrSetAsync<T?>(
            RbacRedisKeys.ApiMap(project),
            factory,
            BuildOptions(ApiMapL1Ttl, ApiMapL2Ttl, failSafe: true),
            ct);
    }

    public Task EvictApiMapAsync(string project) =>
        _cache.RemoveAsync(RbacRedisKeys.ApiMap(project));

    // ── User Projects ─────────────────────────────────────────────

    public async Task<T?> GetUserProjectsAsync<T>(
        string userid,
        Func<CancellationToken, Task<T?>> factory,
        CancellationToken ct = default) where T : class
    {
        return await _cache.GetOrSetAsync<T?>(
            RbacRedisKeys.UserProjects(userid),
            factory,
            BuildOptions(UserProjectsL1Ttl, UserProjectsL2Ttl, failSafe: false),
            ct);
    }

    public Task EvictUserProjectsAsync(string userid) =>
        _cache.RemoveAsync(RbacRedisKeys.UserProjects(userid));

    // ── 私有 ──────────────────────────────────────────────────────

    private static FusionCacheEntryOptions BuildOptions(TimeSpan l1Ttl, TimeSpan l2Ttl, bool failSafe) =>
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
