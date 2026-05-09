using Microsoft.Extensions.Logging;
using Nest;
using System.Text.Json;
using Rbac.Application.Outbox;
using Rbac.Application.Repositories;
using Rbac.Domain.ValueObjects;
using Rbac.Infrastructure.Elasticsearch.Documents;
using Rbac.Infrastructure.Elasticsearch.Indexes;
using Rbac.Infrastructure.MySql.Outbox;

namespace Rbac.Worker.Outbox;

/// <summary>
/// ES 增量同步 Outbox 处理器。
///
/// 消费 Outbox 事件后，从 MySQL 重新读取聚合根数据并写入 ES 对应索引。
/// 原则：不直接信任 Outbox payload 作为 ES 写入数据，需回读 MySQL 真相。
///
/// 删除场景（ChangeKind=Deleted）直接按 DxEId 从 ES 删除文档。
/// 写入失败时不影响 MySQL 真相，Outbox 标记 Failed 后进入重试队列。
/// </summary>
public sealed class RbacElasticsearchOutboxProcessor
{
    private readonly IElasticClient _esClient;
    private readonly IAdministratorRepository _adminRepo;
    private readonly IGroupRepository _groupRepo;
    private readonly IRuleRepository _ruleRepo;
    private readonly ILogger<RbacElasticsearchOutboxProcessor> _logger;

    public RbacElasticsearchOutboxProcessor(
        IElasticClient esClient,
        IAdministratorRepository adminRepo,
        IGroupRepository groupRepo,
        IRuleRepository ruleRepo,
        ILogger<RbacElasticsearchOutboxProcessor> logger)
    {
        _esClient = esClient;
        _adminRepo = adminRepo;
        _groupRepo = groupRepo;
        _ruleRepo = ruleRepo;
        _logger = logger;
    }

    public async Task ProcessAsync(OutboxEventEntity entity, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "ESProcessor event={EventId} type={Type} project={P}",
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
            default:
                // PolicyChanged / ApiMapChanged / ProjectGrantChanged 触发用户文档更新
                if (entity.Userid is not null)
                    await HandleUserChangedAsync(entity, ct);
                break;
        }
    }

    // ── 用户文档更新 ──────────────────────────────────────────────

    private async Task HandleUserChangedAsync(OutboxEventEntity entity, CancellationToken ct)
    {
        var payload = Deserialize<UserChangedPayload>(entity.Payload);
        if (payload is null) return;

        var admin = await _adminRepo.FindByUseridAsync(new UserId(payload.Userid), ct);
        if (admin is null)
        {
            // 用户已删除：从 ES 移除
            await DeleteDocumentAsync<UserDocument>(
                RbacUserIndexMapping.IndexName, payload.Userid, ct);
            return;
        }

        var doc = new UserDocument
        {
            Id = admin.Id.ToString(),
            DxEId = admin.DxEId.Value,    // string，不为 number
            Userid = admin.Userid.Value,
            Username = admin.Username,
            Status = admin.Status.ToString(),
        };

        await IndexDocumentAsync(RbacUserIndexMapping.IndexName, admin.Id.ToString(), doc, ct);
    }

    // ── 权限组文档更新 ────────────────────────────────────────────

    private async Task HandleGroupChangedAsync(OutboxEventEntity entity, CancellationToken ct)
    {
        var payload = Deserialize<GroupChangedPayload>(entity.Payload);
        if (payload is null) return;

        var group = await _groupRepo.FindByGroupCodeAsync(
            new GroupCode(payload.GroupCode), new ProjectCode(entity.Project), ct);

        if (group is null)
        {
            await DeleteDocumentAsync<GroupDocument>(
                RbacGroupIndexMapping.IndexName, payload.GroupCode, ct);
            return;
        }

        var doc = new GroupDocument
        {
            Id = group.Id.ToString(),
            DxEId = group.DxEId.Value,
            Project = group.Project.Value,
            GroupCode = group.GroupCode.Value,
            GroupName = group.GroupName,
            ParentGroupCode = group.ParentGroupCode?.Value,
            RuleCodes = group.RuleCodes.Select(r => r.Value).ToList(),
            PermissionCodes = group.PermissionCodes.Select(p => p.Value).ToList(),
            Status = group.Status.ToString(),
        };

        await IndexDocumentAsync(RbacGroupIndexMapping.IndexName, group.Id.ToString(), doc, ct);
    }

    // ── 规则文档更新 ──────────────────────────────────────────────

    private async Task HandleMenuChangedAsync(OutboxEventEntity entity, CancellationToken ct)
    {
        var payload = Deserialize<MenuChangedPayload>(entity.Payload);
        if (payload is null) return;

        var rule = await _ruleRepo.FindByRuleCodeAsync(
            new RuleCode(payload.RuleCode), new ProjectCode(entity.Project), ct);

        if (rule is null || payload.ChangeKind == "Deleted")
        {
            await DeleteDocumentAsync<RuleDocument>(
                RbacRuleIndexMapping.IndexName, payload.RuleCode, ct);
            return;
        }

        var doc = new RuleDocument
        {
            Id = rule.Id.ToString(),
            DxEId = rule.DxEId.Value,
            Project = rule.Project.Value,
            RuleCode = rule.RuleCode.Value,
            PermissionCode = rule.PermissionCode.Value,
            ParentRuleCode = rule.ParentRuleCode?.Value,
            Title = rule.Title,
            Name = rule.Name,
            Path = rule.Path,
            Type = rule.Type.ToString().ToLowerInvariant(),
            MenuType = rule.MenuType?.ToString().ToLowerInvariant() ?? string.Empty,
            Status = rule.Status.ToString(),
            Weigh = rule.Weigh,
        };

        await IndexDocumentAsync(RbacRuleIndexMapping.IndexName, rule.Id.ToString(), doc, ct);
    }

    // ── 辅助 ──────────────────────────────────────────────────────

    private async Task IndexDocumentAsync<T>(string index, string id, T doc, CancellationToken ct)
        where T : class
    {
        var response = await _esClient.IndexAsync(doc, i => i
            .Index(index)
            .Id(id), ct);

        if (!response.IsValid)
        {
            _logger.LogError(
                "ES index failed index={Index} id={Id} error={Err}",
                index, id, response.ServerError?.Error?.Reason);
            throw new InvalidOperationException($"ES index failed: {response.ServerError?.Error?.Reason}");
        }
    }

    private async Task DeleteDocumentAsync<T>(string index, string id, CancellationToken ct)
        where T : class
    {
        var response = await _esClient.DeleteAsync<T>(id, d => d.Index(index), ct);

        if (!response.IsValid && response.Result != Result.NotFound)
        {
            _logger.LogError(
                "ES delete failed index={Index} id={Id} error={Err}",
                index, id, response.ServerError?.Error?.Reason);
        }
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
