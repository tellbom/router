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
        UpsertEntity(_db.Administrators, admin);

        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.UserChanged,
            Project   = string.Empty,
            Userid    = admin.Userid.Value,
            Payload   = Serialize(new UserChangedPayload
            {
                Userid             = admin.Userid.Value,
                UserGuid           = admin.Id.ToString(),
                Project            = string.Empty,
                ChangedFields      = changedFields,
                OldStatus          = oldStatus,
                NewStatus          = admin.Status.ToString(),
                AffectedGroupCodes = affectedGroupCodes,
                OperatorUserid     = operatorUserid,
            }),
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("SaveAdministrator userid={U} operator={Op}", admin.Userid.Value, operatorUserid);
    }

    public async Task DeleteAdministratorAsync(
        RbacAdministrator admin,
        string operatorUserid,
        CancellationToken ct = default)
    {
        ValidateOperator(operatorUserid);

        // 1. 查出该用户所有 GroupMember 记录
        var members = await _db.GroupMembers
            .Where(m => m.Userid == admin.Userid)
            .ToListAsync(ct);

        // 批量加载所有相关 Group，避免在 foreach 内对同一 DbContext 做并发异步查询
        // （EF Core 不允许同一 DbContext 实例上同时运行多个异步操作）
        var groupKeys = members
            .Select(m => new { m.GroupCode, m.Project })
            .Distinct()
            .ToList();

        var groupCodes  = groupKeys.Select(k => k.GroupCode).ToList();
        var projectCodes = groupKeys.Select(k => k.Project).ToList();

        var groups = await _db.Groups
            .Where(g => groupCodes.Contains(g.GroupCode) && projectCodes.Contains(g.Project))
            .ToListAsync(ct);

        var groupLookup = groups.ToDictionary(
            g => (g.GroupCode, g.Project));

        foreach (var member in members)
        {
            groupLookup.TryGetValue((member.GroupCode, member.Project), out var grp);
            var permCodes = grp?.PermissionCodes.Select(p => p.Value).ToList()
                            ?? new List<string>();

            AppendPolicyChangedEvent(
                member.Project.Value, member.GroupCode.Value,
                permissionCode: string.Join(",", permCodes),
                action: "access", changeKind: "Removed", subjectType: "User",
                affectedUserids: new[] { admin.Userid.Value },
                operatorUserid,
                userid: admin.Userid.Value);

            _outbox.Append(new RbacOutboxEvent
            {
                EventType = RbacOutboxEventTypes.GroupChanged,
                Project   = member.Project.Value,
                GroupCode = member.GroupCode.Value,
                Payload   = Serialize(new GroupChangedPayload
                {
                    GroupCode          = member.GroupCode.Value,
                    GroupGuid          = grp?.Id.ToString() ?? string.Empty,
                    Project            = member.Project.Value,
                    ChangedFields      = new[] { "members" },
                    OldPermissionCodes = permCodes,
                    NewPermissionCodes = Array.Empty<string>(),
                    AffectedUserids    = new[] { admin.Userid.Value },
                    OperatorUserid     = operatorUserid,
                }),
            });
        }

        _db.GroupMembers.RemoveRange(members);

        // 2. 删除管理员记录
        _db.Administrators.Remove(admin);

        // 3. UserChanged（变更类型 = deleted）
        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.UserChanged,
            Project   = string.Empty,
            Userid    = admin.Userid.Value,
            Payload   = Serialize(new UserChangedPayload
            {
                Userid             = admin.Userid.Value,
                UserGuid           = admin.Id.ToString(),
                Project            = string.Empty,
                ChangedFields      = new[] { "deleted" },
                OldStatus          = admin.Status.ToString(),
                NewStatus          = "Deleted",
                AffectedGroupCodes = members.Select(m => m.GroupCode.Value).ToList(),
                OperatorUserid     = operatorUserid,
            }),
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "DeleteAdministrator userid={U} membersCleaned={N} operator={Op}",
            admin.Userid.Value, members.Count, operatorUserid);
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

        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.GroupChanged,
            Project   = group.Project.Value,
            GroupCode = group.GroupCode.Value,
            Payload   = Serialize(new GroupChangedPayload
            {
                GroupCode          = group.GroupCode.Value,
                GroupGuid          = group.Id.ToString(),
                Project            = group.Project.Value,
                ChangedFields      = changedFields,
                OldRuleCodes       = oldRuleCodes,
                NewRuleCodes       = newRuleCodes,
                OldPermissionCodes = oldPermissionCodes,
                NewPermissionCodes = newPermCodes,
                AffectedUserids    = affectedUserids,
                OperatorUserid     = operatorUserid,
            }),
        });

        var permCodesChanged = !oldPermissionCodes.OrderBy(x => x)
            .SequenceEqual(newPermCodes.OrderBy(x => x));

        if (permCodesChanged)
        {
            foreach (var added in newPermCodes.Except(oldPermissionCodes, StringComparer.OrdinalIgnoreCase))
                AppendPolicyChangedEvent(group.Project.Value, group.GroupCode.Value,
                    added, "access", "Added", "Group", affectedUserids, operatorUserid);

            foreach (var removed in oldPermissionCodes.Except(newPermCodes, StringComparer.OrdinalIgnoreCase))
                AppendPolicyChangedEvent(group.Project.Value, group.GroupCode.Value,
                    removed, "access", "Removed", "Group", affectedUserids, operatorUserid);
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("SaveGroup groupCode={G} project={P} operator={Op}",
            group.GroupCode.Value, group.Project.Value, operatorUserid);
    }

    public async Task DeleteGroupAsync(
        RbacGroup group,
        IReadOnlyList<string> affectedUserids,
        string operatorUserid,
        CancellationToken ct = default)
    {
        ValidateOperator(operatorUserid);

        // 1. 清理 GroupMember，每条产生 PolicyChanged + GroupChanged
        var members = await _db.GroupMembers
            .Where(m => m.GroupCode == group.GroupCode && m.Project == group.Project)
            .ToListAsync(ct);

        var permCodes = group.PermissionCodes.Select(p => p.Value).ToList();

        foreach (var member in members)
        {
            AppendPolicyChangedEvent(
                group.Project.Value, group.GroupCode.Value,
                permissionCode: string.Join(",", permCodes),
                action: "access", changeKind: "Removed", subjectType: "User",
                affectedUserids: new[] { member.Userid.Value },
                operatorUserid,
                userid: member.Userid.Value);

            _outbox.Append(new RbacOutboxEvent
            {
                EventType = RbacOutboxEventTypes.GroupChanged,
                Project   = group.Project.Value,
                GroupCode = group.GroupCode.Value,
                Payload   = Serialize(new GroupChangedPayload
                {
                    GroupCode          = group.GroupCode.Value,
                    GroupGuid          = group.Id.ToString(),
                    Project            = group.Project.Value,
                    ChangedFields      = new[] { "members" },
                    OldPermissionCodes = permCodes,
                    NewPermissionCodes = Array.Empty<string>(),
                    AffectedUserids    = new[] { member.Userid.Value },
                    OperatorUserid     = operatorUserid,
                }),
            });
        }

        _db.GroupMembers.RemoveRange(members);

        // 2. 删除 Group 记录
        _db.Groups.Remove(group);

        // 3. 最终 GroupChanged（changeKind=Deleted）
        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.GroupChanged,
            Project   = group.Project.Value,
            GroupCode = group.GroupCode.Value,
            Payload   = Serialize(new GroupChangedPayload
            {
                GroupCode          = group.GroupCode.Value,
                GroupGuid          = group.Id.ToString(),
                Project            = group.Project.Value,
                ChangedFields      = new[] { "deleted" },
                OldPermissionCodes = permCodes,
                NewPermissionCodes = Array.Empty<string>(),
                AffectedUserids    = affectedUserids,
                OperatorUserid     = operatorUserid,
            }),
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "DeleteGroup groupCode={G} project={P} membersCleaned={N} operator={Op}",
            group.GroupCode.Value, group.Project.Value, members.Count, operatorUserid);
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
        _logger.LogInformation("SaveRule ruleCode={R} project={P} changeKind={K} operator={Op}",
            rule.RuleCode.Value, rule.Project.Value, changeKind, operatorUserid);
    }

    public async Task DeleteRuleAsync(
        RbacRule rule,
        IReadOnlyList<string> affectedPermissionCodes,
        string operatorUserid,
        CancellationToken ct = default)
    {
        ValidateOperator(operatorUserid);
        _db.Rules.Remove(rule);
        _outbox.Append(BuildMenuChangedEvent(rule, "Deleted", affectedPermissionCodes, operatorUserid));
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("DeleteRule ruleCode={R} project={P} operator={Op}",
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

        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.ProjectGrantChanged,
            Project   = grant.Project.Value,
            Userid    = grant.Userid.Value,
            Payload   = Serialize(new ProjectGrantChangedPayload
            {
                Project        = grant.Project.Value,
                Userid         = grant.Userid.Value,
                GrantKind      = grantKind,
                OldProjects    = oldProjects,
                NewProjects    = newProjects,
                OldSuper       = oldSuper,
                NewSuper       = grant.IsSuper,
                OperatorUserid = operatorUserid,
            }),
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("SaveProjectGrant userid={U} project={P} grantKind={K} operator={Op}",
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

        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.ProjectGrantChanged,
            Project   = grant.Project.Value,
            Userid    = grant.Userid.Value,
            Payload   = Serialize(new ProjectGrantChangedPayload
            {
                Project        = grant.Project.Value,
                Userid         = grant.Userid.Value,
                GrantKind      = "Revoked",
                OldProjects    = new[] { grant.Project.Value },
                NewProjects    = remainingProjects,
                OldSuper       = grant.IsSuper,
                NewSuper       = false,
                OperatorUserid = operatorUserid,
            }),
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("RevokeProjectGrant userid={U} project={P} operator={Op}",
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
        _outbox.Append(BuildApiMapChangedEvent(map, changeKind, oldPermissionCode, oldAction, operatorUserid));
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("SaveApiPermissionMap project={P} method={M} route={R} changeKind={K} operator={Op}",
            map.Project.Value, map.HttpMethod, map.RoutePattern, changeKind, operatorUserid);
    }

    public async Task DeleteApiPermissionMapAsync(
        RbacApiPermissionMap map,
        string operatorUserid,
        CancellationToken ct = default)
    {
        ValidateOperator(operatorUserid);
        _db.ApiPermissionMaps.Remove(map);
        _outbox.Append(BuildApiMapChangedEvent(map, "Deleted",
            oldPermissionCode: map.PermissionCode.Value,
            oldAction: map.Action, operatorUserid));
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("DeleteApiPermissionMap project={P} method={M} route={R} operator={Op}",
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

        AppendPolicyChangedEvent(
            member.Project.Value, member.GroupCode.Value,
            permissionCode: string.Join(",", groupPermissionCodes),
            action: "access", changeKind: "Added", subjectType: "User",
            affectedUserids, operatorUserid, userid: member.Userid.Value);

        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.GroupChanged,
            Project   = member.Project.Value,
            GroupCode = member.GroupCode.Value,
            Payload   = Serialize(new GroupChangedPayload
            {
                GroupCode          = member.GroupCode.Value,
                GroupGuid          = string.Empty,
                Project            = member.Project.Value,
                ChangedFields      = new[] { "members" },
                NewPermissionCodes = groupPermissionCodes,
                AffectedUserids    = affectedUserids,
                OperatorUserid     = operatorUserid,
            }),
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("SaveGroupMember userid={U} groupCode={G} project={P} operator={Op}",
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

        AppendPolicyChangedEvent(
            member.Project.Value, member.GroupCode.Value,
            permissionCode: string.Join(",", groupPermissionCodes),
            action: "access", changeKind: "Removed", subjectType: "User",
            affectedUserids, operatorUserid, userid: member.Userid.Value);

        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.GroupChanged,
            Project   = member.Project.Value,
            GroupCode = member.GroupCode.Value,
            Payload   = Serialize(new GroupChangedPayload
            {
                GroupCode          = member.GroupCode.Value,
                GroupGuid          = string.Empty,
                Project            = member.Project.Value,
                ChangedFields      = new[] { "members" },
                OldPermissionCodes = groupPermissionCodes,
                AffectedUserids    = affectedUserids,
                OperatorUserid     = operatorUserid,
            }),
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("DeleteGroupMember userid={U} groupCode={G} project={P} operator={Op}",
            member.Userid.Value, member.GroupCode.Value, member.Project.Value, operatorUserid);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private void UpsertEntity<T>(DbSet<T> set, T entity) where T : class
    {
        var entry = _db.Entry(entity);
        if (entry.State == EntityState.Detached)
        {
            // 实体从未被当前 DbContext 追踪过（Create 场景）。
            // 直接 Add：EF 将其标记为 Added，SaveChanges 时执行 INSERT。
            set.Add(entity);
        }
        else if (entry.State == EntityState.Modified
              || entry.State == EntityState.Unchanged)
        {
            // 实体已由同一 DbContext 查询加载（Update 场景）。
            // 显式标记 Modified，避免 Update() 对无变化字段产生不必要的 UPDATE。
            entry.State = EntityState.Modified;
        }
        // Added / Deleted 状态：调用方已明确设置，不干预。
    }

    private static void ValidateOperator(string operatorUserid)
    {
        if (string.IsNullOrWhiteSpace(operatorUserid))
            throw new ArgumentException("operatorUserid is required.", nameof(operatorUserid));
    }

    private static string Serialize<T>(T payload) => JsonSerializer.Serialize(payload, _json);

    private static RbacOutboxEvent BuildMenuChangedEvent(
        RbacRule rule, string changeKind,
        IReadOnlyList<string> affectedPermissionCodes, string operatorUserid) =>
        new()
        {
            EventType = RbacOutboxEventTypes.MenuChanged,
            Project   = rule.Project.Value,
            Payload   = JsonSerializer.Serialize(new MenuChangedPayload
            {
                RuleCode                = rule.RuleCode.Value,
                RuleGuid                = rule.Id.ToString(),
                Project                 = rule.Project.Value,
                ChangeKind              = changeKind,
                ParentRuleCode          = rule.ParentRuleCode?.Value,
                PermissionCode          = rule.PermissionCode.Value,
                RoutePath               = rule.Path,
                MenuType                = rule.MenuType?.ToString(),
                AffectedPermissionCodes = affectedPermissionCodes,
                OperatorUserid          = operatorUserid,
            }, _json),
        };

    private static RbacOutboxEvent BuildApiMapChangedEvent(
        RbacApiPermissionMap map, string changeKind,
        string? oldPermissionCode, string? oldAction, string operatorUserid) =>
        new()
        {
            EventType = RbacOutboxEventTypes.ApiMapChanged,
            Project   = map.Project.Value,
            Payload   = JsonSerializer.Serialize(new ApiMapChangedPayload
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
            }, _json),
        };

    private void AppendPolicyChangedEvent(
        string project, string groupCode,
        string permissionCode, string action,
        string changeKind, string subjectType,
        IReadOnlyList<string> affectedUserids,
        string operatorUserid,
        string? userid = null)
    {
        _outbox.Append(new RbacOutboxEvent
        {
            EventType = RbacOutboxEventTypes.PolicyChanged,
            Project   = project,
            GroupCode = groupCode,
            Userid    = userid,
            Payload   = Serialize(new PolicyChangedPayload
            {
                Project         = project,
                PolicyVersion   = 0,
                ChangeKind      = changeKind,
                SubjectType     = subjectType,
                Userid          = userid,
                GroupCode       = groupCode,
                PermissionCode  = permissionCode,
                Action          = action,
                AffectedUserids = affectedUserids,
                OperatorUserid  = operatorUserid,
            }),
        });
    }
}