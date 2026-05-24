using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using Rbac.Application.Snapshots;

namespace Rbac.Infrastructure.Redis;

/// <summary>
/// permset 高频判断与写入。
///
/// SISMEMBER 判断必须直接使用 StackExchange.Redis IDatabase，
/// 禁止经过 FusionCache 对象缓存层包装（设计约束，见 ADR-001 决策3）。
///
/// 写入时执行 version compare-before-write，版本冲突时丢弃结果。
/// </summary>
public sealed class RbacPermsetStore : IRbacPermsetBuilder
{
    private readonly IDatabase _db;
    private readonly ILogger<RbacPermsetStore> _logger;

    public RbacPermsetStore(IDatabase db, ILogger<RbacPermsetStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── 高频读取：直接 SISMEMBER ──────────────────────────────────

    /// <summary>
    /// 判断用户是否拥有指定 permissionCode:action。
    /// O(1) Redis Set SISMEMBER，热路径直接调用，不经过 FusionCache。
    /// </summary>
    public async Task<bool> IsMemberAsync(
        string project,
        string userid,
        string permissionCode,
        string action,
        CancellationToken ct = default)
    {
        var key = RbacRedisKeys.Permset(project, userid);
        var member = $"{permissionCode}:{action}";

        try
        {
            return await _db.SetContainsAsync(key, member);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Redis SISMEMBER failed key={Key} member={Member}", key, member);
            // 热路径异常视为 miss，由上层走 Casbin 兜底
            return false;
        }
    }

    /// <summary>
    /// 判断 permset key 是否存在（用于区分"key 不存在"和"权限不存在"）。
    /// </summary>
    public async Task<bool> ExistsAsync(string project, string userid, CancellationToken ct = default)
    {
        var key = RbacRedisKeys.Permset(project, userid);
        return await _db.KeyExistsAsync(key);
    }

    // ── IRbacPermsetBuilder 实现：compare-before-write ────────────

    /// <inheritdoc/>
    public async Task<bool> BuildAndWriteAsync(PermsetBuildInput input, CancellationToken ct = default)
    {
        if (input.Source != PermsetInputSource.DMCasbinDerived)
        {
            _logger.LogError(
                "Rejected permset write with illegal source={Source} userid={Userid} project={Project}",
                input.Source, input.Userid, input.Project);
            return false;
        }

        var versionKey = RbacRedisKeys.VersionUser(input.Project, input.Userid);
        var permsetKey = RbacRedisKeys.Permset(input.Project, input.Userid);

        // compare-before-write：写入前再次读取版本
        var currentVersion = (long?)await _db.StringGetAsync(versionKey) ?? 0L;

        if (currentVersion != input.VersionAtBuildTime)
        {
            _logger.LogWarning(
                "Permset write discarded due to version conflict. " +
                "userid={Userid} project={Project} buildVersion={Build} currentVersion={Current}",
                input.Userid, input.Project, input.VersionAtBuildTime, currentVersion);
            return false;
        }

        // 版本一致：原子写入 permset（先删后 SADD，保证 Set 内容精确）
        var tx = _db.CreateTransaction();
        _ = tx.KeyDeleteAsync(permsetKey);

        if (input.Members.Count > 0)
        {
            var members = input.Members.Select(m => (RedisValue)m).ToArray();
            _ = tx.SetAddAsync(permsetKey, members);
        }

        _ = tx.KeyExpireAsync(permsetKey, TimeSpan.FromMinutes(45));
        var committed = await tx.ExecuteAsync();

        if (!committed)
        {
            _logger.LogWarning(
                "Permset transaction failed userid={Userid} project={Project}",
                input.Userid, input.Project);
            return false;
        }

        _logger.LogDebug(
            "Permset written userid={Userid} project={Project} memberCount={Count}",
            input.Userid, input.Project, input.Members.Count);
        return true;
    }

    /// <summary>删除 permset（权限收回高风险场景主动清除）。</summary>
    public Task DeleteAsync(string project, string userid, CancellationToken ct = default) =>
        _db.KeyDeleteAsync(RbacRedisKeys.Permset(project, userid));
}
