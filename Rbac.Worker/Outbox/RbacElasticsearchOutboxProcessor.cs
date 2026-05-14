using Microsoft.Extensions.Logging;
using Nest;
using System.Text.Json;
using Rbac.Application.Outbox;
using Rbac.Application.Repositories;
using Rbac.Application.Serialization;
using Rbac.Domain.ValueObjects;
using Rbac.Infrastructure.Elasticsearch.Documents;
using Rbac.Infrastructure.Elasticsearch.Indexes;
using Rbac.Infrastructure.MySql.Outbox;

namespace Rbac.Worker.Outbox;

/// <summary>
/// ES 澧為噺鍚屾 Outbox 澶勭悊鍣ㄣ€?///
/// 娑堣垂 Outbox 浜嬩欢鍚庯紝浠?MySQL 閲嶆柊璇诲彇鑱氬悎鏍规暟鎹苟鍐欏叆 ES 瀵瑰簲绱㈠紩銆?/// 鍘熷垯锛氫笉鐩存帴淇′换 Outbox payload 浣滀负 ES 鍐欏叆鏁版嵁锛岄渶鍥炶 MySQL 鐪熺浉銆?///
/// 鍒犻櫎鍦烘櫙锛圕hangeKind=Deleted锛夌洿鎺ヤ粠 ES 鍒犻櫎鏂囨。銆?/// 鍐欏叆澶辫触鏃朵笉褰卞搷 MySQL 鐪熺浉锛孫utbox 鏍囪 Failed 鍚庤繘鍏ラ噸璇曢槦鍒椼€?/// </summary>
public sealed class RbacElasticsearchOutboxProcessor
{
    private readonly IElasticClient _esClient;
    private readonly IAdministratorRepository _adminRepo;
    private readonly IGroupRepository _groupRepo;
    private readonly IGroupMemberRepository _groupMemberRepo;
    private readonly IProjectGrantRepository _projectGrantRepo;
    private readonly IRuleRepository _ruleRepo;
    private readonly ILogger<RbacElasticsearchOutboxProcessor> _logger;

    public RbacElasticsearchOutboxProcessor(
        IElasticClient esClient,
        IAdministratorRepository adminRepo,
        IGroupRepository groupRepo,
        IGroupMemberRepository groupMemberRepo,
        IProjectGrantRepository projectGrantRepo,
        IRuleRepository ruleRepo,
        ILogger<RbacElasticsearchOutboxProcessor> logger)
    {
        _esClient = esClient;
        _adminRepo = adminRepo;
        _groupRepo = groupRepo;
        _groupMemberRepo = groupMemberRepo;
        _projectGrantRepo = projectGrantRepo;
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
                // PolicyChanged / ApiMapChanged / ProjectGrantChanged 瑙﹀彂鐢ㄦ埛鏂囨。鏇存柊
                if (entity.Userid is not null)
                    await HandleUserChangedAsync(entity, ct);
                break;
        }
    }

    // 鈹€鈹€ 鐢ㄦ埛鏂囨。鏇存柊 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    private async Task HandleUserChangedAsync(OutboxEventEntity entity, CancellationToken ct)
    {
        var payload = Deserialize<UserChangedPayload>(entity.Payload);
        if (payload is null) return;

        var admin = await _adminRepo.FindByUseridAsync(new UserId(payload.Userid), ct);
        if (admin is null)
        {
            // 鐢ㄦ埛宸插垹闄わ細浠?ES 绉婚櫎
            await DeleteDocumentAsync<UserDocument>(
                RbacUserIndexMapping.IndexName, payload.Userid, ct);
            return;
        }

        var doc = await BuildUserDocumentAsync(admin, ct);
        await IndexDocumentAsync(RbacUserIndexMapping.IndexName, admin.Id.ToString(), doc, ct);
    }

    // 鈹€鈹€ 鏉冮檺缁勬枃妗ｆ洿鏂?鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    private async Task<UserDocument> BuildUserDocumentAsync(Rbac.Domain.Users.RbacAdministrator admin, CancellationToken ct)
    {
        var grants = await _projectGrantRepo.FindByUseridAsync(admin.Userid, ct);
        var projectCodes = grants.Select(g => g.Project.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var superProjects = grants.Where(g => g.IsSuper)
            .Select(g => g.Project.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var groupCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projectCodes)
        {
            var memberships = await _groupMemberRepo.FindByUseridAndProjectAsync(admin.Userid.Value, project, ct);
            foreach (var membership in memberships)
            {
                groupCodes.Add(membership.GroupCode.Value);

                var group = await _groupRepo.FindByGroupCodeAsync(
                    membership.GroupCode, membership.Project, ct);
                if (group is not null)
                    groupNames.Add(group.GroupName);
            }
        }

        return new UserDocument
        {
            Id = admin.Id.ToString(),
            Userid = admin.Userid.Value,
            Username = admin.Username,
            ProjectCodes = projectCodes,
            GroupCodes = groupCodes.ToList(),
            GroupNames = groupNames.ToList(),
            Status = admin.Status.ToString(),
            SuperProjects = superProjects,
            CreatedAt = admin.CreatedAt,
            UpdatedAt = admin.UpdatedAt,
        };
    }

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

    // 鈹€鈹€ 瑙勫垯鏂囨。鏇存柊 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

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
            Project = rule.Project.Value,
            RuleCode = rule.RuleCode.Value,
            PermissionCode = rule.PermissionCode.Value,
            ParentRuleCode = rule.ParentRuleCode?.Value,
            Title = rule.Title,
            Name = rule.Name,
            Path = rule.Path,
            Icon = rule.Icon,
            Type = rule.Type.ToString().ToLowerInvariant(),
            MenuType = rule.MenuType?.ToString().ToLowerInvariant() ?? string.Empty,
            Component = rule.Component,
            Url = rule.Url,
            Extend = rule.Extend,
            Remark = rule.Remark,
            Keepalive = rule.Keepalive.ToString().ToLowerInvariant(),
            Status = rule.Status.ToString(),
            Weigh = rule.Weigh,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt,
        };

        await IndexDocumentAsync(RbacRuleIndexMapping.IndexName, rule.Id.ToString(), doc, ct);
    }

    // 鈹€鈹€ 杈呭姪 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

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
        try { return JsonSerializer.Deserialize<T>(payload, RbacSerializationRules.InternalOptions); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payload deserialize failed type={T}", typeof(T).Name);
            return null;
        }
    }
}
