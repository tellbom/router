using Microsoft.Extensions.Logging;
using System.Text.Json;
using Rbac.Application.Cache;
using Rbac.Application.Outbox;
using Rbac.Infrastructure.MySql.Outbox;

namespace Rbac.Worker.Outbox;

/// <summary>
/// Redis 缓存失效 Outbox 处理器。
///
/// 消费 Outbox 事件后，根据 eventType 执行对应的版本递增和缓存删除操作：
/// - UserChanged         → IncrUserVersion，高风险时 DeleteUserCache
/// - GroupChanged        → IncrGroupVersion + IncrProjectVersion
/// - MenuChanged         → DeleteMenuTree + IncrProjectVersion
/// - PolicyChanged       → IncrPolicyVersion
/// - ProjectGrantChanged → DeleteUserCache（高风险：super/授权变更必须主动删除）
/// - ApiMapChanged       → DeleteApiMap + IncrProjectVersion
///
/// 操作顺序：版本递增 → 主动删除（高风险）→ 发布 Pub/Sub 失效事件。
/// </summary>
public sealed class RbacRedisOutboxProcessor
{
    private readonly IRbacCacheInvalidator _invalidator;
    private readonly ILogger<RbacRedisOutboxProcessor> _logger;

    public RbacRedisOutboxProcessor(
        IRbacCacheInvalidator invalidator,
        ILogger<RbacRedisOutboxProcessor> logger)
    {
        _invalidator = invalidator;
        _logger = logger;
    }

    /// <summary>处理单个 Outbox 事件。</summary>
    public async Task ProcessAsync(OutboxEventEntity entity, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "RedisProcessor event={EventId} type={Type} project={P}",
            entity.EventId, entity.EventType, entity.Project);

        switch (entity.EventType)
        {
            case RbacOutboxEventTypes.UserChanged:
                await HandleUserChangedAsync(entity, ct);
                break;

            case RbacOutboxEventTypes.GroupChanged:
                await HandleGroupChangedAsync(entity, ct);
                break;

            case RbacOutboxEventTypes.MenuChanged:
                await HandleMenuChangedAsync(entity, ct);
                break;

            case RbacOutboxEventTypes.PolicyChanged:
                await HandlePolicyChangedAsync(entity, ct);
                break;

            case RbacOutboxEventTypes.ProjectGrantChanged:
                await HandleProjectGrantChangedAsync(entity, ct);
                break;

            case RbacOutboxEventTypes.ApiMapChanged:
                await HandleApiMapChangedAsync(entity, ct);
                break;

            default:
                _logger.LogWarning(
                    "Unknown eventType={Type} eventId={Id}", entity.EventType, entity.EventId);
                break;
        }
    }

    // ── 各 eventType 处理 ─────────────────────────────────────────

    private async Task HandleUserChangedAsync(OutboxEventEntity entity, CancellationToken ct)
    {
        var payload = Deserialize<UserChangedPayload>(entity);
        if (payload is null) return;

        var newVersion = await _invalidator.IncrUserVersionAsync(entity.Project, payload.Userid, ct);

        // 状态变更（禁用）属于高风险，主动删除用户缓存
        if (payload.ChangedFields.Contains("status"))
        {
            await _invalidator.DeleteUserCacheAsync(entity.Project, payload.Userid, ct);
            _logger.LogInformation(
                "UserDisabled: deleted user cache project={P} userid={U}", entity.Project, payload.Userid);
        }

        await PublishAsync(entity.Project, payload.Userid, null, CacheResourceType.Snapshot, newVersion, ct);
    }

    private async Task HandleGroupChangedAsync(OutboxEventEntity entity, CancellationToken ct)
    {
        var payload = Deserialize<GroupChangedPayload>(entity);
        if (payload is null) return;

        await _invalidator.IncrGroupVersionAsync(entity.Project, payload.GroupCode, ct);
        var newVersion = await _invalidator.IncrProjectVersionAsync(entity.Project, ct);

        await PublishAsync(entity.Project, null, payload.GroupCode, CacheResourceType.Snapshot, newVersion, ct);
    }

    private async Task HandleMenuChangedAsync(OutboxEventEntity entity, CancellationToken ct)
    {
        var payload = Deserialize<MenuChangedPayload>(entity);
        if (payload is null) return;

        await _invalidator.DeleteMenuTreeAsync(entity.Project, ct);
        var newVersion = await _invalidator.IncrProjectVersionAsync(entity.Project, ct);

        await PublishAsync(entity.Project, null, null, CacheResourceType.Menu, newVersion, ct);
    }

    private async Task HandlePolicyChangedAsync(OutboxEventEntity entity, CancellationToken ct)
    {
        var payload = Deserialize<PolicyChangedPayload>(entity);
        if (payload is null) return;

        var newVersion = await _invalidator.IncrPolicyVersionAsync(entity.Project, ct);

        await PublishAsync(entity.Project, null, null, CacheResourceType.Policy, newVersion, ct);
    }

    private async Task HandleProjectGrantChangedAsync(OutboxEventEntity entity, CancellationToken ct)
    {
        var payload = Deserialize<ProjectGrantChangedPayload>(entity);
        if (payload is null) return;

        // 授权变更属于高风险：主动删除用户所有缓存
        await _invalidator.DeleteUserCacheAsync(entity.Project, payload.Userid, ct);
        var newVersion = await _invalidator.IncrUserVersionAsync(entity.Project, payload.Userid, ct);

        _logger.LogInformation(
            "ProjectGrantChanged: deleted user cache project={P} userid={U} grantKind={K}",
            entity.Project, payload.Userid, payload.GrantKind);

        await PublishAsync(entity.Project, payload.Userid, null, CacheResourceType.All, newVersion, ct);
    }

    private async Task HandleApiMapChangedAsync(OutboxEventEntity entity, CancellationToken ct)
    {
        var payload = Deserialize<ApiMapChangedPayload>(entity);
        if (payload is null) return;

        await _invalidator.DeleteApiMapAsync(entity.Project, ct);
        var newVersion = await _invalidator.IncrProjectVersionAsync(entity.Project, ct);

        await PublishAsync(entity.Project, null, null, CacheResourceType.ApiMap, newVersion, ct);
    }

    // ── 辅助 ──────────────────────────────────────────────────────

    private async Task PublishAsync(
        string project, string? userid, string? groupCode,
        string resourceType, long newVersion, CancellationToken ct)
    {
        await _invalidator.PublishInvalidationAsync(new RbacCacheInvalidationEvent
        {
            Project = project,
            Userid = userid,
            GroupCode = groupCode,
            ResourceType = resourceType,
            NewVersion = newVersion,
        }, ct);
    }

    private T? Deserialize<T>(OutboxEventEntity entity) where T : class
    {
        if (string.IsNullOrWhiteSpace(entity.Payload))
        {
            _logger.LogError("Empty payload eventId={Id} type={Type}", entity.EventId, entity.EventType);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(entity.Payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to deserialize payload eventId={Id} type={Type}", entity.EventId, entity.EventType);
            return null;
        }
    }
}
