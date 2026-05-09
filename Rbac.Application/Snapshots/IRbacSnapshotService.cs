namespace Rbac.Application.Snapshots;

/// <summary>
/// 用户权限快照的读取和构建契约。
///
/// 快照内容包含：groups、super 标志、permissionCodes、ruleCodes、版本号集合。
/// 快照来源只能是 MySQL 真相库 + Casbin policy，不允许从 Redis permset 或 ES 反向生成。
///
/// 读取顺序：FusionCache L1 → Redis rbac:snapshot:{project}:{userid} → MySQL/Casbin 重建。
/// </summary>
public interface IRbacSnapshotService
{
    /// <summary>
    /// 获取用户权限快照。缓存未命中或版本过期时自动重建。
    /// </summary>
    Task<UserPermissionSnapshot?> GetSnapshotAsync(
        string userid,
        string project,
        CancellationToken ct = default);

    /// <summary>
    /// 强制从 MySQL/Casbin 重建并写入缓存。
    /// 重建前必须做 version compare-before-write，版本冲突时丢弃结果。
    /// </summary>
    Task<UserPermissionSnapshot?> RebuildSnapshotAsync(
        string userid,
        string project,
        CancellationToken ct = default);

    /// <summary>
    /// 主动删除用户快照缓存（super 变更、用户禁用、project 授权移除时调用）。
    /// </summary>
    Task InvalidateAsync(string userid, string project, CancellationToken ct = default);
}

/// <summary>
/// 用户权限快照。存储在 Redis rbac:snapshot:{project}:{userid}。
/// </summary>
public sealed class UserPermissionSnapshot
{
    public string Userid { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;

    /// <summary>用户所属权限组编码列表。</summary>
    public IReadOnlyList<string> Groups { get; init; } = Array.Empty<string>();

    /// <summary>该 project 下是否为超级管理员。</summary>
    public bool Super { get; init; }

    /// <summary>用户拥有的所有权限码（含所有组的合并结果）。</summary>
    public IReadOnlyList<string> PermissionCodes { get; init; } = Array.Empty<string>();

    /// <summary>用户拥有的所有规则码。</summary>
    public IReadOnlyList<string> RuleCodes { get; init; } = Array.Empty<string>();

    /// <summary>快照生成时的版本号集合，用于版本校验。</summary>
    public SnapshotVersions Versions { get; init; } = new();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 快照内嵌版本号，用于与 Redis 当前版本对比（version compare-before-write）。
/// </summary>
public sealed class SnapshotVersions
{
    /// <summary>对应 rbac:version:{project}。</summary>
    public long Project { get; init; }

    /// <summary>对应 rbac:version:{project}:{userid}。</summary>
    public long User { get; init; }

    /// <summary>对应 rbac:policy-version:{project}。</summary>
    public long Policy { get; init; }

    /// <summary>菜单版本（menu-tree 版本）。</summary>
    public long Menu { get; init; }
}
