using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using Rbac.Application.Cache;

namespace Rbac.Infrastructure.Redis;

/// <summary>
/// Redis Pub/Sub 订阅端。每个 API 实例启动时注册，监听 rbac.cache.invalidate 频道。
///
/// 收到事件后驱逐对应 FusionCache L1 缓存（由 RbacFusionCacheEvictionHandler 执行）。
/// 事件丢失时依赖短 L1 TTL + version 校验兜底，保证最终一致。
/// </summary>
public sealed class RbacCacheInvalidationSubscriber : IHostedService
{
    private readonly ISubscriber _subscriber;
    private readonly RbacFusionCacheEvictionHandler _evictionHandler;
    private readonly ILogger<RbacCacheInvalidationSubscriber> _logger;

    public RbacCacheInvalidationSubscriber(
        ISubscriber subscriber,
        RbacFusionCacheEvictionHandler evictionHandler,
        ILogger<RbacCacheInvalidationSubscriber> logger)
    {
        _subscriber = subscriber;
        _evictionHandler = evictionHandler;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _subscriber.SubscribeAsync(
            RbacRedisKeys.CacheInvalidateChannel,
            OnMessage);

        _logger.LogInformation(
            "Subscribed to Redis channel: {Ch}", RbacRedisKeys.CacheInvalidateChannel);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _subscriber.UnsubscribeAsync(RbacRedisKeys.CacheInvalidateChannel);
    }

    private void OnMessage(RedisChannel channel, RedisValue message)
    {
        try
        {
            var evt = JsonSerializer.Deserialize<RbacCacheInvalidationEvent>(message.ToString());
            if (evt is null) return;

            // 驱逐 L1 缓存（同步执行，不阻塞 Pub/Sub 线程）
            _evictionHandler.Evict(evt);
        }
        catch (Exception ex)
        {
            // 订阅端异常写日志，不阻塞主请求
            _logger.LogWarning(ex,
                "Cache invalidation subscriber error channel={Ch}", channel);
        }
    }
}
