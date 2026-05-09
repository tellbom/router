using Nest;

namespace Rbac.Infrastructure.Elasticsearch.Documents;

// 鈹€鈹€ rbac_user_index 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

/// <summary>
/// ES 鐢ㄦ埛鏂囨。銆傚搴?rbac_user_index銆?/// DxEId 蹇呴』涓?keyword锛坰tring锛夛紝涓嶅厑璁?long銆?/// allText copy_to 鏉ユ簮锛歶serid, username, groupNames, projectCodes, groupCodes, status, DxEId銆?/// </summary>
[ElasticsearchType(RelationName = "rbac_user")]
public sealed class UserDocument
{
    [Keyword(Name = "id")]
    public string Id { get; set; } = string.Empty;

    [Keyword(Name = "dxe_id")]
    public string DxEId { get; set; } = string.Empty;

    [Keyword(Name = "userid")]
    public string Userid { get; set; } = string.Empty;

    [Text(Name = "username")]
    public string Username { get; set; } = string.Empty;

    [Keyword(Name = "projectCodes")]
    public IList<string> ProjectCodes { get; set; } = new List<string>();

    [Keyword(Name = "groupCodes")]
    public IList<string> GroupCodes { get; set; } = new List<string>();

    [Text(Name = "groupNames")]
    public IList<string> GroupNames { get; set; } = new List<string>();

    [Keyword(Name = "status")]
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

// 鈹€鈹€ rbac_group_index 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

/// <summary>
/// ES 鏉冮檺缁勬枃妗ｃ€傚搴?rbac_group_index銆?/// allText copy_to 鏉ユ簮锛歡roupCode, groupName, parentGroupCode, ruleCodes,
///   permissionCodes, project, status, DxEId銆?/// </summary>
[ElasticsearchType(RelationName = "rbac_group")]
public sealed class GroupDocument
{
    [Keyword(Name = "id")]
    public string Id { get; set; } = string.Empty;

    [Keyword(Name = "dxe_id")]
    public string DxEId { get; set; } = string.Empty;

    [Keyword(Name = "project")]
    public string Project { get; set; } = string.Empty;

    [Keyword(Name = "groupCode")]
    public string GroupCode { get; set; } = string.Empty;

    [Text(Name = "groupName")]
    public string GroupName { get; set; } = string.Empty;

    [Keyword(Name = "parentGroupCode")]
    public string? ParentGroupCode { get; set; }

    [Keyword(Name = "ruleCodes")]
    public IList<string> RuleCodes { get; set; } = new List<string>();

    [Keyword(Name = "permissionCodes")]
    public IList<string> PermissionCodes { get; set; } = new List<string>();

    [Keyword(Name = "status")]
    public string Status { get; set; } = string.Empty;

