using Nest;
using Rbac.Infrastructure.Elasticsearch.Documents;

namespace Rbac.Infrastructure.Elasticsearch.Indexes;

/// <summary>
/// rbac_permission_view_index 索引 mapping 定义。
///
/// 用途：权限管理视图，聚合用户/组/规则/权限码，方便管理端排查。
/// 精确过滤：project, permissionCode, ruleCode, action, resourceType, status。
/// 模糊搜索：title, path, groupNames, allText。
/// allText copy_to 来源：permissionCode, ruleCode, action, resourceType,
///   title, path, groupCodes, groupNames, project, status。
/// </summary>
public static class RbacPermissionViewIndexMapping
{
    public const string IndexName = "rbac_permission_view_index";

    public static CreateIndexDescriptor Build(CreateIndexDescriptor descriptor) =>
        descriptor
            .Settings(s => s
                .NumberOfShards(1)
                .NumberOfReplicas(1)
                .Analysis(a => a
                    .Analyzers(an => an
                        .Custom("ik_max_word_analyzer", c => c.Tokenizer("ik_max_word"))
                        .Custom("ik_smart_analyzer", c => c.Tokenizer("ik_smart")))))
            .Map<PermissionViewDocument>(m => m
                .AutoMap()
                .Properties(p => p
                    .Keyword(k => k.Name(n => n.Project)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.HttpMethod)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.PermissionCode)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.RuleCode)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.Action)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.ResourceType)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Text(t => t
                        .Name(n => n.Title)
                        .Analyzer("ik_max_word_analyzer")
                        .SearchAnalyzer("ik_smart_analyzer")
                        .CopyTo(c => c.Field(f => f.AllText))
                        .Fields(f => f.Keyword(k => k.Name("keyword"))))
                    .Text(t => t
                        .Name(n => n.Path)
                        .CopyTo(c => c.Field(f => f.AllText))
                        .Fields(f => f.Keyword(k => k.Name("keyword"))))
                    .Keyword(k => k.Name(n => n.GroupCodes)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Text(t => t
                        .Name(n => n.GroupNames)
                        .Analyzer("ik_max_word_analyzer")
                        .CopyTo(c => c.Field(f => f.AllText))
                        .Fields(f => f.Keyword(k => k.Name("keyword"))))
                    .Keyword(k => k.Name(n => n.Status)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Date(d => d.Name(n => n.UpdatedAt))
                    .Text(t => t
                        .Name(n => n.AllText)
                        .Analyzer("ik_max_word_analyzer")
                        .SearchAnalyzer("ik_smart_analyzer")
                        .Store(false))));
}
