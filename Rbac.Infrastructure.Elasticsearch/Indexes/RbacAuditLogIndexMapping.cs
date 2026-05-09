using Nest;
using Rbac.Infrastructure.Elasticsearch.Documents;

namespace Rbac.Infrastructure.Elasticsearch.Indexes;

/// <summary>
/// rbac_audit_log_index 索引 mapping 定义。
///
/// 用途：权限鉴权、管理操作、同步失败等审计检索。
/// 精确过滤：userid, project, permissionCode, result, httpMethod, createdAt range。
/// 模糊搜索：apiPath, userAgent, allText。
/// allText copy_to 来源：auditId, traceId, userid, project, requestedProject,
///   permissionCode, action, result, reason, apiPath, httpMethod, clientIp, userAgent。
/// </summary>
public static class RbacAuditLogIndexMapping
{
    public const string IndexName = "rbac_audit_log_index";

    public static CreateIndexDescriptor Build(CreateIndexDescriptor descriptor) =>
        descriptor
            .Settings(s => s
                .NumberOfShards(1)
                .NumberOfReplicas(1)
                // 审计日志写多读少，关闭刷新间隔提升写入性能
                .RefreshInterval(new Nest.Time("5s")))
            .Map<AuditLogDocument>(m => m
                .AutoMap()
                .Properties(p => p
                    .Keyword(k => k.Name(n => n.AuditId)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.TraceId)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.Userid)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.Project)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.RequestedProject)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.PermissionCode)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.Action)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    // result: allow / deny / error
                    .Keyword(k => k.Name(n => n.Result)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Keyword(k => k.Name(n => n.Reason)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Text(t => t
                        .Name(n => n.ApiPath)
                        .CopyTo(c => c.Field(f => f.AllText))
                        .Fields(f => f.Keyword(k => k.Name("keyword"))))
                    .Keyword(k => k.Name(n => n.HttpMethod)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    // clientIp 使用 ip 类型，支持 CIDR 范围查询
                    .Ip(i => i.Name(n => n.ClientIp)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Text(t => t
                        .Name(n => n.UserAgent)
                        .CopyTo(c => c.Field(f => f.AllText)))
                    .Date(d => d.Name(n => n.CreatedAt))
                    .Text(t => t
                        .Name(n => n.AllText)
                        .Store(false))));
}
