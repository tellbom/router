using Rbac.Application.Security;

namespace Rbac.Application.Authorization;

/// <summary>
/// 統一接口鉴权契约。
/// </summary>
public interface IRbacPermissionChecker
{
    Task<PermissionCheckResult> CheckAsync(
        PermissionCheckRequest request,
        CancellationToken ct = default);
}

/// <summary>鉴权请求入参。</summary>
public sealed class PermissionCheckRequest
{
    public CurrentRbacContext Context { get; init; } = new();
    public string PermissionCode { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
}

/// <summary>鉴权判断结果。</summary>
public sealed class PermissionCheckResult
{
    public bool IsAllowed { get; init; }
    public PermissionCheckSource Source { get; init; }
    public string? Reason { get; init; }

    public static PermissionCheckResult Allow(PermissionCheckSource source) =>
        new() { IsAllowed = true, Source = source };

    public static PermissionCheckResult Deny(PermissionCheckSource source, string reason) =>
        new() { IsAllowed = false, Source = source, Reason = reason };
}

/// <summary>鉴权判断来源枚举。</summary>
public enum PermissionCheckSource
{
    ProjectSuper,
    RedisPermset,
    NetCasbin,
    PermsetRebuilt,
    Fallback,
}
