using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Rbac.Application.Cache;
using Rbac.Application.Policies;
using Rbac.Application.Repositories;
using Rbac.Application.Security;
using Rbac.Application.Snapshots;
using Rbac.Domain.ValueObjects;

namespace Rbac.Infrastructure.Redis;

/// <summary>
/// PATCH-05: IRbacSnapshotService 的实现。
///
/// 读取顺序：FusionCache L1 → Redis STRING GET → DM/Casbin 重建。
/// 快照存储在 rbac:snapshot:{project}:{userid}，JSON 序列化，TTL 30 min。
///
/// 重建约束（设计文档 ADR-001 §5.3）：
/// - 重建开始时读取 versionAtStart。
/// - 构建快照后再次读取 Redis 版本。
/// - 若 versionNow != versionAtStart → 版本冲突，丢弃本次结果（不写入）。
/// - 只有版本一致才写 Redis 和 FusionCache L1。
///
/// permset 同步（PATCH-05 范围）：
/// - RebuildSnapshotAsync 同时通过 IRbacPermsetBuilder.BuildAndWriteAsync 更新 permset。
/// - permset 写入同样执行 compare-before-write，由 RbacPermsetStore 实现。
/// </summary>
public sealed class RbacSnapshotService : IRbacSnapshotService
{
    private readonly IDatabase _redisDb;
    private readonly RbacFusionCacheFacade _fusionCache;
    private readonly IVersionStore _versionStore;
    private readonly ICasbinGroupingPolicyReader _groupingReader;
    private readonly ICasbinPermissionPolicyReader _permissionReader;
    private readonly IProjectGrantStore _grantStore;
    private readonly IRbacPermsetBuilder _permsetBuilder;
    private readonly ILogger<RbacSnapshotService> _logger;

    private static readonly TimeSpan SnapshotRedisTtl = TimeSpan.FromMinutes(30);

    // 内部序列化选项：保留全部字段，long → string（含 SnapshotVersions 内的 long）
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public RbacSnapshotService(
        IDatabase redisDb,
        RbacFusionCacheFacade fusionCache,
        IVersionStore versionStore,
        ICasbinGroupingPolicyReader groupingReader,
        ICasbinPermissionPolicyReader permissionReader,
        IProjectGrantStore grantStore,
        IRbacPermsetBuilder permsetBuilder,
        ILogger<RbacSnapshotService> logger)
    {
        _redisDb        = redisDb;
        _fusionCache    = fusionCache;
        _versionStore   = versionStore;
        _groupingReader = groupingReader;
        _permissionReader = permissionReader;
        _grantStore     = grantStore;
        _permsetBuilder = permsetBuilder;
        _logger         = logger;
    }

