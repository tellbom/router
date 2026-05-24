using Microsoft.Extensions.Logging;
using System.Text.Json;
using Rbac.Application.Outbox;
using Rbac.Infrastructure.Casbin;
using Rbac.Infrastructure.DM.Outbox;

namespace Rbac.Worker.Outbox;

/// <summary>
/// Casbin policy 刷新 Outbox 处理器。
///
/// 消费 PolicyChanged / GroupChanged 事件后，触发对应 project 的 Casbin Enforcer reload。
/// reload 流程由 <see cref="CasbinPolicyVersionWatcher.ReloadAsync"/> 完成：
/// - 从 MySQL 真相表重新加载 g / p policy。
/// - 新 Enforcer 加载成功后原子替换当前引用。
/// - 失败时保留旧 Enforcer，不阻塞实时鉴权。
///
/// Casbin 处理器读取 payload 中的 project、policyVersion、userid、groupCode、permissionCode、action。
/// 缺失必须字段时进入 Failed，不自行推断。
/// </summary>
public sealed class RbacCasbinOutboxProcessor
{
    private readonly CasbinPolicyVersionWatcher _casbinWatcher;
    private readonly ILogger<RbacCasbinOutboxProcessor> _logger;

    public RbacCasbinOutboxProcessor(
        CasbinPolicyVersionWatcher casbinWatcher,
        ILogger<RbacCasbinOutboxProcessor> logger)
    {
        _casbinWatcher = casbinWatcher;
        _logger = logger;
    }

    public async Task ProcessAsync(OutboxEventEntity entity, CancellationToken ct = default)
    {
        // 只有 PolicyChanged 和 GroupChanged 需要触发 Casbin reload
        if (entity.EventType != RbacOutboxEventTypes.PolicyChanged
            && entity.EventType != RbacOutboxEventTypes.GroupChanged)
            return;

        _logger.LogDebug(
            "CasbinProcessor event={EventId} type={Type} project={P}",
            entity.EventId, entity.EventType, entity.Project);

        if (string.IsNullOrWhiteSpace(entity.Project))
        {
            _logger.LogError(
                "CasbinProcessor: missing project field eventId={Id}", entity.EventId);
            throw new InvalidOperationException("Missing required field: project");
        }

        // 验证 payload 包含必须字段（policyVersion）
        if (entity.EventType == RbacOutboxEventTypes.PolicyChanged)
        {
            var payload = Deserialize<PolicyChangedPayload>(entity.Payload);
            if (payload is null)
                throw new InvalidOperationException("PolicyChanged payload deserialization failed.");

            _logger.LogInformation(
                "Casbin reload triggered project={P} policyVersion={V}",
                entity.Project, payload.PolicyVersion);
        }

        // 后台触发 reload（从 MySQL 真相表重新加载）
        // ReloadAsync 内部处理失败：保留旧 Enforcer 并记录审计日志
        await _casbinWatcher.ReloadAsync(entity.Project, ct);

        _logger.LogInformation(
            "CasbinProcessor completed event={EventId} project={P}", entity.EventId, entity.Project);
    }

    private T? Deserialize<T>(string? payload) where T : class
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try { return JsonSerializer.Deserialize<T>(payload); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payload deserialize failed type={T}", typeof(T).Name);
            return null;
        }
    }
}
