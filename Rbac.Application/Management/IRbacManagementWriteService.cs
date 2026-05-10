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
/// 调用方（Controller / Application Service）职责：
/// - 编辑/删除前先通过 RbacManagementWriteGuard 从 MySQL 重新加载聚合根。
/// - 传入完整的 operatorUserid（不可为 null，写入审计）。
/// - 传入 changedFields / affectedUserids 等上下文字段（服务内不反查，避免多余查询）。
/// </summary>
public interface IRbacManagementWriteService
{
    // ── 1. 管理员 ────────────────────────────────────────────────

    /// <summary>
    /// 新增或更新管理员账号。
    /// 产生事件：UserChanged。
    /// </summary>
    /// <param name="admin">已构造好的管理员聚合根（调用方负责 Create / 加载后修改）。</param>
    /// <param name="changedFields">本次变更的字段名列表，例如 ["status", "username"]。</param>
    /// <param name="oldStatus">变更前状态（可选，首次创建时为 null）。</param>
    /// <param name="affectedGroupCodes">受影响的权限组编码（用户被移出/移入的组）。</param>
    /// <param name="operatorUserid">执行操作的管理员 userid，不可为 null。</param>
    Task SaveAdministratorAsync(
        RbacAdministrator admin,
        IReadOnlyList<string> changedFields,
        string? oldStatus,
        IReadOnlyList<string> affectedGroupCodes,
        string operatorUserid,
        CancellationToken ct = default);

    // ── 2. 权限组 ─────────────────────────────────────────────────

    /// <summary>
    /// 新增或更新权限组（名称、状态、ruleCodes、permissionCodes 变更）。
    /// 产生事件：GroupChanged；若 permissionCodes 变化额外产生 PolicyChanged。
    /// </summary>
    /// <param name="group">已构造好的权限组聚合根。</param>
    /// <param name="changedFields">变更字段列表。</param>
    /// <param name="oldRuleCodes">变更前 ruleCodes（首次创建时传空列表）。</param>
    /// <param name="oldPermissionCodes">变更前 permissionCodes（首次创建时传空列表）。</param>
    /// <param name="affectedUserids">该组下受影响的用户 ID 列表（调用方从 GroupMembers 查得）。</param>
    /// <param name="operatorUserid">执行操作的管理员 userid。</param>
    Task SaveGroupAsync(
        RbacGroup group,
        IReadOnlyList<string> changedFields,
        IReadOnlyList<string> oldRuleCodes,
        IReadOnlyList<string> oldPermissionCodes,
        IReadOnlyList<string> affectedUserids,
        string operatorUserid,
        CancellationToken ct = default);

    // ── 3. 规则/菜单 ──────────────────────────────────────────────

    /// <summary>
    /// 新增或更新规则（菜单/按钮）。
    /// 产生事件：MenuChanged。
    /// </summary>
    /// <param name="rule">已构造好的规则聚合根。</param>
    /// <param name="changeKind">Created / Updated / Deleted / StatusChanged / Reordered。</param>
    /// <param name="affectedPermissionCodes">因此菜单变更受影响的权限码列表。</param>
    /// <param name="operatorUserid">执行操作的管理员 userid。</param>
    Task SaveRuleAsync(
        RbacRule rule,
        string changeKind,
        IReadOnlyList<string> affectedPermissionCodes,
        string operatorUserid,
        CancellationToken ct = default);

    /// <summary>
    /// 删除规则。
    /// 产生事件：MenuChanged（changeKind = Deleted）。
    /// </summary>
    Task DeleteRuleAsync(
        RbacRule rule,
        IReadOnlyList<string> affectedPermissionCodes,
        string operatorUserid,
        CancellationToken ct = default);

    // ── 4. Project 授权 ───────────────────────────────────────────

    /// <summary>
    /// 新增或更新用户-project 授权（含 super 变更）。
    /// 产生事件：ProjectGrantChanged。
    /// </summary>
    /// <param name="grant">已构造好的 ProjectGrant 聚合根。</param>
    /// <param name="grantKind">Granted / Revoked / SuperGranted / SuperRevoked。</param>
    /// <param name="oldProjects">变更前该用户的 project 列表（首次授权时传空）。</param>
    /// <param name="newProjects">变更后该用户的 project 列表。</param>
    /// <param name="oldSuper">变更前是否 super。</param>
    /// <param name="operatorUserid">执行操作的管理员 userid。</param>
    Task SaveProjectGrantAsync(
        RbacProjectGrant grant,
        string grantKind,
        IReadOnlyList<string> oldProjects,
        IReadOnlyList<string> newProjects,
        bool oldSuper,
        string operatorUserid,
        CancellationToken ct = default);

    /// <summary>
    /// 撤销用户-project 授权（删除 grant 记录）。
    /// 产生事件：ProjectGrantChanged（grantKind = Revoked）。
    /// </summary>
    Task RevokeProjectGrantAsync(
        RbacProjectGrant grant,
        IReadOnlyList<string> remainingProjects,
        string operatorUserid,
        CancellationToken ct = default);

    // ── 5. API 权限映射 ───────────────────────────────────────────

    /// <summary>
    /// 新增或更新 API route → permissionCode 映射。
    /// 产生事件：ApiMapChanged。
    /// </summary>
    /// <param name="map">已构造好的 ApiPermissionMap 聚合根。</param>
    /// <param name="changeKind">Created / Updated / Deleted。</param>
    /// <param name="oldPermissionCode">变更前权限码（新增时为 null）。</param>
    /// <param name="oldAction">变更前 action（新增时为 null）。</param>
    /// <param name="operatorUserid">执行操作的管理员 userid。</param>
    Task SaveApiPermissionMapAsync(
        RbacApiPermissionMap map,
        string changeKind,
        string? oldPermissionCode,
        string? oldAction,
        string operatorUserid,
        CancellationToken ct = default);

    /// <summary>
    /// 删除 API 权限映射。
    /// 产生事件：ApiMapChanged（changeKind = Deleted）。
    /// </summary>
    Task DeleteApiPermissionMapAsync(
        RbacApiPermissionMap map,
        string operatorUserid,
        CancellationToken ct = default);

    // ── 6. 用户-组成员关系 ─────────────────────────────────────────

    /// <summary>
    /// 将用户加入权限组。
    /// 产生事件：PolicyChanged + GroupChanged（双事件，同一事务）。
    /// </summary>
    /// <param name="member">已构造好的 GroupMember 聚合根。</param>
    /// <param name="affectedUserids">此次变更受影响的用户列表（至少包含 member.Userid）。</param>
    /// <param name="groupPermissionCodes">该组当前的 permissionCodes（用于 PolicyChanged payload）。</param>
    /// <param name="operatorUserid">执行操作的管理员 userid。</param>
    Task SaveGroupMemberAsync(
        RbacGroupMember member,
        IReadOnlyList<string> affectedUserids,
        IReadOnlyList<string> groupPermissionCodes,
        string operatorUserid,
        CancellationToken ct = default);

    /// <summary>
    /// 将用户从权限组移除。
    /// 产生事件：PolicyChanged + GroupChanged（双事件，同一事务）。
    /// </summary>
    Task DeleteGroupMemberAsync(
        RbacGroupMember member,
        IReadOnlyList<string> affectedUserids,
        IReadOnlyList<string> groupPermissionCodes,
        string operatorUserid,
        CancellationToken ct = default);
}
