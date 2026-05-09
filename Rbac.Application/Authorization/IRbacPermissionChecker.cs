using Rbac.Application.Security;

namespace Rbac.Application.Authorization;

/// <summary>
/// 统一接口鉴权契约。封装 Redis permset → NetCasbin → snapshot rebuild 的完整判断链路。
/// 业务 Controller / Filter 只调用此接口，不直接操作 Redis 或 Casbin。
/// </summary>
public interface IRbacPermissionChecker
{
    /// <summary>
    /// 判断当前请求上下文是否具备指定权限。
    /// </summary>
    Task<PermissionCheckResult> CheckAsync(
        PermissionCheckRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// 鉴权请求入参。
/// </summary>
public sealed class PermissionCheckRequest
{
    /// <summary>已校验的 RBAC 上下文（来自 ICurrentRbacContextAccessor）。</summary>
    public required CurrentRbacContext Context { get; init; }

    /// <summary>
    /// 目标权限码。格式：{resourceType}:{scope}，例如 api:system.user.create。
    /// 由 IRbacApiPermissionMapper 从当前路由解析得到。
    /// </summary>
    public required string PermissionCode { get; init; }

    /// <summary>
    /// 操作类型：read / create / update / delete / execute / access。
    /// 由 IRbacApiPermissionMapper 从当前路由解析得到。
    /// </summary>
    public required string Action { get; init; }
}

/// <summary>
/// 鉴权判断结果。
/// </summary>
public sealed class PermissionCheckResult
{
    /// <summary>是否允许。</summary>
    public bool IsAllowed { get; init; }

    /// <summary>判断来源，用于审计和指标。</summary>
    public PermissionCheckSource Source { get; init; }

    /// <summary>deny 或 error 时的原因描述。</summary>
    public string? Reason { get; init; }

    public static PermissionCheckResult Allow(PermissionCheckSource source) =>
        new() { IsAllowed = true, Source = source };

    public static PermissionCheckResult Deny(PermissionCheckSource source, string reason) =>
        new() { IsAllowed = false, Source = source, Reason = reason };
}

/// <summary>
/// 鉴权判断来源枚举，用于指标统计和审计。
/// </summary>
public enum PermissionCheckSource
{
    /// <summary>project-scoped super，直接放行。</summary>
    ProjectSuper,

    /// <summary>Redis permset SISMEMBER 命中。</summary>
    RedisPermset,

    /// <summary>Redis 未命中，由 NetCasbin Enforce 判断。</summary>
    NetCasbin,

    /// <summary>缓存版本过期，重建 permset 后再次判断。</summary>
    PermsetRebuilt,

    /// <summary>全部来源均不可用时的降级拒绝。</summary>
    Fallback,
}
