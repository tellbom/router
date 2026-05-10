using Rbac.Domain.Groups;
using Rbac.Domain.Permissions;
using Rbac.Domain.Projects;
using Rbac.Domain.Rules;
using Rbac.Domain.Users;
using Rbac.Domain.ValueObjects;

namespace Rbac.Application.Repositories;

// ── 管理员 ────────────────────────────────────────────────────────

/// <summary>管理员聚合根仓储接口。</summary>
public interface IAdministratorRepository
{
    Task<RbacAdministrator?> FindByGuidAsync(Guid id, CancellationToken ct = default);
    Task<RbacAdministrator?> FindByUseridAsync(UserId userid, CancellationToken ct = default);
    Task<RbacAdministrator?> FindByDxEIdAsync(DxEId dxeId, CancellationToken ct = default);
    Task<IReadOnlyList<RbacAdministrator>> FindByProjectAsync(ProjectCode project, CancellationToken ct = default);
    Task SaveAsync(RbacAdministrator admin, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

// ── 权限组 ────────────────────────────────────────────────────────

/// <summary>权限组聚合根仓储接口。</summary>
public interface IGroupRepository
{
    Task<RbacGroup?> FindByGuidAsync(Guid id, CancellationToken ct = default);
    Task<RbacGroup?> FindByGroupCodeAsync(GroupCode groupCode, ProjectCode project, CancellationToken ct = default);
    Task<RbacGroup?> FindByDxEIdAsync(DxEId dxeId, CancellationToken ct = default);
    Task<IReadOnlyList<RbacGroup>> FindByProjectAsync(ProjectCode project, CancellationToken ct = default);
    Task SaveAsync(RbacGroup group, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>用户-权限组关系仓储接口。</summary>
public interface IGroupMemberRepository
{
    Task<RbacGroupMember?> FindAsync(
        UserId userid,
        GroupCode groupCode,
        ProjectCode project,
        CancellationToken ct = default);

    Task<IReadOnlyList<RbacGroupMember>> FindByGroupAsync(
        GroupCode groupCode,
        ProjectCode project,
        CancellationToken ct = default);

    Task<IReadOnlyList<RbacGroupMember>> FindByUseridAsync(
        UserId userid,
        ProjectCode project,
        CancellationToken ct = default);
}

// ── 菜单/按钮规则 ─────────────────────────────────────────────────

/// <summary>规则聚合根仓储接口。</summary>
public interface IRuleRepository
{
    Task<RbacRule?> FindByGuidAsync(Guid id, CancellationToken ct = default);
    Task<RbacRule?> FindByRuleCodeAsync(RuleCode ruleCode, ProjectCode project, CancellationToken ct = default);
    Task<RbacRule?> FindByDxEIdAsync(DxEId dxeId, CancellationToken ct = default);

    /// <summary>获取 project 下所有启用的规则，用于构建菜单树。</summary>
    Task<IReadOnlyList<RbacRule>> FindActiveByProjectAsync(ProjectCode project, CancellationToken ct = default);

    /// <summary>获取指定权限组拥有的规则。</summary>
    Task<IReadOnlyList<RbacRule>> FindByGroupCodeAsync(GroupCode groupCode, ProjectCode project, CancellationToken ct = default);

    Task SaveAsync(RbacRule rule, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

// ── Project 授权 ──────────────────────────────────────────────────

/// <summary>Project 授权聚合根仓储接口。</summary>
public interface IProjectGrantRepository
{
    Task<RbacProjectGrant?> FindAsync(UserId userid, ProjectCode project, CancellationToken ct = default);

    /// <summary>获取 userid 拥有授权的所有 project 列表。</summary>
    Task<IReadOnlyList<RbacProjectGrant>> FindByUseridAsync(UserId userid, CancellationToken ct = default);

    /// <summary>获取 project 下所有授权用户。</summary>
    Task<IReadOnlyList<RbacProjectGrant>> FindByProjectAsync(ProjectCode project, CancellationToken ct = default);

    Task SaveAsync(RbacProjectGrant grant, CancellationToken ct = default);
    Task DeleteAsync(UserId userid, ProjectCode project, CancellationToken ct = default);
}

// ── API 权限映射 ──────────────────────────────────────────────────

/// <summary>API 权限映射聚合根仓储接口。</summary>
public interface IApiPermissionMapRepository
{
    Task<RbacApiPermissionMap?> FindByGuidAsync(Guid id, CancellationToken ct = default);

    /// <summary>获取 project 下所有启用的 API 映射，用于运行时路由匹配缓存。</summary>
    Task<IReadOnlyList<RbacApiPermissionMap>> FindActiveByProjectAsync(ProjectCode project, CancellationToken ct = default);

    Task SaveAsync(RbacApiPermissionMap map, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

// ── Casbin Policy ─────────────────────────────────────────────────

/// <summary>
/// Casbin policy 读取接口。
/// 提供 g（用户-组关系）和 p（组-权限码-action）两类策略的 MySQL 真相读取。
/// 由 Rbac.Infrastructure.MySql 实现，供 Rbac.Infrastructure.Casbin 使用。
/// </summary>
public interface ICasbinPolicyRepository
{
    /// <summary>
    /// 读取 project 下的 g policy（用户-组关系）。
    /// 返回格式：(userid, groupCode, project) 三元组列表。
    /// </summary>
    Task<IReadOnlyList<(string Userid, string GroupCode, string Project)>> GetGroupingPoliciesAsync(
        ProjectCode project, CancellationToken ct = default);

    /// <summary>
    /// 读取 project 下的 p policy（组-权限码-action）。
    /// 返回格式：(groupCode, project, permissionCode, action) 四元组列表。
    /// </summary>
    Task<IReadOnlyList<(string GroupCode, string Project, string PermissionCode, string Action)>> GetPermissionPoliciesAsync(
        ProjectCode project, CancellationToken ct = default);
}
