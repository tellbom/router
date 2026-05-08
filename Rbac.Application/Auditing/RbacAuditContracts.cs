namespace Rbac.Application.Auditing;

/// <summary>
/// 审计事件基类。所有审计事件必须继承此类。
/// </summary>
public abstract class RbacAuditEvent
{
    /// <summary>审计事件唯一 ID（幂等）。</summary>
    public string AuditId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>请求链路追踪 ID。</summary>
    public string TraceId { get; init; } = string.Empty;

    /// <summary>操作用户 ID。</summary>
    public string Userid { get; init; } = string.Empty;

    /// <summary>已验证的项目标识。</summary>
    public string Project { get; init; } = string.Empty;

    /// <summary>事件发生时间（UTC）。</summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

// ── 1. 鉴权审计事件 ──────────────────────────────────────────────

/// <summary>
/// 接口鉴权结果审计事件。allow / deny / error 均需记录。
/// 热路径产生此事件后必须异步写入，不阻塞主请求。
/// </summary>
public sealed class AuthorizationAuditEvent : RbacAuditEvent
{
    public string PermissionCode { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;

    /// <summary>鉴权结果：allow / deny / error。</summary>
    public string Result { get; init; } = string.Empty;

    /// <summary>deny 或 error 时的原因描述。</summary>
    public string Reason { get; init; } = string.Empty;

    public string ApiPath { get; init; } = string.Empty;
    public string HttpMethod { get; init; } = string.Empty;
    public string ClientIp { get; init; } = string.Empty;

    /// <summary>前端原始传入的 project（校验前），用于检测 project 伪造。</summary>
    public string RequestedProject { get; init; } = string.Empty;
}

// ── 2. 管理写入审计事件 ──────────────────────────────────────────

/// <summary>
/// 管理端写入操作审计事件（新增/编辑/删除/启停/授权变更）。
/// </summary>
public sealed class ManagementWriteAuditEvent : RbacAuditEvent
{
    /// <summary>操作类型：Create / Update / Delete / Enable / Disable / GrantProject 等。</summary>
    public string OperationType { get; init; } = string.Empty;

    /// <summary>操作对象类型：User / Group / Rule / ProjectGrant / ApiMap / Policy。</summary>
    public string ResourceType { get; init; } = string.Empty;

    /// <summary>操作对象的内部 Guid。</summary>
    public string ResourceId { get; init; } = string.Empty;

    /// <summary>变更摘要（非完整 diff，仅关键字段）。</summary>
    public string ChangeSummary { get; init; } = string.Empty;
}

// ── 3. 缓存失效审计事件 ──────────────────────────────────────────

/// <summary>
/// 缓存失效操作审计事件，用于追踪 Redis key 删除和版本递增。
/// </summary>
public sealed class CacheInvalidationAuditEvent : RbacAuditEvent
{
    public string ResourceType { get; init; } = string.Empty;
    public string AffectedKey { get; init; } = string.Empty;
    public long NewVersion { get; init; }
}

// ── 4. ES 同步审计事件 ───────────────────────────────────────────

/// <summary>
/// ES 索引同步审计事件（Outbox 增量或全量重建）。
/// </summary>
public sealed class EsSyncAuditEvent : RbacAuditEvent
{
    public string IndexName { get; init; } = string.Empty;

    /// <summary>同步类型：Incremental / FullReindex。</summary>
    public string SyncType { get; init; } = string.Empty;

    /// <summary>同步结果：Succeeded / Failed。</summary>
    public string Result { get; init; } = string.Empty;

    public string? FailureReason { get; init; }
    public int DocumentCount { get; init; }
}

// ── 5. Casbin policy 同步审计事件 ────────────────────────────────

/// <summary>
/// Casbin Enforcer reload 审计事件。
/// reload 成功、失败、耗时、版本变更均必须记录。
/// </summary>
public sealed class CasbinReloadAuditEvent : RbacAuditEvent
{
    public long OldPolicyVersion { get; init; }
    public long NewPolicyVersion { get; init; }

    /// <summary>reload 结果：Succeeded / Failed。</summary>
    public string Result { get; init; } = string.Empty;

    public string? FailureReason { get; init; }

    /// <summary>reload 耗时（毫秒）。</summary>
    public long ElapsedMs { get; init; }
}
