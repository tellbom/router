using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using Rbac.Application.Cache;

namespace Rbac.Infrastructure.Redis;

/// <summary>
/// <see cref="IRbacCacheInvalidator"/> 实现。
///
/// 三类操作全部直接使用 StackExchange.Redis，不经过 FusionCache：
/// 1. 版本 INCR（原子递增，返回新版本号）。
/// 2. 主动 DEL（高风险场景精确删除）。
/// 3. Pub/Sub PUBLISH（通知各实例驱逐 L1 缓存）。
///
/// 缓存失效不扫描 10W 用户 key：
/// - 权限组变更 → 递增 group version，用户请求时懒失效。
/// - 高风险场景（super 变更/用户禁用）→ 主动 DEL 该用户的 snapshot + permset。
/// </summary>
public sealed class RbacCacheInvalidator : IRbacCacheInvalidator
{
    private readonly IDatabase _db;
    private readonly ISubscriber _subscriber;
    private readonly ILogger<RbacCacheInvalidator> _logger;

    public RbacCacheInvalidator(
        IDatabase db,
        ISubscriber subscriber,
        ILogger<RbacCacheInvalidator> logger)
    {
        _db = db;
        _subscriber = subscriber;
        _logger = logger;
    }

    // ── 版本递增 ──────────────────────────────────────────────────

    public async Task<long> IncrProjectVersionAsync(string project, CancellationToken ct = default)
    {
        var key = RbacRedisKeys.VersionProject(project);
        var newVersion = await _db.StringIncrementAsync(key);
        _logger.LogDebug("IncrProjectVersion project={P} newVersion={V}", project, newVersion);
        return newVersion;
    }

    public async Task<long> IncrUserVersionAsync(string project, string userid, CancellationToken ct = default)
    {
        var key = RbacRedisKeys.VersionUser(project, userid);
        var newVersion = await _db.StringIncrementAsync(key);
        _logger.LogDebug("IncrUserVersion project={P} userid={U} newVersion={V}", project, userid, newVersion);
        return newVersion;
    }

    public async Task<long> IncrGroupVersionAsync(string project, string groupCode, CancellationToken ct = default)
    {
        var key = RbacRedisKeys.VersionGroup(project, groupCode);
        var newVersion = await _db.StringIncrementAsync(key);
        _logger.LogDebug("IncrGroupVersion project={P} groupCode={G} newVersion={V}", project, groupCode, newVersion);
        return newVersion;
    }

    public async Task<long> IncrPolicyVersionAsync(string project, CancellationToken ct = default)
    {
        var key = RbacRedisKeys.PolicyVersion(project);
        var newVersion = await _db.StringIncrementAsync(key);
        _logger.LogDebug("IncrPolicyVersion project={P} newVersion={V}", project, newVersion);
        return newVersion;
    }

    // ── 主动删除（高风险场景）────────────────────────────────────

    public async Task DeleteUserCacheAsync(string project, string userid, CancellationToken ct = default)
    {
        var snapshotKey    = RbacRedisKeys.Snapshot(project, userid);
        var permsetKey     = RbacRedisKeys.Permset(project, userid);
        var menusKey       = RbacRedisKeys.Menus(project, userid);
        // rbac:user-projects:{userid} 存储 ProjectGrantInfo（含 IsSuper），
        // super 升降权后必须同步删除，否则下次 L1 miss 时从 Redis Hash 回填旧值，
        // 导致升权后最长 20 分钟内 IsSuper 仍为 false（或降权后仍为 true）。
        var userProjectsKey = RbacRedisKeys.UserProjects(userid);

        await _db.KeyDeleteAsync(new RedisKey[]
        {
            snapshotKey,
            permsetKey,
            menusKey,
            userProjectsKey,
        });

        _logger.LogInformation(
            "DeleteUserCache project={P} userid={U} keys=[snapshot,permset,menus,user-projects]",
            project, userid);
    }

    public async Task DeleteMenuTreeAsync(string project, CancellationToken ct = default)
    {
        var key = RbacRedisKeys.MenuTree(project);
        await _db.KeyDeleteAsync(key);
        _logger.LogDebug("DeleteMenuTree project={P}", project);
    }

    public async Task DeleteApiMapAsync(string project, CancellationToken ct = default)
    {
        var key = RbacRedisKeys.ApiMap(project);
        await _db.KeyDeleteAsync(key);
        _logger.LogDebug("DeleteApiMap project={P}", project);
    }

    // ── Pub/Sub 发布 ──────────────────────────────────────────────

    public async Task PublishInvalidationAsync(
        RbacCacheInvalidationEvent evt, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(evt);

        try
        {
            await _subscriber.PublishAsync(
                RbacRedisKeys.CacheInvalidateChannel, payload);

            _logger.LogDebug(
                "Published invalidation event eventId={Id} project={P} resourceType={RT}",
                evt.EventId, evt.Project, evt.ResourceType);
        }
        catch (Exception ex)
        {
            // Pub/Sub 失败不阻塞主流程；L1 TTL 兜底
            _logger.LogWarning(ex,
                "Failed to publish cache invalidation event eventId={Id}", evt.EventId);
        }
    }
}