    [Date(Name = "createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [Date(Name = "updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [Text(Name = "allText", Index = false)]
    public string AllText { get; set; } = string.Empty;
}

// 鈹€鈹€ rbac_rule_index 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

/// <summary>
/// ES 瑙勫垯鏂囨。銆傚搴?rbac_rule_index銆?/// allText copy_to 鏉ユ簮锛歳uleCode, permissionCode, parentRuleCode, title, name,
///   path, type, menu_type, component, url, project, status, DxEId銆?/// </summary>
[ElasticsearchType(RelationName = "rbac_rule")]
public sealed class RuleDocument
{
    [Keyword(Name = "id")]
    public string Id { get; set; } = string.Empty;

    [Keyword(Name = "dxe_id")]
    public string DxEId { get; set; } = string.Empty;

    [Keyword(Name = "project")]
    public string Project { get; set; } = string.Empty;

    [Keyword(Name = "ruleCode")]
    public string RuleCode { get; set; } = string.Empty;

    [Keyword(Name = "permissionCode")]
    public string PermissionCode { get; set; } = string.Empty;

    [Keyword(Name = "parentRuleCode")]
    public string? ParentRuleCode { get; set; }

    [Text(Name = "title")]
    public string Title { get; set; } = string.Empty;

    [Text(Name = "name")]
    public string Name { get; set; } = string.Empty;

    [Text(Name = "path")]
    public string Path { get; set; } = string.Empty;

    [Keyword(Name = "type")]
    public string Type { get; set; } = string.Empty;

    [Keyword(Name = "menu_type")]
    public string MenuType { get; set; } = string.Empty;

    [Keyword(Name = "component")]
    public string? Component { get; set; }

    [Keyword(Name = "url")]
    public string? Url { get; set; }

    [Keyword(Name = "extend")]
    public string? Extend { get; set; }

    [Keyword(Name = "keepalive")]
    public string Keepalive { get; set; } = "false";

    [Keyword(Name = "status")]
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

// 鈹€鈹€ rbac_permission_view_index 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

/// <summary>
/// ES 鏉冮檺瑙嗗浘鏂囨。銆傚搴?rbac_permission_view_index銆?/// allText copy_to 鏉ユ簮锛歱ermissionCode, ruleCode, action, resourceType,
///   title, path, groupCodes, groupNames, project, status銆?/// </summary>
[ElasticsearchType(RelationName = "rbac_permission_view")]
public sealed class PermissionViewDocument
{
    [Keyword(Name = "project")]
    public string Project { get; set; } = string.Empty;

    [Keyword(Name = "permissionCode")]
    public string PermissionCode { get; set; } = string.Empty;

    [Keyword(Name = "ruleCode")]
    public string RuleCode { get; set; } = string.Empty;

    [Keyword(Name = "action")]
    public string Action { get; set; } = string.Empty;

    [Keyword(Name = "resourceType")]
    public string ResourceType { get; set; } = string.Empty;

    [Text(Name = "title")]
    public string Title { get; set; } = string.Empty;

    [Text(Name = "path")]
    public string Path { get; set; } = string.Empty;

    [Keyword(Name = "groupCodes")]
    public IList<string> GroupCodes { get; set; } = new List<string>();

    [Text(Name = "groupNames")]
    public IList<string> GroupNames { get; set; } = new List<string>();

    [Keyword(Name = "status")]
    public string Status { get; set; } = string.Empty;

    [Date(Name = "updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [Text(Name = "allText", Index = false)]
    public string AllText { get; set; } = string.Empty;
}

// 鈹€鈹€ rbac_audit_log_index 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

/// <summary>
/// ES 瀹¤鏃ュ織鏂囨。銆傚搴?rbac_audit_log_index銆?/// allText copy_to 鏉ユ簮锛歛uditId, traceId, userid, project, requestedProject,
///   permissionCode, action, result, reason, apiPath, httpMethod, clientIp, userAgent銆?/// </summary>
[ElasticsearchType(RelationName = "rbac_audit_log")]
public sealed class AuditLogDocument
{
    [Keyword(Name = "auditId")]
    public string AuditId { get; set; } = string.Empty;

    [Keyword(Name = "traceId")]
    public string TraceId { get; set; } = string.Empty;

    [Keyword(Name = "userid")]
    public string Userid { get; set; } = string.Empty;

    [Keyword(Name = "project")]
    public string Project { get; set; } = string.Empty;

    [Keyword(Name = "requestedProject")]
    public string RequestedProject { get; set; } = string.Empty;

    [Keyword(Name = "permissionCode")]
    public string PermissionCode { get; set; } = string.Empty;

    [Keyword(Name = "action")]
    public string Action { get; set; } = string.Empty;

    [Keyword(Name = "result")]
    public string Result { get; set; } = string.Empty;

    [Keyword(Name = "reason")]
    public string Reason { get; set; } = string.Empty;

    [Text(Name = "apiPath")]
    public string ApiPath { get; set; } = string.Empty;

    [Keyword(Name = "httpMethod")]
    public string HttpMethod { get; set; } = string.Empty;

    [Ip(Name = "clientIp")]
    public string ClientIp { get; set; } = string.Empty;

    [Text(Name = "userAgent")]
    public string UserAgent { get; set; } = string.Empty;

    [Date(Name = "createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [Text(Name = "allText", Index = false)]
    public string AllText { get; set; } = string.Empty;
}

