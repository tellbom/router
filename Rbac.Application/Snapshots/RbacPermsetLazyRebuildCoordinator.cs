using Microsoft.Extensions.Logging;
using Rbac.Application.Repositories;
using Rbac.Domain.ValueObjects;
using Rbac.Infrastructure.Redis;
using StackExchange.Redis;

namespace Rbac.Application.Snapshots;

/// <summary>
/// 请求链路 permset 懒重建协调器。
///
/// 在 cache miss 或 version stale 时触发，执行完整的懒重建流程：
/// 1. 读取当前 Redis version（project + user + policy）。
/// 2. 从 MySQL/Casbin 计算候选 permset members。
/// 3. 写入前再次读取 version（compare-before-write）。
/// 4. version 未变化 → 写入 permset。
/// 5. version 已变化 → 丢弃（由下一次请求或 Worker 重新生成）。
///
/// 设计约束：
/// - permset 输入只能来自 MySQL/Casbin 策略（PermsetInputSource.MySqlCasbinDerived）。
/// - 重建不阻塞当前请求的鉴权结果（Casbin 兜底已给出 allow/deny）。
/// - 调用方以 fire-and-forget 方式触发（Task.Run）。
/// </summary>
public sealed class RbacPermsetLazyRebuildCoordinator
{
    private readonly IDatabase _redisDb;
    private readonly IRbacPermsetBuilder _permsetBuilder;
    private readonly ICasbinGroupingPolicyReader _groupingReader;
    private readonly ICasbinPermissionPolicyReader _permissionReader;
    private readonly IAdministratorRepository _adminRepo;
    private readonly ILogger<RbacPermsetLazyRebuildCoordinator> _logger;

    public RbacPermsetLazyRebuildCoordinator(
        IDatabase redisDb,
        IRbacPermsetBuilder permsetBuilder,
        ICasbinGroupingPolicyReader groupingReader,
        ICasbinPermissionPolicyReader permissionReader,
        IAdministratorRepository adminRepo,
        ILogger<RbacPermsetLazyRebuildCoordinator> logger)
    {
        _redisDb = redisDb;
        _permsetBuilder = permsetBuilder;
        _groupingReader = groupingReader;
        _permissionReader = permissionReader;
        _adminRepo = adminRepo;
        _logger = logger;
    }

    /// <summary>
    /// 触发 permset 懒重建。
    /// 应以 fire-and-forget 方式调用（不 await，不阻塞鉴权结果）。
    /// </summary>
    public async Task RebuildAsync(string userid, string project, CancellationToken ct = default)
    {
        try
        {
            await RebuildInternalAsync(userid, project, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Permset lazy rebuild failed userid={U} project={P}", userid, project);
        }
    }

    private async Task RebuildInternalAsync(string userid, string project, CancellationToken ct)
    {
        // 1. 读取构建前 version
        var versionKey = RbacRedisKeys.VersionUser(project, userid);
        var versionAtStart = (long?)await _redisDb.StringGetAsync(versionKey) ?? 0L;

        // 2. 从 MySQL/Casbin 计算候选 permset
        var projectCode = new ProjectCode(project);
        var grouping = await _groupingReader.LoadAsync(projectCode, ct);
        var permission = await _permissionReader.LoadAsync(projectCode, ct);

        // 过滤出当前用户的权限组
        var userGroups = grouping
            .Where(g => string.Equals(g.Userid, userid, StringComparison.OrdinalIgnoreCase))
            .Select(g => g.GroupCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 展开用户权限码
        var members = permission
            .Where(p => userGroups.Contains(p.GroupCode))
            .Select(p => $"{p.PermissionCode}:{p.Action}")
            .Distinct()
            .ToList();

        // 3. compare-before-write：写入前再次读取 version
        var input = new PermsetBuildInput
        {
            Userid = userid,
            Project = project,
            Members = members,
            VersionAtBuildTime = versionAtStart,
            Source = PermsetInputSource.MySqlCasbinDerived,
        };

        var written = await _permsetBuilder.BuildAndWriteAsync(input, ct);

        if (written)
        {
            _logger.LogDebug(
                "Permset lazy rebuild succeeded userid={U} project={P} memberCount={C}",
                userid, project, members.Count);
        }
        else
        {
            _logger.LogDebug(
                "Permset lazy rebuild discarded (version conflict) userid={U} project={P}",
                userid, project);
        }
    }
}
