using Microsoft.Extensions.Logging;
using Nest;
using Rbac.Application.Contracts.Common;
using Rbac.Infrastructure.Elasticsearch.Documents;
using Rbac.Infrastructure.Elasticsearch.Indexes;
using Rbac.Infrastructure.Elasticsearch.Search;

namespace Rbac.Application.Search;

/// <summary>
/// 管理端 ES 查询服务。
///
/// 约束：
/// - 只读 ES，不写 ES（写入由 Worker Outbox 处理器负责）。
/// - 查询结果不作为编辑/删除的真相，写入前必须回读 MySQL。
/// - 返回格式：data.list / data.total（与前端 table 组件约定一致）。
/// - 不参与实时鉴权链路。
/// </summary>
public sealed class RbacManagementSearchService
{
    private readonly IElasticClient _esClient;
    private readonly ILogger<RbacManagementSearchService> _logger;

    public RbacManagementSearchService(
        IElasticClient esClient,
        ILogger<RbacManagementSearchService> logger)
    {
        _esClient = esClient;
        _logger = logger;
    }

    // ── 用户搜索 ─────────────────────────────────────────────────

    public async Task<PagedData<UserDocument>> SearchUsersAsync(
        UserSearchQuery query, CancellationToken ct = default)
    {
        var descriptor = RbacElasticQueryBuilder.BuildUserSearch(query);
        var response = await _esClient.SearchAsync<UserDocument>(
            d => descriptor, ct);
        return ToPagedData(response, query, RbacUserIndexMapping.IndexName);
    }

    // ── 权限组搜索 ───────────────────────────────────────────────

    public async Task<PagedData<GroupDocument>> SearchGroupsAsync(
        GroupSearchQuery query, CancellationToken ct = default)
    {
        var descriptor = RbacElasticQueryBuilder.BuildGroupSearch(query);
        var response = await _esClient.SearchAsync<GroupDocument>(
            d => descriptor, ct);
        return ToPagedData(response, query, RbacGroupIndexMapping.IndexName);
    }

    // ── 规则搜索 ─────────────────────────────────────────────────

    public async Task<PagedData<RuleDocument>> SearchRulesAsync(
        RuleSearchQuery query, CancellationToken ct = default)
    {
        var descriptor = RbacElasticQueryBuilder.BuildRuleSearch(query);
        var response = await _esClient.SearchAsync<RuleDocument>(
            d => descriptor, ct);
        return ToPagedData(response, query, RbacRuleIndexMapping.IndexName);
    }

    // ── 权限视图搜索 ─────────────────────────────────────────────

    public async Task<PagedData<PermissionViewDocument>> SearchPermissionViewAsync(
        PermissionViewSearchQuery query, CancellationToken ct = default)
    {
        var descriptor = RbacElasticQueryBuilder.BuildPermissionViewSearch(query);
        var response = await _esClient.SearchAsync<PermissionViewDocument>(
            d => descriptor, ct);
        return ToPagedData(response, query, RbacPermissionViewIndexMapping.IndexName);
    }

    // ── 审计日志搜索 ─────────────────────────────────────────────

    public async Task<PagedData<AuditLogDocument>> SearchAuditLogsAsync(
        AuditLogSearchQuery query, CancellationToken ct = default)
    {
        var descriptor = RbacElasticQueryBuilder.BuildAuditLogSearch(query);
        var response = await _esClient.SearchAsync<AuditLogDocument>(
            d => descriptor, ct);
        return ToPagedData(response, query, RbacAuditLogIndexMapping.IndexName);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private PagedData<TDoc> ToPagedData<TDoc>(
        ISearchResponse<TDoc> response,
        PagedQuery query,
        string indexName) where TDoc : class
    {
        if (!response.IsValid)
        {
            _logger.LogError(
                "ES search failed index={Index} error={Error}",
                indexName, response.ServerError?.Error?.Reason ?? response.DebugInformation);

            return new PagedData<TDoc>
            {
                List = Array.Empty<TDoc>(),
                Total = 0,
            };
        }

        _logger.LogDebug(
            "ES search index={Index} total={Total} page={Page} pageSize={Size}",
            indexName, response.Total, query.Page, query.PageSize);

        return new PagedData<TDoc>
        {
            List = response.Documents.ToList(),
            Total = response.Total,
        };
    }
}
