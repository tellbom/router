using Nest;
using Rbac.Application.Search;
using Rbac.Infrastructure.Elasticsearch.Documents;

namespace Rbac.Infrastructure.Elasticsearch.Search;

/// <summary>
/// NEST 7.17.x 查询构建器。
/// 查询入参 DTO 来自 Rbac.Application.Search，本类只负责转换为 NEST SearchDescriptor。
/// </summary>
public static class RbacElasticQueryBuilder
{
    public static SearchDescriptor<UserDocument> BuildUserSearch(UserSearchQuery q) =>
        new SearchDescriptor<UserDocument>().From(q.Offset).Size(q.PageSize)
            .Query(qc => qc.Bool(b =>
            {
                if (!string.IsNullOrWhiteSpace(q.Keyword))
                    b = b.Must(m => m.Match(mt => mt.Field("allText").Query(q.Keyword).Operator(Operator.And)));
                return b.Filter(BuildUserFilters(q));
            }));

    public static SearchDescriptor<GroupDocument> BuildGroupSearch(GroupSearchQuery q) =>
        new SearchDescriptor<GroupDocument>().From(q.Offset).Size(q.PageSize)
            .Query(qc => qc.Bool(b =>
            {
                if (!string.IsNullOrWhiteSpace(q.Keyword))
                    b = b.Must(m => m.Match(mt => mt.Field("allText").Query(q.Keyword).Operator(Operator.And)));
                return b.Filter(BuildGroupFilters(q));
            }));

    public static SearchDescriptor<RuleDocument> BuildRuleSearch(RuleSearchQuery q) =>
        new SearchDescriptor<RuleDocument>().From(q.Offset).Size(q.PageSize)
            .Query(qc => qc.Bool(b =>
            {
                if (!string.IsNullOrWhiteSpace(q.Keyword))
                    b = b.Must(m => m.Match(mt => mt.Field("allText").Query(q.Keyword).Operator(Operator.And)));
                return b.Filter(BuildRuleFilters(q));
            }));

    public static SearchDescriptor<PermissionViewDocument> BuildPermissionViewSearch(PermissionViewSearchQuery q) =>
        new SearchDescriptor<PermissionViewDocument>().From(q.Offset).Size(q.PageSize)
            .Query(qc => qc.Bool(b =>
            {
                if (!string.IsNullOrWhiteSpace(q.Keyword))
                    b = b.Must(m => m.Match(mt => mt.Field("allText").Query(q.Keyword).Operator(Operator.And)));
                return b.Filter(BuildPermViewFilters(q));
            }));

    public static SearchDescriptor<AuditLogDocument> BuildAuditLogSearch(AuditLogSearchQuery q) =>
        new SearchDescriptor<AuditLogDocument>().From(q.Offset).Size(q.PageSize)
            .Sort(s => s.Descending(f => f.CreatedAt))
            .Query(qc => qc.Bool(b =>
            {
                if (!string.IsNullOrWhiteSpace(q.Keyword))
                    b = b.Must(m => m.Match(mt => mt.Field("allText").Query(q.Keyword).Operator(Operator.And)));
                return b.Filter(BuildAuditFilters(q));
            }));

    // ── filter helpers ────────────────────────────────────────────

    private static Func<QueryContainerDescriptor<UserDocument>, QueryContainer> BuildUserFilters(UserSearchQuery q) =>
        f => f.Bool(b => b.Filter(Terms(
            ("projectCodes", q.Project), ("status", q.Status),
            ("userid", q.Userid), ("groupCodes", q.GroupCode))));

    private static Func<QueryContainerDescriptor<GroupDocument>, QueryContainer> BuildGroupFilters(GroupSearchQuery q) =>
        f => f.Bool(b => b.Filter(Terms(
            ("project", q.Project), ("status", q.Status),
            ("groupCode", q.GroupCode), ("permissionCodes", q.PermissionCode))));

    private static Func<QueryContainerDescriptor<RuleDocument>, QueryContainer> BuildRuleFilters(RuleSearchQuery q) =>
        f => f.Bool(b => b.Filter(Terms(
            ("project", q.Project), ("status", q.Status), ("ruleCode", q.RuleCode),
            ("permissionCode", q.PermissionCode), ("type", q.Type), ("menu_type", q.MenuType))));

    private static Func<QueryContainerDescriptor<PermissionViewDocument>, QueryContainer> BuildPermViewFilters(PermissionViewSearchQuery q) =>
        f => f.Bool(b => b.Filter(Terms(
            ("project", q.Project), ("status", q.Status), ("permissionCode", q.PermissionCode),
            ("action", q.Action), ("resourceType", q.ResourceType))));

    private static Func<QueryContainerDescriptor<AuditLogDocument>, QueryContainer> BuildAuditFilters(AuditLogSearchQuery q) =>
        f => f.Bool(b =>
        {
            var filters = Terms(
                ("project", q.Project), ("userid", q.Userid), ("permissionCode", q.PermissionCode),
                ("result", q.Result), ("httpMethod", q.HttpMethod), ("status", q.Status));

            if (q.CreatedAtFrom.HasValue || q.CreatedAtTo.HasValue)
                filters = filters.Append(new DateRangeQuery
                {
                    Field = "createdAt",
                    GreaterThanOrEqualTo = q.CreatedAtFrom?.ToString("O"),
                    LessThanOrEqualTo = q.CreatedAtTo?.ToString("O"),
                }).ToArray();

            return b.Filter(filters);
        });

    private static QueryContainer[] Terms(params (string field, string? value)[] pairs) =>
        pairs.Where(p => !string.IsNullOrWhiteSpace(p.value))
             .Select(p => (QueryContainer)new TermQuery { Field = p.field, Value = p.value })
             .ToArray();
}
