using Microsoft.Extensions.Logging;
using Rbac.Application.Cache;
using Rbac.Application.Snapshots;

namespace Rbac.Worker.Cache;

/// <summary>
/// Worker 触发的 permset 失效与热点预热 Worker。
///
/// 在权限变更的 Outbox 处理完成后（版本已递增、Redis key 已删除）触发：
/// 1. 对高风险用户（super 变更、project 授权移除）的 permset 已由
///    RbacRedisOutboxProcessor 主动删除，此处无需重复。
/// 2. 对热点用户（最近活跃管理员）可选预热 permset。
/// </summary>
public sealed class RbacPermsetInvalidationWorker
{
    private readonly IRbacCacheInvalidator _invalidator;
    private readonly RbacPermsetLazyRebuildCoordinator _rebuilder;
    private readonly ILogger<RbacPermsetInvalidationWorker> _logger;

    public RbacPermsetInvalidationWorker(
        IRbacCacheInvalidator invalidator,
        RbacPermsetLazyRebuildCoordinator rebuilder,
        ILogger<RbacPermsetInvalidationWorker> logger)
    {
        _invalidator = invalidator;
        _rebuilder = rebuilder;
        _logger = logger;
    }

    /// <summary>
    /// 触发 project 下指定用户集合的 permset 预热（可选，仅对热点用户）。
    /// </summary>
    public async Task PrewarmAsync(
        string project,
        IReadOnlyList<string> hotUserids,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Permset prewarm started project={P} userCount={C}", project, hotUserids.Count);

        foreach (var userid in hotUserids)
        {
            if (ct.IsCancellationRequested) break;

            // fire-and-forget 重建，不阻塞批量预热循环
            _ = _rebuilder.RebuildAsync(userid, project, ct);
        }
    }

    /// <summary>
    /// 主动失效权限组下所有受影响用户的 permset（版本递增已由 Redis 处理器完成）。
    /// 此方法为版本懒失效的补充，对在线高风险用户做小批量主动删除。
    /// </summary>
    public async Task InvalidateGroupUsersAsync(
        string project, IReadOnlyList<string> affectedUserids, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "InvalidateGroupUsers project={P} count={C}", project, affectedUserids.Count);

        foreach (var userid in affectedUserids)
        {
            if (ct.IsCancellationRequested) break;
            await _invalidator.DeleteUserCacheAsync(project, userid, ct);
        }
    }
}
