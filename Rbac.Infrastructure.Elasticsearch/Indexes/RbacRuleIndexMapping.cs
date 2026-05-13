using Nest;
using Rbac.Infrastructure.Elasticsearch.Documents;

namespace Rbac.Infrastructure.Elasticsearch.Indexes;

/// <summary>
/// rbac_rule_index 索引 mapping 定义。
///
/// 用途：菜单规则和按钮规则查询。
/// 精确过滤：project, permissionCode, ruleCode, type, menu_type, status, dxe_id。
/// 模糊搜索：title, name, path, allText。
/// allText copy_to 来源：ruleCode, permissionCode, parentRuleCode, title, name, path,
///   type, menu_type, component, url, project, status, dxe_id。
/// </summary>
public static class RbacRuleIndexMapping
{
    public const string IndexName = "rbac_rule_index";

    public static CreateIndexDescriptor Build(CreateIndexDescriptor descriptor) =>
        descriptor
            .Settings(s => s
                .NumberOfShards(1)
                .NumberOfReplicas(1)
                .Analysis(a => a
                    .Analyzers(an => an
                        .Custom("ik_max_word_analyzer", c => c.Tokenizer("ik_max_word"))
                        .Custom("ik_smart_analyzer", c => c.Tokenizer("ik_smart")))))
            .Map<RuleDocument>(m => m
                .AutoMap()
                .Properties(p => p
                    .Keyword(k => k.Name(n => n.Id))
                    .Keyword(k => k.Name(n => n.DxEId)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.Project)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.RuleCode)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.PermissionCode)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.ParentRuleCode)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Text(t => t
                        .Name(n => n.Title)
                        .Analyzer("ik_max_word_analyzer")
                        .SearchAnalyzer("ik_smart_analyzer")
                        .CopyTo(c => c.Field(f => f.AllText))
                        .Fields(f => f.Keyword(k => k.Name("keyword"))))
                    .Text(t => t
                        .Name(n => n.Name)
                        .Analyzer("ik_max_word_analyzer")
                        .CopyTo(c => c.Field(f => f.AllText))
                        .Fields(f => f.Keyword(k => k.Name("keyword"))))
                    .Text(t => t
                        .Name(n => n.Path)
                        .CopyTo(c => c.Field(f => f.AllText))
                        .Fields(f => f.Keyword(k => k.Name("keyword"))))
                    .Keyword(k => k.Name(n => n.Icon)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.Type)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.MenuType)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.Component)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.Url)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.Extend))
                    .Text(t => t
                        .Name(n => n.Remark)
                        .Analyzer("ik_max_word_analyzer")
                        .SearchAnalyzer("ik_smart_analyzer")
                        .CopyTo(c => c.Field(f => f.AllText))
                        .Fields(f => f.Keyword(k => k.Name("keyword"))))
                    .Keyword(k => k.Name(n => n.Keepalive))
                    .Keyword(k => k.Name(n => n.Status)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Number(n => n.Name(nn => nn.Weigh).Type(NumberType.Integer))
                    .Date(d => d.Name(n => n.CreatedAt))
                    .Date(d => d.Name(n => n.UpdatedAt))
                    .Text(t => t
                        .Name(n => n.AllText)
                        .Analyzer("ik_max_word_analyzer")
                        .SearchAnalyzer("ik_smart_analyzer")
                        .Store(false))));
}