    // ── GetSnapshotAsync ──────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<UserPermissionSnapshot?> GetSnapshotAsync(
        string userid, string project, CancellationToken ct = default)
    {
        // 1. FusionCache L1
        var snapshot = await _fusionCache.GetSnapshotAsync(
            project, userid,
            factory: null,   // 不让 FusionCache 自动回源，手动控制
            ct: ct);

        if (snapshot is not null)
        {
            if (await IsSnapshotFreshAsync(snapshot))
            {
                _logger.LogDebug("Snapshot L1 hit userid={U} project={P}", userid, project);
                return snapshot;
            }

            await _fusionCache.EvictSnapshotAsync(project, userid);
            _logger.LogDebug("Snapshot L1 stale userid={U} project={P}", userid, project);
        }

        // 2. Redis L2（直接 GET，JSON 反序列化）
        snapshot = await ReadFromRedisAsync(project, userid);
        if (snapshot is not null)
        {
            if (!await IsSnapshotFreshAsync(snapshot))
            {
                await _redisDb.KeyDeleteAsync(RbacRedisKeys.Snapshot(project, userid));
                _logger.LogDebug("Snapshot Redis stale userid={U} project={P}", userid, project);
                return await RebuildSnapshotAsync(userid, project, ct);
            }

            _logger.LogDebug("Snapshot Redis hit userid={U} project={P}", userid, project);
            // 回填 L1
            await _fusionCache.GetSnapshotAsync(project, userid,
                factory: _ => Task.FromResult<UserPermissionSnapshot?>(snapshot), ct);
            return snapshot;
        }

        // 3. 未命中 → 重建
        _logger.LogDebug("Snapshot miss, rebuilding userid={U} project={P}", userid, project);
        return await RebuildSnapshotAsync(userid, project, ct);
    }

    // ── RebuildSnapshotAsync ──────────────────────────────────────

    /// <inheritdoc/>
    public async Task<UserPermissionSnapshot?> RebuildSnapshotAsync(
        string userid, string project, CancellationToken ct = default)
    {
        // 重建开始前记录版本
        var userVersionAtStart = await _versionStore.ReadUserVersionAsync(project, userid);
        var projectVersionAtStart = await _versionStore.ReadProjectVersionAsync(project);
        var policyVersionAtStart = await _versionStore.ReadPolicyVersionAsync(project);
        var projectCode    = new ProjectCode(project);

        // 读取 project 授权（isSuper / policyVersion）
        var grant = await _grantStore.GetGrantAsync(userid, project, ct);
        if (grant is null)
        {
            _logger.LogDebug("No grant for userid={U} project={P}, skip rebuild", userid, project);
            return null;
        }

        // 从 DM 读取 g policy（用户-组关系）和 p policy（组-权限码）
        var groupings   = await _groupingReader.LoadAsync(projectCode, ct);
        var permissions = await _permissionReader.LoadAsync(projectCode, ct);

        var userGroupCodes = groupings
            .Where(g => string.Equals(g.Userid, userid, StringComparison.OrdinalIgnoreCase))
            .Select(g => g.GroupCode)
            .ToList();

        var permCodes = permissions
            .Where(p => userGroupCodes.Contains(p.GroupCode))
            .Select(p => p.PermissionCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 版本 compare-before-write
        var versionNow = await _versionStore.ReadUserVersionAsync(project, userid);
        var projectVersionNow = await _versionStore.ReadProjectVersionAsync(project);
        var policyVersionNow = await _versionStore.ReadPolicyVersionAsync(project);
        if (versionNow != userVersionAtStart
            || projectVersionNow != projectVersionAtStart
            || policyVersionNow != policyVersionAtStart)
        {
            _logger.LogWarning(
                "Snapshot rebuild discarded (version conflict) userid={U} project={P} " +
                "userStart={US} userNow={UN} projectStart={PS} projectNow={PN} policyStart={POL} policyNow={PON}",
                userid, project,
                userVersionAtStart, versionNow,
                projectVersionAtStart, projectVersionNow,
                policyVersionAtStart, policyVersionNow);
            return null;
        }

        var snapshot = new UserPermissionSnapshot
        {
            Userid = userid,
            Project = project,
            Groups = userGroupCodes,
            Super = grant.IsSuper,
            PermissionCodes = permCodes,
            RuleCodes = Array.Empty<string>(), // RuleCode 在 permset 热路径不需要，按需扩展
            Versions = new SnapshotVersions
            {
                User    = versionNow,
                Project = projectVersionNow,
                Policy  = policyVersionNow,
            },
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // 写 Redis（STRING SET with TTL）
        await WriteToRedisAsync(project, userid, snapshot);

        // 回填 FusionCache L1
        await _fusionCache.GetSnapshotAsync(project, userid,
            factory: _ => Task.FromResult<UserPermissionSnapshot?>(snapshot), ct);

        // 同步更新 permset（compare-before-write 在 RbacPermsetStore 内部保证）
        var permsetMembers = permissions
            .Where(p => userGroupCodes.Contains(p.GroupCode))
            .Select(p => $"{p.PermissionCode}:{p.Action}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await _permsetBuilder.BuildAndWriteAsync(new PermsetBuildInput
        {
            Userid            = userid,
            Project           = project,
            Members           = permsetMembers,
            VersionAtBuildTime = versionNow,
            Source            = PermsetInputSource.DMCasbinDerived,
        }, ct);

        _logger.LogDebug(
            "Snapshot rebuilt userid={U} project={P} permCodes={N} permsetMembers={M}",
            userid, project, permCodes.Count, permsetMembers.Count);

        return snapshot;
    }

    // ── InvalidateAsync ───────────────────────────────────────────

    /// <inheritdoc/>
    public async Task InvalidateAsync(string userid, string project, CancellationToken ct = default)
    {
        // 删除 FusionCache L1
        await _fusionCache.EvictSnapshotAsync(project, userid);

        // 删除 Redis
        var key = RbacRedisKeys.Snapshot(project, userid);
        await _redisDb.KeyDeleteAsync(key);

        _logger.LogDebug("Snapshot invalidated userid={U} project={P}", userid, project);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private async Task<UserPermissionSnapshot?> ReadFromRedisAsync(string project, string userid)
    {
        try
        {
            var key = RbacRedisKeys.Snapshot(project, userid);
            var raw = await _redisDb.StringGetAsync(key);
            if (raw.IsNullOrEmpty) return null;

            return JsonSerializer.Deserialize<UserPermissionSnapshot>(raw!, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Snapshot Redis read failed userid={U} project={P}", userid, project);
            return null;
        }
    }

    private async Task WriteToRedisAsync(string project, string userid, UserPermissionSnapshot snapshot)
    {
        try
        {
            var key  = RbacRedisKeys.Snapshot(project, userid);
            var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
            await _redisDb.StringSetAsync(key, json, SnapshotRedisTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Snapshot Redis write failed userid={U} project={P}", userid, project);
            // 写失败不抛出，调用方仍可拿到内存中的快照
        }
    }

    private async Task<bool> IsSnapshotFreshAsync(UserPermissionSnapshot snapshot)
    {
        var projectVersion = await _versionStore.ReadProjectVersionAsync(snapshot.Project);
        if (snapshot.Versions.Project < projectVersion)
            return false;

        var userVersion = await _versionStore.ReadUserVersionAsync(snapshot.Project, snapshot.Userid);
        if (snapshot.Versions.User < userVersion)
            return false;

        var policyVersion = await _versionStore.ReadPolicyVersionAsync(snapshot.Project);
        return snapshot.Versions.Policy >= policyVersion;
    }
}
