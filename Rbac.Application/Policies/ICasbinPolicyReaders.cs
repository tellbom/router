using Rbac.Domain.ValueObjects;

namespace Rbac.Application.Policies;

/// <summary>
/// T093: 从 MySQL 真相表加载 g policy（用户-组关系）的契约。
/// 由 Rbac.Infrastructure.DM 实现，供 CasbinEnforcerFactory 调用。
/// 禁止从 Redis permset 或 ES 反向加载。
/// </summary>
public interface ICasbinGroupingPolicyReader
{
    /// <summary>
    /// 读取 project 下的所有 g policy（用户-权限组三元组）。
    /// 返回格式：(userid, groupCode, project)。
    /// </summary>
    Task<IReadOnlyList<(string Userid, string GroupCode, string Project)>> LoadAsync(
        ProjectCode project, CancellationToken ct = default);
}

/// <summary>
/// T094: 从 MySQL 真相表加载 p policy（组-权限码-action）的契约。
/// 由 Rbac.Infrastructure.DM 实现，供 CasbinEnforcerFactory 调用。
/// 禁止从 Redis permset 或 ES 反向加载。
/// </summary>
public interface ICasbinPermissionPolicyReader
{
    /// <summary>
    /// 读取 project 下的所有 p policy（组-权限码-action 四元组）。
    /// 返回格式：(groupCode, project, permissionCode, action)。
    /// </summary>
    Task<IReadOnlyList<(string GroupCode, string Project, string PermissionCode, string Action)>> LoadAsync(
        ProjectCode project, CancellationToken ct = default);
}
