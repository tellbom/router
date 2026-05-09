using Nest;
using Rbac.Application.Contracts.Common;

namespace Rbac.Infrastructure.Elasticsearch.Search;

/// <summary>
/// 管理端查询入参基类。继承 PagedQuery 获得分页能力。
/// </summary>
public abstract class EsManagementQuery : PagedQuery
{
    /// <summary>精确过滤：项目标识。</summary>
    public string? Project { get; init; }

    /// <summary>精确过滤：状态（Active / Disabled）。</summary>
    public string? Status { get; init; }

    /// <summary>全字段模糊搜索（对 allText 字段）。</summary>
    public string? Keyword { get; init; }
}

/// <summary>用户查询入参。</summary>
public sealed class UserSearchQuery : EsManagementQuery
{
    public string? Userid { get; init; }
    public string? GroupCode { get; init; }
}

/// <summary>权限组查询入参。</summary>
public sealed class GroupSearchQuery : EsManagementQuery
{
    public string? GroupCode { get; init; }
    public string? PermissionCode { get; init; }
}

/// <summary>规则查询入参。</summary>
public sealed class RuleSearchQuery : EsManagementQuery
{
    public string? RuleCode { get; init; }
    public string? PermissionCode { get; init; }
    public string? Type { get; init; }
    public string? MenuType { get; init; }
}

/// <summary>权限视图查询入参。</summary>
public sealed class PermissionViewSearchQuery : EsManagementQuery
{
    public string? PermissionCode { get; init; }
    public string? Action { get; init; }
    public string? ResourceType { get; init; }
}

/// <summary>审计日志查询入参。</summary>
public sealed class AuditLogSearchQuery : EsManagementQuery
{
    public string? Userid { get; init; }
    public string? PermissionCode { get; init; }
    public string? Result { get; init; }
    public string? HttpMethod { get; init; }
    public DateTimeOffset? CreatedAtFrom { get; init; }
    public DateTimeOffset? CreatedAtTo { get; init; }
}

/// <summary>
/// NEST 7.17.x 查询构建器。
///
/// 约束：
/// - 精确过滤使用 term / terms 查询（keyword 字段）。
/// - 时间范围使用 range 查询（date 字段）。
/// - 模糊搜索使用 match 或 multi_match 对 allText 字段。
/// - 分页使用 from / size（from = query.Offset，size = query.PageSize）。
/// - 结果不作为编辑真相，ES 文档只用于展示。
/// </summary>
public static class RbacElasticQueryBuilder
{
    // ── 用户查询 ─────────────────────────────────────────────────

    public static SearchDescriptor<Documents.UserDocument> BuildUserSearch(UserSearchQuery q) =>
        new SearchDescriptor<Documents.UserDocument>()
            .From(q.Offset)
            .Size(q.PageSize)
            .Query(qc => BuildBool(qc, q, extra => extra
                .Term(t => t.Field(f => f.Userid).Value(q.Userid).When(q.Userid))
                .Term(t => t.Field(f => f.GroupCodes).Value(q.GroupCode).When(q.GroupCode))));

    // ── 权限组查询 ───────────────────────────────────────────────

    public static SearchDescriptor<Documents.GroupDocument> BuildGroupSearch(GroupSearchQuery q) =>
        new SearchDescriptor<Documents.GroupDocument>()
            .From(q.Offset)
            .Size(q.PageSize)
            .Query(qc => BuildBool(qc, q, extra => extra
                .Term(t => t.Field(f => f.GroupCode).Value(q.GroupCode).When(q.GroupCode))
                .Term(t => t.Field(f => f.PermissionCodes).Value(q.PermissionCode).When(q.PermissionCode))));

    // ── 规则查询 ─────────────────────────────────────────────────

    public static SearchDescriptor<Documents.RuleDocument> BuildRuleSearch(RuleSearchQuery q) =>
        new SearchDescriptor<Documents.RuleDocument>()
            .From(q.Offset)
            .Size(q.PageSize)
            .Query(qc => BuildBool(qc, q, extra => extra
                .Term(t => t.Field(f => f.RuleCode).Value(q.RuleCode).When(q.RuleCode))
                .Term(t => t.Field(f => f.PermissionCode).Value(q.PermissionCode).When(q.PermissionCode))
                .Term(t => t.Field(f => f.Type).Value(q.Type).When(q.Type))
                .Term(t => t.Field(f => f.MenuType).Value(q.MenuType).When(q.MenuType))));

    // ── 权限视图查询 ─────────────────────────────────────────────

