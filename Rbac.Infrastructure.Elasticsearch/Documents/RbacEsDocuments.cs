using Nest;

namespace Rbac.Infrastructure.Elasticsearch.Documents;

// ── rbac_user_index ───────────────────────────────────────────────

/// <summary>
/// ES 用户文档。对应 rbac_user_index。
/// DxEId 必须为 keyword（string），不允许 long。
/// allText copy_to 来源：userid, username, groupNames, projectCodes, groupCodes, status, DxEId。
/// </summary>
[ElasticsearchType(RelationName = "rbac_user")]
public sealed class UserDocument
{
    [Keyword(Name = "id")]
    public string Id { get; set; } = string.Empty;

    [Keyword(Name = "dxe_id")]
    public string DxEId { get; set; } = string.Empty;

    [Keyword(Name = "userid")]
    public string Userid { get; set; } = string.Empty;

    [Text(Name = "username", CopyTo = new[] { "allText" })]
    public string Username { get; set; } = string.Empty;

    [Keyword(Name = "projectCodes", CopyTo = new[] { "allText" })]
    public IList<string> ProjectCodes { get; set; } = new List<string>();

    [Keyword(Name = "groupCodes", CopyTo = new[] { "allText" })]
    public IList<string> GroupCodes { get; set; } = new List<string>();

    [Text(Name = "groupNames", CopyTo = new[] { "allText" })]
    public IList<string> GroupNames { get; set; } = new List<string>();

    [Keyword(Name = "status", CopyTo = new[] { "allText" })]
    public string Status { get; set; } = string.Empty;

    [Keyword(Name = "superProjects")]
    public IList<string> SuperProjects { get; set; } = new List<string>();

