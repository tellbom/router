using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Rbac.Application.Management;
using Rbac.Application.Outbox;
using Rbac.Domain.Groups;
using Rbac.Domain.Permissions;
using Rbac.Domain.Projects;
using Rbac.Domain.Rules;
using Rbac.Domain.Users;
using Rbac.Infrastructure.MySql.Mapping;
using Rbac.Infrastructure.MySql.Outbox;

namespace Rbac.Infrastructure.MySql.Management;

/// <summary>
/// IRbacManagementWriteService 的 EF Core 实现。
///
/// 核心事务保证：
///   每个方法内，业务实体写入 + IOutboxWriter.Append 均在同一个 RbacDbContext 内完成，
///   由最后一次 SaveChangesAsync 原子提交。
///   IOutboxWriter.Append 是 void（仅追加到变更跟踪），不单独 SaveChanges。
///
/// 约束：
///   - 不从 Redis / ES 读取任何数据。
///   - Payload 字段全部由调用方传入，不在服务内反查（除必要的 EF 状态判断）。
///   - 禁止从 rbac_project_grant × rbac_group 做笛卡尔积推导。
/// </summary>
public sealed class RbacManagementWriteService : IRbacManagementWriteService
{
    private readonly RbacDbContext _db;
    private readonly IOutboxWriter _outbox;
    private readonly ILogger<RbacManagementWriteService> _logger;

    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public RbacManagementWriteService(
        RbacDbContext db,
        IOutboxWriter outbox,
        ILogger<RbacManagementWriteService> logger)
    {
        _db     = db;
        _outbox = outbox;
        _logger = logger;
    }

    // ── 1. 管理员 ────────────────────────────────────────────────

