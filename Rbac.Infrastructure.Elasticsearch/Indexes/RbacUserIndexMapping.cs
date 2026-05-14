using Nest;
using Rbac.Infrastructure.Elasticsearch.Documents;

namespace Rbac.Infrastructure.Elasticsearch.Indexes;

/// <summary>
/// rbac_user_index 索引 mapping 定义。
///
/// 用途：管理员用户查询（列表、搜索、过滤）。
/// 不参与实时鉴权，不作为编辑真相（写入前必须回读 MySQL）。
///
/// 精确过滤字段：userid, projectCodes, groupCodes, status。
/// 模糊搜索字段：username, groupNames, allText。
/// allText copy_to 来源：userid, username, groupNames, projectCodes, groupCodes, status。
/// </summary>
public static class RbacUserIndexMapping
{
    public const string IndexName = "rbac_user_index";

    /// <summary>
    /// 构建 CreateIndexDescriptor，由 bootstrap 服务调用。
    /// 包含 settings（IK 分词器）和 mappings（字段类型）。
    /// </summary>
    public static CreateIndexDescriptor Build(CreateIndexDescriptor descriptor) =>
        descriptor
            .Settings(s => s
                .NumberOfShards(1)
                .NumberOfReplicas(1)
                .Analysis(a => a
                    .Analyzers(an => an
                        .Custom("ik_max_word_analyzer", c => c
                            .Tokenizer("ik_max_word"))
                        .Custom("ik_smart_analyzer", c => c
                            .Tokenizer("ik_smart")))))
            .Map<UserDocument>(m => m
                .AutoMap()
                .Properties(p => p
                    .Keyword(k => k.Name(n => n.Id))
                    .Keyword(k => k.Name(n => n.Userid))
                    .Text(t => t
                        .Name(n => n.Username)
                        .Analyzer("ik_max_word_analyzer")
                        .SearchAnalyzer("ik_smart_analyzer")
                        .CopyTo(c => c.Field(f => f.AllText))
                        .Fields(f => f
                            .Keyword(k => k.Name("keyword"))))
                    .Keyword(k => k.Name(n => n.ProjectCodes)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.GroupCodes)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Text(t => t
                        .Name(n => n.GroupNames)
                        .Analyzer("ik_max_word_analyzer")
                        .CopyTo(c => c.Field(f => f.AllText))
                        .Fields(f => f
                            .Keyword(k => k.Name("keyword"))))
                    .Keyword(k => k.Name(n => n.Status)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.SuperProjects))
                    .Date(d => d.Name(n => n.CreatedAt))
                    .Date(d => d.Name(n => n.UpdatedAt))
                    // allText：不存储原始值，只接收 copy_to 数据
                    .Text(t => t
                        .Name(n => n.AllText)
                        .Analyzer("ik_max_word_analyzer")
                        .SearchAnalyzer("ik_smart_analyzer")
                        .Store(false))));
}
