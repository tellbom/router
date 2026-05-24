using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using ZiggyCreatures.Caching.Fusion;
using Rbac.Application.Security;
using Rbac.Application.Repositories;

namespace Rbac.Infrastructure.Redis;

/// <summary>
/// <see cref="IProjectGrantStore"/> 的缓存实现。
/// 读取顺序：FusionCache L1 → Redis rbac:user-projects:{userid} → DM（通过回调）。
///
/// 约束：
/// - FusionCache 包装 project 授权关系对象读取（中等粒度，适合 L1 缓存）。
/// - 高频 SISMEMBER 由 RbacPermsetStore 直接使用 StackExchange.Redis，本类不涉及 permset。
/// - TTL: L1 30-120s，L2 Redis 10-30min。
/// </summary>
public sealed class RbacProjectGrantCache : IProjectGrantStore
{
    private readonly IFusionCache _fusionCache;
    private readonly IDatabase _redisDb;
    private readonly IProjectGrantDMReader _dmReader;
    private readonly ILogger<RbacProjectGrantCache> _logger;

    // FusionCache key 前缀（L1 缓存用，不是 Redis key）
    private const string FcKeyPrefix = "rbac:user-projects:";

    public RbacProjectGrantCache(
        IFusionCache fusionCache,
        IDatabase redisDb,
        IProjectGrantDMReader dmReader,
        ILogger<RbacProjectGrantCache> logger)
    {
        _fusionCache = fusionCache;
        _redisDb = redisDb;
        _dmReader = dmReader;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ProjectGrantInfo?> GetGrantAsync(
        string userid,
        string project,
        CancellationToken ct = default)
    {
        var fcKey = $"{FcKeyPrefix}{userid}";

        // FusionCache 包装：L1 命中直接返回，L2 miss 时回调从 Redis/DM 重建
        var grants = await _fusionCache.GetOrSetAsync<UserProjectGrantMap?>(
            fcKey,
            async (ctx, token) =>
            {
                // L2 Redis miss：尝试从 Redis Set 读取
                var fromRedis = await LoadFromRedisAsync(userid, token);
                if (fromRedis is not null) return fromRedis;

                // Redis miss：从 DM 读取并回写 Redis
                var fromDm = await _dmReader.GetUserGrantsAsync(userid, token);
                if (fromDm is not null)
                    await WriteToRedisAsync(userid, fromDm, token);

                return fromDm;
            },
            new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromSeconds(60),
                DistributedCacheDuration = TimeSpan.FromMinutes(20),
                IsFailSafeEnabled = true,
                FailSafeMaxDuration = TimeSpan.FromMinutes(120),
            },
            ct);

        if (grants is null)
        {
            _logger.LogWarning("No grants found userid={Userid}", userid);
            return null;
        }

        return grants.Projects.TryGetValue(project, out var info) ? info : null;
    }

    // ── Redis 读写 ─────────────────────────────────────────────────

    private async Task<UserProjectGrantMap?> LoadFromRedisAsync(string userid, CancellationToken ct)
    {
        // Redis key: rbac:user-projects:{userid}  类型: Hash
        // field: projectCode  value: JSON(ProjectGrantInfo)
        var hashKey = RbacRedisKeys.UserProjects(userid);
        var entries = await _redisDb.HashGetAllAsync(hashKey);
        if (entries.Length == 0) return null;

        var map = new Dictionary<string, ProjectGrantInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.Value.HasValue)
            {
                var info = JsonSerializer.Deserialize<ProjectGrantInfo>(entry.Value!);
                if (info is not null)
                    map[entry.Name!] = info;
            }
        }
        return new UserProjectGrantMap { Projects = map };
    }

    private async Task WriteToRedisAsync(string userid, UserProjectGrantMap map, CancellationToken ct)
    {
        var hashKey = RbacRedisKeys.UserProjects(userid);
        var entries = map.Projects
            .Select(kv => new HashEntry(kv.Key, JsonSerializer.Serialize(kv.Value)))
            .ToArray();

        if (entries.Length == 0) return;

        var db = _redisDb;
        await db.HashSetAsync(hashKey, entries);
        await db.KeyExpireAsync(hashKey, TimeSpan.FromMinutes(20));
    }
}

// UserProjectGrantMap 定义在 Rbac.Application.Repositories.IProjectGrantDMReader.cs