    public async Task SaveAdministratorAsync(
        RbacAdministrator admin,
        IReadOnlyList<string> changedFields,
        string? oldStatus,
        IReadOnlyList<string> affectedGroupCodes,
        string operatorUserid,
        CancellationToken ct = default)
    {
        ValidateOperator(operatorUserid);

        // 新增 or 更新（按 Guid 判断是否已被 EF 跟踪）
        UpsertEntity(_db.Administrators, admin);

        // 构造 Outbox 事件
        var payload = new UserChangedPayload
        {
            Userid             = admin.Userid.Value,
            UserGuid           = admin.Id.ToString(),
            Project            = string.Empty, // 管理员不绑定单一 project
            ChangedFields      = changedFields,
            OldStatus          = oldStatus,
            NewStatus          = admin.Status.ToString(),
            AffectedGroupCodes = affectedGroupCodes,
            OperatorUserid     = operatorUserid,
        };

        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.UserChanged,
            Project   = string.Empty,
            Userid    = admin.Userid.Value,
            Payload   = Serialize(payload),
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SaveAdministrator userid={U} changedFields={F} operator={Op}",
            admin.Userid.Value, string.Join(",", changedFields), operatorUserid);
    }

    // ── 2. 权限组 ─────────────────────────────────────────────────

    public async Task SaveGroupAsync(
        RbacGroup group,
        IReadOnlyList<string> changedFields,
        IReadOnlyList<string> oldRuleCodes,
        IReadOnlyList<string> oldPermissionCodes,
        IReadOnlyList<string> affectedUserids,
        string operatorUserid,
        CancellationToken ct = default)
    {
        ValidateOperator(operatorUserid);

        UpsertEntity(_db.Groups, group);

        var newPermCodes = group.PermissionCodes.Select(p => p.Value).ToList();
        var newRuleCodes = group.RuleCodes.Select(r => r.Value).ToList();

        // GroupChanged 事件（必须产生）
        var groupPayload = new GroupChangedPayload
        {
            GroupCode         = group.GroupCode.Value,
            GroupGuid         = group.Id.ToString(),
            Project           = group.Project.Value,
            ChangedFields     = changedFields,
            OldRuleCodes      = oldRuleCodes,
            NewRuleCodes      = newRuleCodes,
            OldPermissionCodes = oldPermissionCodes,
            NewPermissionCodes = newPermCodes,
            AffectedUserids   = affectedUserids,
            OperatorUserid    = operatorUserid,
        };

        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.GroupChanged,
            Project   = group.Project.Value,
            GroupCode = group.GroupCode.Value,
            Payload   = Serialize(groupPayload),
        });

        // PolicyChanged 事件（permissionCodes 有变化时额外产生，驱动 Casbin reload）
        var permCodesChanged = !oldPermissionCodes
            .OrderBy(x => x)
            .SequenceEqual(newPermCodes.OrderBy(x => x));

        if (permCodesChanged)
        {
            // 每个新增的权限码产生一条 Added 策略记录
            foreach (var addedPerm in newPermCodes.Except(oldPermissionCodes, StringComparer.OrdinalIgnoreCase))
            {
                AppendPolicyChangedEvent(
                    group.Project.Value, group.GroupCode.Value,
                    addedPerm, action: "access",
                    changeKind: "Added", subjectType: "Group",
                    affectedUserids, operatorUserid);
            }

            // 每个移除的权限码产生一条 Removed 策略记录
            foreach (var removedPerm in oldPermissionCodes.Except(newPermCodes, StringComparer.OrdinalIgnoreCase))
            {
                AppendPolicyChangedEvent(
                    group.Project.Value, group.GroupCode.Value,
                    removedPerm, action: "access",
                    changeKind: "Removed", subjectType: "Group",
                    affectedUserids, operatorUserid);
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SaveGroup groupCode={G} project={P} permCodesChanged={C} operator={Op}",
            group.GroupCode.Value, group.Project.Value, permCodesChanged, operatorUserid);
    }

    // ── 3. 规则/菜单 ──────────────────────────────────────────────

    public async Task SaveRuleAsync(
        RbacRule rule,
        string changeKind,
        IReadOnlyList<string> affectedPermissionCodes,
        string operatorUserid,
        CancellationToken ct = default)
    {
        ValidateOperator(operatorUserid);

        UpsertEntity(_db.Rules, rule);

        _outbox.Append(BuildMenuChangedEvent(rule, changeKind, affectedPermissionCodes, operatorUserid));

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SaveRule ruleCode={R} project={P} changeKind={K} operator={Op}",
            rule.RuleCode.Value, rule.Project.Value, changeKind, operatorUserid);
    }

    public async Task DeleteRuleAsync(
        RbacRule rule,
        IReadOnlyList<string> affectedPermissionCodes,
        string operatorUserid,
        CancellationToken ct = default)
    {
        ValidateOperator(operatorUserid);

        // 软删除：由领域模型 Disable 控制状态；硬删除时直接 Remove
        _db.Rules.Remove(rule);

        _outbox.Append(BuildMenuChangedEvent(rule, "Deleted", affectedPermissionCodes, operatorUserid));

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "DeleteRule ruleCode={R} project={P} operator={Op}",
            rule.RuleCode.Value, rule.Project.Value, operatorUserid);
    }

    // ── 4. Project 授权 ───────────────────────────────────────────

    public async Task SaveProjectGrantAsync(
        RbacProjectGrant grant,
        string grantKind,
        IReadOnlyList<string> oldProjects,
        IReadOnlyList<string> newProjects,
        bool oldSuper,
        string operatorUserid,
        CancellationToken ct = default)
    {
        ValidateOperator(operatorUserid);

        UpsertEntity(_db.ProjectGrants, grant);

        var payload = new ProjectGrantChangedPayload
        {
            Project        = grant.Project.Value,
            Userid         = grant.Userid.Value,
            GrantKind      = grantKind,
            OldProjects    = oldProjects,
            NewProjects    = newProjects,
            OldSuper       = oldSuper,
            NewSuper       = grant.IsSuper,
            OperatorUserid = operatorUserid,
        };

        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.ProjectGrantChanged,
            Project   = grant.Project.Value,
            Userid    = grant.Userid.Value,
            Payload   = Serialize(payload),
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SaveProjectGrant userid={U} project={P} grantKind={K} operator={Op}",
            grant.Userid.Value, grant.Project.Value, grantKind, operatorUserid);
    }

    public async Task RevokeProjectGrantAsync(
        RbacProjectGrant grant,
        IReadOnlyList<string> remainingProjects,
        string operatorUserid,
        CancellationToken ct = default)
    {
        ValidateOperator(operatorUserid);

        _db.ProjectGrants.Remove(grant);

        var payload = new ProjectGrantChangedPayload
        {
            Project        = grant.Project.Value,
            Userid         = grant.Userid.Value,
            GrantKind      = "Revoked",
            OldProjects    = new[] { grant.Project.Value },
            NewProjects    = remainingProjects,
            OldSuper       = grant.IsSuper,
            NewSuper       = false,
            OperatorUserid = operatorUserid,
        };

        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.ProjectGrantChanged,
            Project   = grant.Project.Value,
            Userid    = grant.Userid.Value,
            Payload   = Serialize(payload),
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "RevokeProjectGrant userid={U} project={P} operator={Op}",
            grant.Userid.Value, grant.Project.Value, operatorUserid);
    }

    // ── 5. API 权限映射 ───────────────────────────────────────────

    public async Task SaveApiPermissionMapAsync(
        RbacApiPermissionMap map,
        string changeKind,
        string? oldPermissionCode,
        string? oldAction,
        string operatorUserid,
        CancellationToken ct = default)
    {
        ValidateOperator(operatorUserid);

        UpsertEntity(_db.ApiPermissionMaps, map);

        _outbox.Append(BuildApiMapChangedEvent(
            map, changeKind, oldPermissionCode, oldAction, operatorUserid));

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SaveApiPermissionMap project={P} method={M} route={R} changeKind={K} operator={Op}",
            map.Project.Value, map.HttpMethod, map.RoutePattern, changeKind, operatorUserid);
    }

    public async Task DeleteApiPermissionMapAsync(
        RbacApiPermissionMap map,
        string operatorUserid,
        CancellationToken ct = default)
    {
        ValidateOperator(operatorUserid);

        _db.ApiPermissionMaps.Remove(map);

        _outbox.Append(BuildApiMapChangedEvent(
            map, "Deleted",
            oldPermissionCode: map.PermissionCode.Value,
            oldAction: map.Action,
            operatorUserid));

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "DeleteApiPermissionMap project={P} method={M} route={R} operator={Op}",
            map.Project.Value, map.HttpMethod, map.RoutePattern, operatorUserid);
    }

    // ── 6. 用户-组成员关系 ─────────────────────────────────────────

    public async Task SaveGroupMemberAsync(
        RbacGroupMember member,
        IReadOnlyList<string> affectedUserids,
        IReadOnlyList<string> groupPermissionCodes,
        string operatorUserid,
        CancellationToken ct = default)
    {
        ValidateOperator(operatorUserid);

        UpsertEntity(_db.GroupMembers, member);

        // PolicyChanged：驱动 Casbin 加载新 g policy（用户被加入组）
        AppendPolicyChangedEvent(
            member.Project.Value, member.GroupCode.Value,
            permissionCode: string.Join(",", groupPermissionCodes),
            action: "access", changeKind: "Added", subjectType: "User",
            affectedUserids, operatorUserid,
            userid: member.Userid.Value);

        // GroupChanged：驱动受影响用户的 permset 失效
        var groupPayload = new GroupChangedPayload
        {
            GroupCode         = member.GroupCode.Value,
            GroupGuid         = string.Empty, // 调用方可补充；此处不反查 group
            Project           = member.Project.Value,
            ChangedFields     = new[] { "members" },
            OldRuleCodes      = Array.Empty<string>(),
            NewRuleCodes      = Array.Empty<string>(),
            OldPermissionCodes = Array.Empty<string>(),
            NewPermissionCodes = groupPermissionCodes,
            AffectedUserids   = affectedUserids,
            OperatorUserid    = operatorUserid,
        };

        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.GroupChanged,
            Project   = member.Project.Value,
            GroupCode = member.GroupCode.Value,
            Payload   = Serialize(groupPayload),
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "SaveGroupMember userid={U} groupCode={G} project={P} operator={Op}",
            member.Userid.Value, member.GroupCode.Value, member.Project.Value, operatorUserid);
    }

    public async Task DeleteGroupMemberAsync(
        RbacGroupMember member,
        IReadOnlyList<string> affectedUserids,
        IReadOnlyList<string> groupPermissionCodes,
        string operatorUserid,
        CancellationToken ct = default)
    {
        ValidateOperator(operatorUserid);

        _db.GroupMembers.Remove(member);

        // PolicyChanged：驱动 Casbin 移除 g policy（用户被移出组）
        AppendPolicyChangedEvent(
            member.Project.Value, member.GroupCode.Value,
            permissionCode: string.Join(",", groupPermissionCodes),
            action: "access", changeKind: "Removed", subjectType: "User",
            affectedUserids, operatorUserid,
            userid: member.Userid.Value);

        // GroupChanged：驱动受影响用户的 permset 失效
        var groupPayload = new GroupChangedPayload
        {
            GroupCode         = member.GroupCode.Value,
            GroupGuid         = string.Empty,
            Project           = member.Project.Value,
            ChangedFields     = new[] { "members" },
            OldRuleCodes      = Array.Empty<string>(),
            NewRuleCodes      = Array.Empty<string>(),
            OldPermissionCodes = groupPermissionCodes,
            NewPermissionCodes = Array.Empty<string>(),
            AffectedUserids   = affectedUserids,
            OperatorUserid    = operatorUserid,
        };

        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.GroupChanged,
            Project   = member.Project.Value,
            GroupCode = member.GroupCode.Value,
            Payload   = Serialize(groupPayload),
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "DeleteGroupMember userid={U} groupCode={G} project={P} operator={Op}",
            member.Userid.Value, member.GroupCode.Value, member.Project.Value, operatorUserid);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    /// <summary>
    /// Add（新实体）或 Update（已有实体）。
    /// 按 EF Core EntityState 判断：Detached 表示新实体，否则已跟踪直接更新。
    /// </summary>
    private void UpsertEntity<T>(DbSet<T> set, T entity) where T : class
    {
        if (_db.Entry(entity).State == EntityState.Detached)
            set.Add(entity);
        else
            set.Update(entity);
    }

    private static void ValidateOperator(string operatorUserid)
    {
        if (string.IsNullOrWhiteSpace(operatorUserid))
            throw new ArgumentException(
                "operatorUserid is required for audit trail.", nameof(operatorUserid));
    }

    private static string Serialize<T>(T payload) =>
        JsonSerializer.Serialize(payload, _json);

    /// <summary>构造 MenuChanged Outbox 事件。</summary>
    private static RbacOutboxEvent BuildMenuChangedEvent(
        RbacRule rule, string changeKind,
        IReadOnlyList<string> affectedPermissionCodes,
        string operatorUserid)
    {
        var payload = new MenuChangedPayload
        {
            RuleCode                = rule.RuleCode.Value,
            RuleGuid                = rule.Id.ToString(),
            DxEId                   = rule.DxEId.Value,
            Project                 = rule.Project.Value,
            ChangeKind              = changeKind,
            ParentRuleCode          = rule.ParentRuleCode?.Value,
            PermissionCode          = rule.PermissionCode.Value,
            RoutePath               = rule.Path,
            MenuType                = rule.MenuType?.ToString(),
            AffectedPermissionCodes = affectedPermissionCodes,
            OperatorUserid          = operatorUserid,
        };

        return new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.MenuChanged,
            Project   = rule.Project.Value,
            Payload   = JsonSerializer.Serialize(payload, _json),
        };
    }

    /// <summary>构造 ApiMapChanged Outbox 事件。</summary>
    private static RbacOutboxEvent BuildApiMapChangedEvent(
        RbacApiPermissionMap map, string changeKind,
        string? oldPermissionCode, string? oldAction,
        string operatorUserid)
    {
        var payload = new ApiMapChangedPayload
        {
            Project           = map.Project.Value,
            HttpMethod        = map.HttpMethod,
            RoutePattern      = map.RoutePattern,
            OldPermissionCode = oldPermissionCode,
            NewPermissionCode = changeKind == "Deleted" ? null : map.PermissionCode.Value,
            OldAction         = oldAction,
            NewAction         = changeKind == "Deleted" ? null : map.Action,
            ChangeKind        = changeKind,
            OperatorUserid    = operatorUserid,
        };

        return new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.ApiMapChanged,
            Project   = map.Project.Value,
            Payload   = JsonSerializer.Serialize(payload, _json),
        };
    }

    /// <summary>追加单条 PolicyChanged Outbox 事件到当前 DbContext。</summary>
    private void AppendPolicyChangedEvent(
        string project, string groupCode,
        string permissionCode, string action,
        string changeKind, string subjectType,
        IReadOnlyList<string> affectedUserids,
        string operatorUserid,
        string? userid = null)
    {
        var payload = new PolicyChangedPayload
        {
            Project         = project,
            PolicyVersion   = 0,           // 实际版本由 Redis IncrPolicyVersion 在消费侧递增
            ChangeKind      = changeKind,
            SubjectType     = subjectType,
            Userid          = userid,
            GroupCode       = groupCode,
            PermissionCode  = permissionCode,
            Action          = action,
            AffectedUserids = affectedUserids,
            OperatorUserid  = operatorUserid,
        };

        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.PolicyChanged,
            Project   = project,
            GroupCode = groupCode,
            Userid    = userid,
            Payload   = Serialize(payload),
        });
    }
}
