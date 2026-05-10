using Rbac.Domain.Groups;
using Rbac.Domain.Permissions;
using Rbac.Domain.Projects;
using Rbac.Domain.Rules;
using Rbac.Domain.Users;

namespace Rbac.Application.Management;

/// <summary>
/// 权限管理写操作服务契约。
///
/// 每个方法保证：
/// 1. 聚合根持久化到 MySQL 真相表。
/// 2. 对应 RbacOutboxEvent 追加到同一 DbContext（IOutboxWriter.Append）。
/// 3. 一次 SaveChangesAsync 同事务提交，两者要么同时成功要么同时回滚。
///
/// 调用方职责：
/// - 编辑/删除前先通过 RbacManagementWriteGuard 从 MySQL 重新加载聚合根。
/// - 传入完整的 operatorUserid（不可为 null）。
/// - 传入 changedFields / affectedUserids 等上下文字段（服务内不反查，除明确标注的方法）。
/// </summary>
public interface IRbacManagementWriteService
{
    // ── 1. 管理员 ────────────────────────────────────────────────

    /// <summary>新增或更新管理员账号。产生事件：UserChanged。</summary>
    Task SaveAdministratorAsync(
        RbacAdministrator admin,
        IReadOnlyList<string> changedFields,
        string? oldStatus,
        IReadOnlyList<string> affectedGroupCodes,
        string operatorUserid,
        CancellationToken ct = default);

    /// <summary>
    /// 物理删除管理员账号。
    /// 实现内部负责：① 删除 GroupMember 记录并为每条产生 PolicyChanged + GroupChanged；
    ///               ② 删除 Administrator 记录并产生 UserChanged。
    /// 调用方只需传入已从 MySQL 加载的聚合根和操作人。
    /// 产生事件：UserChanged + N×(PolicyChanged + GroupChanged)。
    /// </summary>
    Task DeleteAdministratorAsync(
        RbacAdministrator admin,
        string operatorUserid,
        CancellationToken ct = default);

    // ── 2. 权限组 ─────────────────────────────────────────────────

    /// <summary>
    /// 新增或更新权限组（名称、parentGroupCode、状态、ruleCodes、permissionCodes）。
    /// permissionCodes 变化时额外产生 PolicyChanged。
    /// 产生事件：GroupChanged [+ PolicyChanged×N]。
    /// </summary>
    Task SaveGroupAsync(
        RbacGroup group,
        IReadOnlyList<string> changedFields,
        IReadOnlyList<string> oldRuleCodes,
        IReadOnlyList<string> oldPermissionCodes,
        IReadOnlyList<string> affectedUserids,
        string operatorUserid,
        CancellationToken ct = default);

    /// <summary>
    /// 物理删除权限组。
    /// 实现内部负责：① 删除所有 GroupMember 并产生 PolicyChanged + GroupChanged；
    ///               ② 删除 Group 记录并产生最终 GroupChanged（changeKind=Deleted）。
    /// 调用方已在 Controller 完成前置校验（无子组、无关联成员、非操作人所属组）。
    /// 产生事件：GroupChanged(Deleted) + N×(PolicyChanged + GroupChanged)。
    /// </summary>
    Task DeleteGroupAsync(
        RbacGroup group,
        IReadOnlyList<string> affectedUserids,
        string operatorUserid,
        CancellationToken ct = default);

    // ── 3. 规则/菜单 ──────────────────────────────────────────────

    /// <summary>新增或更新规则。产生事件：MenuChanged。</summary>
    Task SaveRuleAsync(
        RbacRule rule,
        string changeKind,
        IReadOnlyList<string> affectedPermissionCodes,
        string operatorUserid,
        CancellationToken ct = default);

    /// <summary>删除规则（物理删除）。产生事件：MenuChanged(Deleted)。</summary>
    Task DeleteRuleAsync(
        RbacRule rule,
        IReadOnlyList<string> affectedPermissionCodes,
        string operatorUserid,
        CancellationToken ct = default);

    // ── 4. Project 授权 ───────────────────────────────────────────

    /// <summary>新增或更新 project 授权。产生事件：ProjectGrantChanged。</summary>
    Task SaveProjectGrantAsync(
        RbacProjectGrant grant,
        string grantKind,
        IReadOnlyList<string> oldProjects,
        IReadOnlyList<string> newProjects,
        bool oldSuper,
        string operatorUserid,
        CancellationToken ct = default);

    /// <summary>撤销 project 授权。产生事件：ProjectGrantChanged(Revoked)。</summary>
    Task RevokeProjectGrantAsync(
        RbacProjectGrant grant,
        IReadOnlyList<string> remainingProjects,
        string operatorUserid,
        CancellationToken ct = default);

    // ── 5. API 权限映射 ───────────────────────────────────────────

    /// <summary>新增或更新 API 路由映射。产生事件：ApiMapChanged。</summary>
    Task SaveApiPermissionMapAsync(
        RbacApiPermissionMap map,
        string changeKind,
        string? oldPermissionCode,
        string? oldAction,
        string operatorUserid,
        CancellationToken ct = default);

    /// <summary>删除 API 路由映射。产生事件：ApiMapChanged(Deleted)。</summary>
    Task DeleteApiPermissionMapAsync(
        RbacApiPermissionMap map,
        string operatorUserid,
        CancellationToken ct = default);

    // ── 6. 用户-组成员关系 ─────────────────────────────────────────

    /// <summary>将用户加入权限组。产生事件：PolicyChanged + GroupChanged。</summary>
    Task SaveGroupMemberAsync(
        RbacGroupMember member,
        IReadOnlyList<string> affectedUserids,
        IReadOnlyList<string> groupPermissionCodes,
        string operatorUserid,
        CancellationToken ct = default);

    /// <summary>将用户从权限组移除。产生事件：PolicyChanged + GroupChanged。</summary>
    Task DeleteGroupMemberAsync(
        RbacGroupMember member,
        IReadOnlyList<string> affectedUserids,
        IReadOnlyList<string> groupPermissionCodes,
        string operatorUserid,
        CancellationToken ct = default);
}
