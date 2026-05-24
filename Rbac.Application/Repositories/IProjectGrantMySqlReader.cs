using Rbac.Application.Security;

namespace Rbac.Application.Repositories;

/// <summary>
/// 用户所有 project 授权的内存映射。
/// 由 IProjectGrantMySqlReader 和 RbacProjectGrantCache 共同使用。
/// </summary>
public sealed class UserProjectGrantMap
{
    public Dictionary<string, ProjectGrantInfo> Projects { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 从 MySQL 读取用户 project 授权数据的契约。
/// 定义在 Application 层，由 Rbac.Infrastructure.DM 实现。
/// Rbac.Infrastructure.Redis 的 RbacProjectGrantCache 依赖此接口做 MySQL 兜底。
/// </summary>
public interface IProjectGrantMySqlReader
{
    /// <summary>
    /// 读取 userid 的全部 project 授权（含 super 标志和 policyVersion）。
    /// 返回 null 表示用户不存在或已禁用。
    /// </summary>
    Task<UserProjectGrantMap?> GetUserGrantsAsync(string userid, CancellationToken ct = default);
}
