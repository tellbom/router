using Microsoft.Extensions.Logging;
using Rbac.Application.Cache;

namespace Rbac.Infrastructure.Redis;

/// <summary>
/// FusionCache L1 驱逐处理器。
/// 收到 RbacCacheInvalidationEvent 后，按 resourceType 和 userid/groupCode 精确驱逐本地 L1 缓存。
/// </summary>
public sealed class RbacFusionCacheEvictionHandler
{
    private readonly RbacFusionCacheFacade _fusionCache;
    private readonly ILogger<RbacFusionCacheEvictionHandler> _logger;

    public RbacFusionCacheEvictionHandler(
        RbacFusionCacheFacade fusionCache,
        ILogger<RbacFusionCacheEvictionHandler> logger)
    {
        _fusionCache = fusionCache;
        _logger = logger;
    }

    /// <summary>根据失效事件驱逐对应 L1 缓存 key。</summary>
    public void Evict(RbacCacheInvalidationEvent evt)
    {
        _logger.LogDebug(
            "L1 eviction event project={P} userid={U} resourceType={RT}",
            evt.Project, evt.Userid, evt.ResourceType);

        // 用户级失效
        if (!string.IsNullOrEmpty(evt.Userid))
        {
            _ = _fusionCache.EvictSnapshotAsync(evt.Project, evt.Userid);
            _ = _fusionCache.EvictUserProjectsAsync(evt.Userid);
        }

        // 按资源类型驱逐
        switch (evt.ResourceType)
        {
            case CacheResourceType.Menu:
                _ = _fusionCache.EvictMenuTreeAsync(evt.Project);
                break;
            case CacheResourceType.ApiMap:
                _ = _fusionCache.EvictApiMapAsync(evt.Project);
                break;
            case CacheResourceType.All:
                _ = _fusionCache.EvictMenuTreeAsync(evt.Project);
                _ = _fusionCache.EvictApiMapAsync(evt.Project);
                if (!string.IsNullOrEmpty(evt.Userid))
                    _ = _fusionCache.EvictSnapshotAsync(evt.Project, evt.Userid);
                break;
            case CacheResourceType.Snapshot:
                if (!string.IsNullOrEmpty(evt.Userid))
                    _ = _fusionCache.EvictSnapshotAsync(evt.Project, evt.Userid);
                break;
            case CacheResourceType.UserProject:
                if (!string.IsNullOrEmpty(evt.Userid))
                    _ = _fusionCache.EvictUserProjectsAsync(evt.Userid);
                break;
        }
    }
}