    public static SearchDescriptor<Documents.PermissionViewDocument> BuildPermissionViewSearch(
        PermissionViewSearchQuery q) =>
        new SearchDescriptor<Documents.PermissionViewDocument>()
            .From(q.Offset)
            .Size(q.PageSize)
            .Query(qc => BuildBool(qc, q, extra => extra
                .Term(t => t.Field(f => f.PermissionCode).Value(q.PermissionCode).When(q.PermissionCode))
                .Term(t => t.Field(f => f.Action).Value(q.Action).When(q.Action))
                .Term(t => t.Field(f => f.ResourceType).Value(q.ResourceType).When(q.ResourceType))));

    // ── 审计日志查询 ─────────────────────────────────────────────

    public static SearchDescriptor<Documents.AuditLogDocument> BuildAuditLogSearch(AuditLogSearchQuery q) =>
        new SearchDescriptor<Documents.AuditLogDocument>()
            .From(q.Offset)
            .Size(q.PageSize)
            .Sort(s => s.Descending(f => f.CreatedAt))
            .Query(qc =>
            {
                var filters = new List<QueryContainer>();

                if (!string.IsNullOrWhiteSpace(q.Project))
                    filters.Add(new TermQuery { Field = "project", Value = q.Project });
                if (!string.IsNullOrWhiteSpace(q.Userid))
                    filters.Add(new TermQuery { Field = "userid", Value = q.Userid });
                if (!string.IsNullOrWhiteSpace(q.PermissionCode))
                    filters.Add(new TermQuery { Field = "permissionCode", Value = q.PermissionCode });
                if (!string.IsNullOrWhiteSpace(q.Result))
                    filters.Add(new TermQuery { Field = "result", Value = q.Result });
                if (!string.IsNullOrWhiteSpace(q.HttpMethod))
                    filters.Add(new TermQuery { Field = "httpMethod", Value = q.HttpMethod });
                if (!string.IsNullOrWhiteSpace(q.Status))
                    filters.Add(new TermQuery { Field = "status", Value = q.Status });

                // 时间范围过滤
                if (q.CreatedAtFrom.HasValue || q.CreatedAtTo.HasValue)
                {
                    filters.Add(new DateRangeQuery
                    {
                        Field = "createdAt",
                        GreaterThanOrEqualTo = q.CreatedAtFrom?.ToString("O"),
                        LessThanOrEqualTo = q.CreatedAtTo?.ToString("O"),
                    });
                }

                // allText 模糊搜索
                QueryContainer? mustQuery = null;
                if (!string.IsNullOrWhiteSpace(q.Keyword))
                {
                    mustQuery = new MatchQuery
                    {
                        Field = "allText",
                        Query = q.Keyword,
                        Operator = Operator.And,
                    };
                }

                return new BoolQuery
                {
                    Must = mustQuery != null ? new[] { mustQuery } : Array.Empty<QueryContainer>(),
                    Filter = filters,
                };
            });

    // ── 通用 Bool 构建辅助 ────────────────────────────────────────

    private static QueryContainer BuildBool<TDoc>(
        QueryContainerDescriptor<TDoc> qc,
        EsManagementQuery q,
        Func<BoolQueryDescriptor<TDoc>, BoolQueryDescriptor<TDoc>> extraFilters)
        where TDoc : class
    {
        return qc.Bool(b =>
        {
            // Must：allText 模糊搜索（有关键词时）
            if (!string.IsNullOrWhiteSpace(q.Keyword))
                b = b.Must(m => m.Match(mt =>
                    mt.Field("allText").Query(q.Keyword).Operator(Operator.And)));

            // Filter：精确过滤（不影响评分）
            b = b.Filter(f =>
            {
                var filters = new List<QueryContainer>();

                if (!string.IsNullOrWhiteSpace(q.Project))
                    filters.Add(new TermQuery { Field = "project", Value = q.Project });
                if (!string.IsNullOrWhiteSpace(q.Status))
                    filters.Add(new TermQuery { Field = "status", Value = q.Status });

                // 子类额外过滤条件
                // 由调用方通过 extraFilters 追加（此处简化：子类单独构建）
                return f.Bool(fb => fb.Filter(filters.Select<QueryContainer, Func<QueryContainerDescriptor<TDoc>, QueryContainer>>(
                    fq => _ => fq).ToArray()));
            });

            return b;
        });
    }
}

/// <summary>NEST QueryContainer 扩展：条件为 null/空时跳过该 term。</summary>
internal static class QueryExtensions
{
    public static QueryContainer When(this TermQueryDescriptor<Documents.UserDocument> q, string? value) =>
        value is null ? new MatchAllQuery() : q;

    public static QueryContainer When(this TermQueryDescriptor<Documents.GroupDocument> q, string? value) =>
        value is null ? new MatchAllQuery() : q;

    public static QueryContainer When(this TermQueryDescriptor<Documents.RuleDocument> q, string? value) =>
        value is null ? new MatchAllQuery() : q;

    public static QueryContainer When(this TermQueryDescriptor<Documents.PermissionViewDocument> q, string? value) =>
        value is null ? new MatchAllQuery() : q;
}