    [Date(Name = "createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [Date(Name = "updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [Text(Name = "allText", Index = false)]
    public string AllText { get; set; } = string.Empty;
}

// ── rbac_group_index ──────────────────────────────────────────────

/// <summary>
/// ES 权限组文档。对应 rbac_group_index。
/// allText copy_to 来源：groupCode, groupName, parentGroupCode, ruleCodes,
///   permissionCodes, project, status, DxEId。
/// </summary>
[ElasticsearchType(RelationName = "rbac_group")]
public sealed class GroupDocument
{
    [Keyword(Name = "id")]
    public string Id { get; set; } = string.Empty;

    [Keyword(Name = "dxe_id", CopyTo = new[] { "allText" })]
    public string DxEId { get; set; } = string.Empty;

    [Keyword(Name = "project", CopyTo = new[] { "allText" })]
    public string Project { get; set; } = string.Empty;

    [Keyword(Name = "groupCode", CopyTo = new[] { "allText" })]
    public string GroupCode { get; set; } = string.Empty;

    [Text(Name = "groupName", CopyTo = new[] { "allText" })]
    public string GroupName { get; set; } = string.Empty;

    [Keyword(Name = "parentGroupCode", CopyTo = new[] { "allText" })]
    public string? ParentGroupCode { get; set; }

    [Keyword(Name = "ruleCodes", CopyTo = new[] { "allText" })]
    public IList<string> RuleCodes { get; set; } = new List<string>();

    [Keyword(Name = "permissionCodes", CopyTo = new[] { "allText" })]
    public IList<string> PermissionCodes { get; set; } = new List<string>();

    [Keyword(Name = "status", CopyTo = new[] { "allText" })]
    public string Status { get; set; } = string.Empty;

    [Date(Name = "createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [Date(Name = "updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [Text(Name = "allText", Index = false)]
    public string AllText { get; set; } = string.Empty;
}

// ── rbac_rule_index ───────────────────────────────────────────────

/// <summary>
/// ES 规则文档。对应 rbac_rule_index。
/// allText copy_to 来源：ruleCode, permissionCode, parentRuleCode, title, name,
///   path, type, menu_type, component, url, project, status, DxEId。
/// </summary>
[ElasticsearchType(RelationName = "rbac_rule")]
public sealed class RuleDocument
{
    [Keyword(Name = "id")]
    public string Id { get; set; } = string.Empty;

    [Keyword(Name = "dxe_id", CopyTo = new[] { "allText" })]
    public string DxEId { get; set; } = string.Empty;

    [Keyword(Name = "project", CopyTo = new[] { "allText" })]
    public string Project { get; set; } = string.Empty;

    [Keyword(Name = "ruleCode", CopyTo = new[] { "allText" })]
    public string RuleCode { get; set; } = string.Empty;

    [Keyword(Name = "permissionCode", CopyTo = new[] { "allText" })]
    public string PermissionCode { get; set; } = string.Empty;

    [Keyword(Name = "parentRuleCode", CopyTo = new[] { "allText" })]
    public string? ParentRuleCode { get; set; }

    [Text(Name = "title", CopyTo = new[] { "allText" })]
    public string Title { get; set; } = string.Empty;

    [Text(Name = "name", CopyTo = new[] { "allText" })]
    public string Name { get; set; } = string.Empty;

    [Text(Name = "path", CopyTo = new[] { "allText" })]
    public string Path { get; set; } = string.Empty;

    [Keyword(Name = "type", CopyTo = new[] { "allText" })]
    public string Type { get; set; } = string.Empty;

    [Keyword(Name = "menu_type", CopyTo = new[] { "allText" })]
    public string MenuType { get; set; } = string.Empty;

    [Keyword(Name = "component", CopyTo = new[] { "allText" })]
    public string? Component { get; set; }

    [Keyword(Name = "url", CopyTo = new[] { "allText" })]
    public string? Url { get; set; }

    [Keyword(Name = "extend")]
    public string? Extend { get; set; }

    [Keyword(Name = "keepalive")]
    public string Keepalive { get; set; } = "false";

    [Keyword(Name = "status", CopyTo = new[] { "allText" })]
    public string Status { get; set; } = string.Empty;

    [Number(NumberType.Integer, Name = "weigh")]
    public int Weigh { get; set; }

    [Date(Name = "createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [Date(Name = "updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [Text(Name = "allText", Index = false)]
    public string AllText { get; set; } = string.Empty;
}

// ── rbac_permission_view_index ────────────────────────────────────

/// <summary>
/// ES 权限视图文档。对应 rbac_permission_view_index。
/// allText copy_to 来源：permissionCode, ruleCode, action, resourceType,
///   title, path, groupCodes, groupNames, project, status。
/// </summary>
[ElasticsearchType(RelationName = "rbac_permission_view")]
public sealed class PermissionViewDocument
{
    [Keyword(Name = "project", CopyTo = new[] { "allText" })]
    public string Project { get; set; } = string.Empty;

    [Keyword(Name = "permissionCode", CopyTo = new[] { "allText" })]
    public string PermissionCode { get; set; } = string.Empty;

    [Keyword(Name = "ruleCode", CopyTo = new[] { "allText" })]
    public string RuleCode { get; set; } = string.Empty;

    [Keyword(Name = "action", CopyTo = new[] { "allText" })]
    public string Action { get; set; } = string.Empty;

    [Keyword(Name = "resourceType", CopyTo = new[] { "allText" })]
    public string ResourceType { get; set; } = string.Empty;

    [Text(Name = "title", CopyTo = new[] { "allText" })]
    public string Title { get; set; } = string.Empty;

    [Text(Name = "path", CopyTo = new[] { "allText" })]
    public string Path { get; set; } = string.Empty;

    [Keyword(Name = "groupCodes", CopyTo = new[] { "allText" })]
    public IList<string> GroupCodes { get; set; } = new List<string>();

    [Text(Name = "groupNames", CopyTo = new[] { "allText" })]
    public IList<string> GroupNames { get; set; } = new List<string>();

    [Keyword(Name = "status", CopyTo = new[] { "allText" })]
    public string Status { get; set; } = string.Empty;

    [Date(Name = "updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [Text(Name = "allText", Index = false)]
    public string AllText { get; set; } = string.Empty;
}

// ── rbac_audit_log_index ──────────────────────────────────────────

/// <summary>
/// ES 审计日志文档。对应 rbac_audit_log_index。
/// allText copy_to 来源：auditId, traceId, userid, project, requestedProject,
///   permissionCode, action, result, reason, apiPath, httpMethod, clientIp, userAgent。
/// </summary>
[ElasticsearchType(RelationName = "rbac_audit_log")]
public sealed class AuditLogDocument
{
    [Keyword(Name = "auditId", CopyTo = new[] { "allText" })]
    public string AuditId { get; set; } = string.Empty;

    [Keyword(Name = "traceId", CopyTo = new[] { "allText" })]
    public string TraceId { get; set; } = string.Empty;

    [Keyword(Name = "userid", CopyTo = new[] { "allText" })]
    public string Userid { get; set; } = string.Empty;

    [Keyword(Name = "project", CopyTo = new[] { "allText" })]
    public string Project { get; set; } = string.Empty;

    [Keyword(Name = "requestedProject", CopyTo = new[] { "allText" })]
    public string RequestedProject { get; set; } = string.Empty;

    [Keyword(Name = "permissionCode", CopyTo = new[] { "allText" })]
    public string PermissionCode { get; set; } = string.Empty;

    [Keyword(Name = "action", CopyTo = new[] { "allText" })]
    public string Action { get; set; } = string.Empty;

    [Keyword(Name = "result", CopyTo = new[] { "allText" })]
    public string Result { get; set; } = string.Empty;

    [Keyword(Name = "reason", CopyTo = new[] { "allText" })]
    public string Reason { get; set; } = string.Empty;

    [Text(Name = "apiPath", CopyTo = new[] { "allText" })]
    public string ApiPath { get; set; } = string.Empty;

    [Keyword(Name = "httpMethod", CopyTo = new[] { "allText" })]
    public string HttpMethod { get; set; } = string.Empty;

    [Ip(Name = "clientIp", CopyTo = new[] { "allText" })]
    public string ClientIp { get; set; } = string.Empty;

    [Text(Name = "userAgent", CopyTo = new[] { "allText" })]
    public string UserAgent { get; set; } = string.Empty;

    [Date(Name = "createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [Text(Name = "allText", Index = false)]
    public string AllText { get; set; } = string.Empty;
}
