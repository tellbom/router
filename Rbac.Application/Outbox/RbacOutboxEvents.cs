namespace Rbac.Application.Outbox;

/// <summary>
/// Outbox 事件类型常量。与数据库 eventType 字段值一一对应。
/// </summary>
public static class RbacOutboxEventTypes
{
    public const string UserChanged = "UserChanged";
    public const string GroupChanged = "GroupChanged";
    public const string MenuChanged = "MenuChanged";
    public const string PolicyChanged = "PolicyChanged";
    public const string ProjectGrantChanged = "ProjectGrantChanged";
    public const string ApiMapChanged = "ApiMapChanged";
}

/// <summary>
/// Outbox 事件包装体。写入 MySQL outbox 表时序列化 Payload 字段。
/// 写 MySQL 与写 Outbox 必须同一事务，Worker 消费后驱动 Redis/ES/Casbin 同步。
/// </summary>
public sealed class RbacOutboxEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");
    public string EventType { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;

    /// <summary>可选，用户级事件时填写。</summary>
    public string? Userid { get; init; }

    /// <summary>可选，组级事件时填写。</summary>
    public string? GroupCode { get; init; }

    /// <summary>序列化的具体 payload（JSON）。</summary>
    public string Payload { get; init; } = string.Empty;

    /// <summary>状态：Pending / Processing / Succeeded / Failed。</summary>
    public string Status { get; init; } = OutboxStatus.Pending;

    public int RetryCount { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Outbox 处理状态常量。</summary>
public static class OutboxStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

// ── Payload 定义（每种 eventType 固定字段，处理器不得自行推断缺失字段） ──────

/// <summary>
/// UserChanged payload。用户基础信息、状态、权限组变更时使用。
/// Redis 处理器读：userid, project, affectedGroupCodes。
/// ES 处理器读：userid, userGuid。
/// </summary>
public sealed class UserChangedPayload
{
    public string Userid { get; init; } = string.Empty;
    public string UserGuid { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;

    /// <summary>变更的字段名列表，例如 ["status", "username"]。</summary>
    public IReadOnlyList<string> ChangedFields { get; init; } = Array.Empty<string>();

    public string? OldStatus { get; init; }
    public string? NewStatus { get; init; }

    /// <summary>受影响的权限组编码列表。</summary>
    public IReadOnlyList<string> AffectedGroupCodes { get; init; } = Array.Empty<string>();

    public string? Reason { get; init; }
    public string OperatorUserid { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// GroupChanged payload。权限组规则、权限码变更时使用。
/// Redis 处理器读：project, groupCode, affectedUserids。
/// ES 处理器读：groupCode, groupGuid。
/// Casbin 处理器读：project, groupCode, newPermissionCodes。
/// </summary>
public sealed class GroupChangedPayload
{
    public string GroupCode { get; init; } = string.Empty;
    public string GroupGuid { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public IReadOnlyList<string> ChangedFields { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OldRuleCodes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> NewRuleCodes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OldPermissionCodes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> NewPermissionCodes { get; init; } = Array.Empty<string>();

    /// <summary>受影响的用户 ID 列表（该组下的用户）。</summary>
    public IReadOnlyList<string> AffectedUserids { get; init; } = Array.Empty<string>();

    public string OperatorUserid { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// MenuChanged payload。菜单规则新增/编辑/删除/排序/状态变更时使用。
/// Redis 处理器读：project。
/// ES 处理器读：ruleCode, ruleGuid, DxEId。
/// </summary>
public sealed class MenuChangedPayload
{
    public string RuleCode { get; init; } = string.Empty;
    public string RuleGuid { get; init; } = string.Empty;

    /// <summary>前端兼容 ID，必须为 string。</summary>
    public string DxEId { get; init; } = string.Empty;

    public string Project { get; init; } = string.Empty;

    /// <summary>变更类型：Created / Updated / Deleted / StatusChanged / Reordered。</summary>
    public string ChangeKind { get; init; } = string.Empty;

    public string? ParentRuleCode { get; init; }
    public string PermissionCode { get; init; } = string.Empty;
    public string? RoutePath { get; init; }
    public string? MenuType { get; init; }

    /// <summary>因此菜单变更而受影响的权限码列表。</summary>
    public IReadOnlyList<string> AffectedPermissionCodes { get; init; } = Array.Empty<string>();

    public string OperatorUserid { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// PolicyChanged payload。Casbin policy 变更（组-权限关系变更）时使用。
/// Casbin 处理器读：project, policyVersion, userid/groupCode, permissionCode, action。
/// Redis 处理器读：project, affectedUserids。
/// </summary>
public sealed class PolicyChangedPayload
{
    public string Project { get; init; } = string.Empty;
    public long PolicyVersion { get; init; }

    /// <summary>变更类型：Added / Removed。</summary>
    public string ChangeKind { get; init; } = string.Empty;

    /// <summary>策略主体类型：User / Group。</summary>
    public string SubjectType { get; init; } = string.Empty;

    public string? Userid { get; init; }
    public string? GroupCode { get; init; }
    public string PermissionCode { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public IReadOnlyList<string> AffectedUserids { get; init; } = Array.Empty<string>();
    public string OperatorUserid { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// ProjectGrantChanged payload。用户-project 授权关系变更时使用。
/// Redis 处理器读：userid, project（需立即删除 snapshot 和 permset）。
/// ES 处理器读：userid。
/// </summary>
public sealed class ProjectGrantChangedPayload
{
    public string Project { get; init; } = string.Empty;
    public string Userid { get; init; } = string.Empty;

    /// <summary>授权变更类型：Granted / Revoked / SuperGranted / SuperRevoked。</summary>
    public string GrantKind { get; init; } = string.Empty;

    public IReadOnlyList<string> OldProjects { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> NewProjects { get; init; } = Array.Empty<string>();
    public bool OldSuper { get; init; }
    public bool NewSuper { get; init; }
    public string OperatorUserid { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// ApiMapChanged payload。API route → permissionCode 映射变更时使用。
/// Redis 处理器读：project（删除 api-map 缓存，递增版本）。
/// ES 无需更新（api-map 不在 ES 索引中）。
/// </summary>
public sealed class ApiMapChangedPayload
{
    public string Project { get; init; } = string.Empty;
    public string HttpMethod { get; init; } = string.Empty;
    public string RoutePattern { get; init; } = string.Empty;
    public string? OldPermissionCode { get; init; }
    public string? NewPermissionCode { get; init; }
    public string? OldAction { get; init; }
    public string? NewAction { get; init; }

    /// <summary>变更类型：Created / Updated / Deleted。</summary>
    public string ChangeKind { get; init; } = string.Empty;

    public string OperatorUserid { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
