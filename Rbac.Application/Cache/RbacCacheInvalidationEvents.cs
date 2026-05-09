namespace Rbac.Application.Cache;

/// <summary>
/// Redis Pub/Sub 缓存失效事件。
/// 发布到 <c>rbac.cache.invalidate</c> 频道，各 API 实例订阅后驱逐 FusionCache L1 本地缓存。
///
/// 设计原则：
/// - 缓存失效通过版本号懒失效为主，Pub/Sub 为辅（加速 L1 驱逐）。
/// - Pub/Sub 事件丢失时，请求会通过 version 校验发现 stale，最终一致。
/// - 不依赖扫描 10W 用户 key；按 project / userid / groupCode 精确失效。
/// </summary>
public sealed class RbacCacheInvalidationEvent
{
    /// <summary>幂等 ID，防止重复处理。</summary>
    public string EventId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>权限域。必填。</summary>
    public string Project { get; init; } = string.Empty;

    /// <summary>用户级失效时填写，不填则为 project 级失效。</summary>
    public string? Userid { get; init; }

    /// <summary>权限组级失效时填写。</summary>
    public string? GroupCode { get; init; }

    /// <summary>
    /// 资源类型，决定驱逐哪类 L1 缓存 key。
    /// 取值：menu / apiMap / policy / userProject / snapshot / all。
    /// </summary>
    public string ResourceType { get; init; } = CacheResourceType.All;

    /// <summary>触发失效的新版本号（供订阅端决策是否需要驱逐）。</summary>
    public long NewVersion { get; init; }

    /// <summary>请求链路 ID，用于日志追踪。</summary>
    public string TraceId { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>资源类型常量，对应 FusionCache L1 key 的驱逐范围。</summary>
public static class CacheResourceType
{
    /// <summary>菜单树（project 级）。</summary>
    public const string Menu = "menu";

    /// <summary>API 权限映射（project 级）。</summary>
    public const string ApiMap = "apiMap";

    /// <summary>Casbin policy 版本（project 级）。</summary>
    public const string Policy = "policy";

    /// <summary>用户-project 授权关系（user 级）。</summary>
    public const string UserProject = "userProject";

    /// <summary>用户权限快照（user 级）。</summary>
    public const string Snapshot = "snapshot";

    /// <summary>驱逐 project 或 user 下所有类型的缓存。</summary>
    public const string All = "all";
}

/// <summary>
/// 缓存失效接口，由 IRbacCacheInvalidator 实现。
/// 供 Outbox Worker 和直接写操作调用。
/// </summary>
public interface IRbacCacheInvalidator
{
    // ── 版本递增（懒失效主路径）──────────────────────────────────

    /// <summary>递增 project 全局权限版本 <c>rbac:version:{project}</c>。</summary>
    Task<long> IncrProjectVersionAsync(string project, CancellationToken ct = default);

    /// <summary>递增用户级权限版本 <c>rbac:version:{project}:{userid}</c>。</summary>
    Task<long> IncrUserVersionAsync(string project, string userid, CancellationToken ct = default);

    /// <summary>递增权限组版本 <c>rbac:version:{project}:group:{groupCode}</c>。</summary>
    Task<long> IncrGroupVersionAsync(string project, string groupCode, CancellationToken ct = default);

    /// <summary>递增 Casbin policy 版本 <c>rbac:policy-version:{project}</c>。</summary>
    Task<long> IncrPolicyVersionAsync(string project, CancellationToken ct = default);

    // ── 主动删除（高风险场景）────────────────────────────────────

    /// <summary>
    /// 主动删除用户快照和 permset（super 变更、用户禁用、project 授权移除时使用）。
    /// 高风险场景不能仅依赖版本懒失效。
    /// </summary>
    Task DeleteUserCacheAsync(string project, string userid, CancellationToken ct = default);

    /// <summary>删除 project 菜单树缓存。</summary>
    Task DeleteMenuTreeAsync(string project, CancellationToken ct = default);

    /// <summary>删除 project API 映射缓存。</summary>
    Task DeleteApiMapAsync(string project, CancellationToken ct = default);

    // ── Pub/Sub 发布（驱逐 L1）───────────────────────────────────

    /// <summary>发布缓存失效事件到 <c>rbac.cache.invalidate</c>，通知各实例驱逐 L1。</summary>
    Task PublishInvalidationAsync(RbacCacheInvalidationEvent evt, CancellationToken ct = default);
}
