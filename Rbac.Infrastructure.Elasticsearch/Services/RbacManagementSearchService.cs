using Microsoft.Extensions.Logging;
using Nest;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Search;
using Rbac.Infrastructure.Elasticsearch.Documents;
using Rbac.Infrastructure.Elasticsearch.Indexes;
using Rbac.Infrastructure.Elasticsearch.Search;

namespace Rbac.Infrastructure.Elasticsearch.Services;

/// <summary>
/// IRbacManagementSearchService 的 NEST 實現。
/// 位於 Infrastructure.Elasticsearch，不在 Application 層。
/// </summary>
public sealed class RbacManagementSearchService : IRbacManagementSearchService
{
    private readonly IElasticClient _esClient;
    private readonly ILogger<RbacManagementSearchService> _logger;

    public RbacManagementSearchService(IElasticClient esClient, ILogger<RbacManagementSearchService> logger)
    {
        _esClient = esClient;
        _logger = logger;
    }

    public async Task<PagedData<UserSearchResult>> SearchUsersAsync(UserSearchQuery query, CancellationToken ct = default)
    {
        var descriptor = RbacElasticQueryBuilder.BuildUserSearch(query);
        var response = await _esClient.SearchAsync<UserDocument>(d => descriptor, ct);
        return Map(response, query, RbacUserIndexMapping.IndexName,
            doc =>
            {
                var superProjects = doc.SuperProjects ?? Array.Empty<string>();
                return new UserSearchResult
                {
                    Userid = doc.Userid,
                    Username = doc.Username,
                    Status = doc.Status,
                    ProjectCodes = doc.ProjectCodes,
                    GroupCodes = doc.GroupCodes,
                    GroupNames = doc.GroupNames,
                    SuperProjects = superProjects,
                    IsSuper = !string.IsNullOrWhiteSpace(query.Project)
                        && superProjects.Contains(query.Project, StringComparer.OrdinalIgnoreCase),
                };
            });
    }

    public async Task<PagedData<GroupSearchResult>> SearchGroupsAsync(GroupSearchQuery query, CancellationToken ct = default)
    {
        var descriptor = RbacElasticQueryBuilder.BuildGroupSearch(query);
        var response = await _esClient.SearchAsync<GroupDocument>(d => descriptor, ct);
        return Map(response, query, RbacGroupIndexMapping.IndexName,
            doc => new GroupSearchResult
            {
                GroupCode = doc.GroupCode,
                GroupName = doc.GroupName,
                Project = doc.Project,
                ParentGroupCode = doc.ParentGroupCode,
                Status = doc.Status,
                PermissionCodes = doc.PermissionCodes,
            });
    }

    public async Task<PagedData<RuleSearchResult>> SearchRulesAsync(RuleSearchQuery query, CancellationToken ct = default)
    {
        var descriptor = RbacElasticQueryBuilder.BuildRuleSearch(query);
        var response = await _esClient.SearchAsync<RuleDocument>(d => descriptor, ct);
        return Map(response, query, RbacRuleIndexMapping.IndexName,
            doc => new RuleSearchResult
            {
                Id = doc.Id,
                Project = doc.Project,
                RuleCode = doc.RuleCode,
                PermissionCode = doc.PermissionCode,
                ParentRuleCode = doc.ParentRuleCode,
                Title = doc.Title,
                Name = doc.Name,
                Path = doc.Path,
                Icon = doc.Icon ?? string.Empty,
                Type = doc.Type,
                MenuType = doc.MenuType,
                Component = doc.Component ?? string.Empty,
                Url = doc.Url ?? string.Empty,
                Extend = doc.Extend ?? string.Empty,
                Remark = doc.Remark ?? string.Empty,
                Keepalive = bool.TryParse(doc.Keepalive, out var keepalive) && keepalive,
                Status = doc.Status,
                Weigh = doc.Weigh,
                CreatedAt = doc.CreatedAt,
                UpdatedAt = doc.UpdatedAt,
            });
    }

    public async Task<PagedData<PermissionViewSearchResult>> SearchPermissionViewAsync(PermissionViewSearchQuery query, CancellationToken ct = default)
    {
        var descriptor = RbacElasticQueryBuilder.BuildPermissionViewSearch(query);
        var response = await _esClient.SearchAsync<PermissionViewDocument>(d => descriptor, ct);
        return Map(response, query, RbacPermissionViewIndexMapping.IndexName,
            doc => new PermissionViewSearchResult
            {
                PermissionCode = doc.PermissionCode,
                Action = doc.Action,
                ResourceType = doc.ResourceType,
                Title = doc.Title,
            });
    }

    public async Task<PagedData<AuditLogSearchResult>> SearchAuditLogsAsync(AuditLogSearchQuery query, CancellationToken ct = default)
    {
        var descriptor = RbacElasticQueryBuilder.BuildAuditLogSearch(query);
        var response = await _esClient.SearchAsync<AuditLogDocument>(d => descriptor, ct);
        return Map(response, query, RbacAuditLogIndexMapping.IndexName,
            doc => new AuditLogSearchResult
            {
                AuditId = doc.AuditId,
                Userid = doc.Userid,
                Project = doc.Project,
                PermissionCode = doc.PermissionCode,
                Result = doc.Result,
                Reason = doc.Reason,
                CreatedAt = doc.CreatedAt,
            });
    }

    private PagedData<TResult> Map<TDoc, TResult>(
        ISearchResponse<TDoc> response, PagedQuery query,
        string indexName, Func<TDoc, TResult> mapper) where TDoc : class
    {
        if (!response.IsValid)
        {
            _logger.LogError("ES search failed index={Index} error={Err}",
                indexName, response.ServerError?.Error?.Reason ?? response.DebugInformation);
            return new PagedData<TResult> { List = Array.Empty<TResult>(), Total = 0 };
        }
        return new PagedData<TResult>
        {
            List = response.Documents.Select(mapper).ToList(),
            Total = response.Total,
        };
    }
}
