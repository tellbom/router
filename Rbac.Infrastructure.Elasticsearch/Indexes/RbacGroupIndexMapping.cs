using Nest;
using Rbac.Infrastructure.Elasticsearch.Documents;

namespace Rbac.Infrastructure.Elasticsearch.Indexes;

/// <summary>
/// rbac_group_index 索引 mapping 定义。
///
/// 用途：权限组查询。
/// 精确过滤：project, groupCode, status, permissionCodes。
/// 模糊搜索：groupName, allText。
/// allText copy_to 来源：groupCode, groupName, parentGroupCode, ruleCodes,
///   permissionCodes, project, status。
/// </summary>
public static class RbacGroupIndexMapping
{
    public const string IndexName = "rbac_group_index";

    public static CreateIndexDescriptor Build(CreateIndexDescriptor descriptor) =>
        descriptor
            .Settings(s => s
                .NumberOfShards(1)
                .NumberOfReplicas(1)
                .Analysis(a => a
                    .Analyzers(an => an
                        .Custom("ik_max_word_analyzer", c => c.Tokenizer("ik_max_word"))
                        .Custom("ik_smart_analyzer", c => c.Tokenizer("ik_smart")))))
            .Map<GroupDocument>(m => m
                .AutoMap()
                .Properties(p => p
                    .Keyword(k => k.Name(n => n.Id))
                    .Keyword(k => k.Name(n => n.Project)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.GroupCode)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Text(t => t
                        .Name(n => n.GroupName)
                        .Analyzer("ik_max_word_analyzer")
                        .SearchAnalyzer("ik_smart_analyzer")
                        .CopyTo(c => c.Field(f => f.AllText))
                        .Fields(f => f.Keyword(k => k.Name("keyword"))))
                    .Keyword(k => k.Name(n => n.ParentGroupCode)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.RuleCodes)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.PermissionCodes)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.Status)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Date(d => d.Name(n => n.CreatedAt))
                    .Date(d => d.Name(n => n.UpdatedAt))
                    .Text(t => t
                        .Name(n => n.AllText)
                        .Analyzer("ik_max_word_analyzer")
                        .SearchAnalyzer("ik_smart_analyzer")
                        .Store(false))));
}
