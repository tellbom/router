using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rbac.Application.Repositories;
using Rbac.Application.Security;
using Rbac.Domain.Groups;
using Rbac.Domain.Permissions;
using Rbac.Domain.Projects;
using Rbac.Domain.Rules;
using Rbac.Domain.Users;
using Rbac.Domain.ValueObjects;
using Rbac.Infrastructure.MySql.Mapping;
using Rbac.Infrastructure.MySql.Outbox;

namespace Rbac.Infrastructure.MySql.Repositories;

// ── 管理员 Repository ─────────────────────────────────────────────

/// <summary>
/// PATCH-07: IAdministratorRepository 的 MySQL/EF Core 实现。
/// ProjectCode("*") 约定：跳过 project 过滤，读取所有 project 下的记录。
/// </summary>
public sealed class AdministratorRepository : IAdministratorRepository
{
    private readonly RbacDbContext _db;
    private readonly ILogger<AdministratorRepository> _logger;

    public AdministratorRepository(RbacDbContext db, ILogger<AdministratorRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<RbacAdministrator?> FindByGuidAsync(Guid id, CancellationToken ct = default)
        => _db.Administrators.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<RbacAdministrator?> FindByUseridAsync(UserId userid, CancellationToken ct = default)
        => _db.Administrators.FirstOrDefaultAsync(a => a.Userid == userid, ct);

    public Task<RbacAdministrator?> FindByDxEIdAsync(DxEId dxeId, CancellationToken ct = default)
        => _db.Administrators.FirstOrDefaultAsync(a => a.DxEId == dxeId, ct);

    public async Task<IReadOnlyList<RbacAdministrator>> FindByProjectAsync(
        ProjectCode project, CancellationToken ct = default)
    {
        // project="*" 约定：返回所有记录（全量重建用）
        var query = project.Value == "*"
            ? _db.Administrators
            : _db.Administrators.Where(a =>
                _db.ProjectGrants.Any(g => g.Userid == a.Userid && g.Project == project));

        return await query.ToListAsync(ct);
    }

    public async Task SaveAsync(RbacAdministrator admin, CancellationToken ct = default)
    {
        var existing = await _db.Administrators.FindAsync(new object[] { admin.Id }, ct);
        if (existing is null)
            _db.Administrators.Add(admin);
        else
            _db.Entry(existing).CurrentValues.SetValues(admin);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Administrators.FindAsync(new object[] { id }, ct);
        if (entity is not null)
        {
            _db.Administrators.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
    }
}

// ── 权限组 Repository ─────────────────────────────────────────────

/// <summary>
/// PATCH-07: IGroupRepository 的 MySQL/EF Core 实现。
/// </summary>
public sealed class GroupRepository : IGroupRepository
{
    private readonly RbacDbContext _db;

    public GroupRepository(RbacDbContext db) => _db = db;

    public Task<RbacGroup?> FindByGuidAsync(Guid id, CancellationToken ct = default)
        => _db.Groups.FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<RbacGroup?> FindByGroupCodeAsync(GroupCode groupCode, ProjectCode project, CancellationToken ct = default)
        => _db.Groups.FirstOrDefaultAsync(g => g.GroupCode == groupCode && g.Project == project, ct);

    public Task<RbacGroup?> FindByDxEIdAsync(DxEId dxeId, CancellationToken ct = default)
        => _db.Groups.FirstOrDefaultAsync(g => g.DxEId == dxeId, ct);

    public async Task<IReadOnlyList<RbacGroup>> FindByProjectAsync(
        ProjectCode project, CancellationToken ct = default)
    {
        var query = project.Value == "*"
            ? _db.Groups
            : _db.Groups.Where(g => g.Project == project);
        return await query.ToListAsync(ct);
    }

    public async Task SaveAsync(RbacGroup group, CancellationToken ct = default)
    {
        var existing = await _db.Groups.FindAsync(new object[] { group.Id }, ct);
        if (existing is null)
            _db.Groups.Add(group);
        else
            _db.Entry(existing).CurrentValues.SetValues(group);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Groups.FindAsync(new object[] { id }, ct);
        if (entity is not null)
        {
            _db.Groups.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
    }
}

// ── 规则 Repository ───────────────────────────────────────────────

/// <summary>
/// PATCH-07: IRuleRepository 的 MySQL/EF Core 实现。
/// </summary>
/// <summary>
/// PATCH-07: IGroupMemberRepository 的 MySQL/EF Core 实现。
/// </summary>
public sealed class GroupMemberRepository : IGroupMemberRepository
{
    private readonly RbacDbContext _db;

    public GroupMemberRepository(RbacDbContext db) => _db = db;

    public Task<RbacGroupMember?> FindAsync(
        UserId userid,
        GroupCode groupCode,
        ProjectCode project,
        CancellationToken ct = default)
        => _db.GroupMembers.FirstOrDefaultAsync(m =>
            m.Userid == userid &&
            m.GroupCode == groupCode &&
            m.Project == project,
            ct);

    public async Task<IReadOnlyList<RbacGroupMember>> FindByGroupAsync(
        GroupCode groupCode,
        ProjectCode project,
        CancellationToken ct = default)
        => await _db.GroupMembers
            .Where(m => m.GroupCode == groupCode && m.Project == project)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RbacGroupMember>> FindByUseridAsync(
        UserId userid,
        ProjectCode project,
        CancellationToken ct = default)
        => await _db.GroupMembers
            .Where(m => m.Userid == userid && m.Project == project)
            .ToListAsync(ct);
}

public sealed class RuleRepository : IRuleRepository
{
    private readonly RbacDbContext _db;

    public RuleRepository(RbacDbContext db) => _db = db;

    public Task<RbacRule?> FindByGuidAsync(Guid id, CancellationToken ct = default)
        => _db.Rules.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<RbacRule?> FindByRuleCodeAsync(RuleCode ruleCode, ProjectCode project, CancellationToken ct = default)
        => _db.Rules.FirstOrDefaultAsync(r => r.RuleCode == ruleCode && r.Project == project, ct);

    public Task<RbacRule?> FindByDxEIdAsync(DxEId dxeId, CancellationToken ct = default)
        => _db.Rules.FirstOrDefaultAsync(r => r.DxEId == dxeId, ct);

    public async Task<IReadOnlyList<RbacRule>> FindActiveByProjectAsync(
        ProjectCode project, CancellationToken ct = default)
    {
        var query = project.Value == "*"
            ? _db.Rules.Where(r => r.Status == RuleStatus.Active)
            : _db.Rules.Where(r => r.Project == project && r.Status == RuleStatus.Active);
        return await query.OrderBy(r => r.Weigh).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RbacRule>> FindByGroupCodeAsync(
        GroupCode groupCode, ProjectCode project, CancellationToken ct = default)
    {
        // 从组的 RuleCodes 集合匹配规则
        var group = await _db.Groups
            .FirstOrDefaultAsync(g => g.GroupCode == groupCode && g.Project == project, ct);

        if (group is null) return Array.Empty<RbacRule>();

        var ruleCodes = group.RuleCodes.Select(rc => rc.Value).ToList();
        return await _db.Rules
            .Where(r => r.Project == project && ruleCodes.Contains(r.RuleCode.Value))
            .OrderBy(r => r.Weigh)
            .ToListAsync(ct);
    }

    public async Task SaveAsync(RbacRule rule, CancellationToken ct = default)
    {
        var existing = await _db.Rules.FindAsync(new object[] { rule.Id }, ct);
        if (existing is null)
            _db.Rules.Add(rule);
        else
            _db.Entry(existing).CurrentValues.SetValues(rule);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.Rules.FindAsync(new object[] { id }, ct);
        if (entity is not null)
        {
            _db.Rules.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
    }
}

// ── ProjectGrant Repository ───────────────────────────────────────

/// <summary>
/// PATCH-07: IProjectGrantRepository 的 MySQL/EF Core 实现。
/// </summary>
public sealed class ProjectGrantRepository : IProjectGrantRepository
{
    private readonly RbacDbContext _db;

    public ProjectGrantRepository(RbacDbContext db) => _db = db;

    public Task<RbacProjectGrant?> FindAsync(UserId userid, ProjectCode project, CancellationToken ct = default)
        => _db.ProjectGrants.FirstOrDefaultAsync(g => g.Userid == userid && g.Project == project, ct);

    public async Task<IReadOnlyList<RbacProjectGrant>> FindByUseridAsync(
        UserId userid, CancellationToken ct = default)
        => await _db.ProjectGrants.Where(g => g.Userid == userid).ToListAsync(ct);

    public async Task<IReadOnlyList<RbacProjectGrant>> FindByProjectAsync(
        ProjectCode project, CancellationToken ct = default)
        => await _db.ProjectGrants.Where(g => g.Project == project).ToListAsync(ct);

    public async Task SaveAsync(RbacProjectGrant grant, CancellationToken ct = default)
    {
        var existing = await _db.ProjectGrants
            .FirstOrDefaultAsync(g => g.Userid == grant.Userid && g.Project == grant.Project, ct);
        if (existing is null)
            _db.ProjectGrants.Add(grant);
        else
            _db.Entry(existing).CurrentValues.SetValues(grant);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(UserId userid, ProjectCode project, CancellationToken ct = default)
    {
        var entity = await _db.ProjectGrants
            .FirstOrDefaultAsync(g => g.Userid == userid && g.Project == project, ct);
        if (entity is not null)
        {
            _db.ProjectGrants.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
    }
}

// ── ApiPermissionMap Repository ───────────────────────────────────

/// <summary>
/// PATCH-07: IApiPermissionMapRepository 的 MySQL/EF Core 实现。
/// </summary>
public sealed class ApiPermissionMapRepository : IApiPermissionMapRepository
{
    private readonly RbacDbContext _db;

    public ApiPermissionMapRepository(RbacDbContext db) => _db = db;

    public Task<RbacApiPermissionMap?> FindByGuidAsync(Guid id, CancellationToken ct = default)
        => _db.ApiPermissionMaps.FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<IReadOnlyList<RbacApiPermissionMap>> FindActiveByProjectAsync(
        ProjectCode project, CancellationToken ct = default)
    {
        var query = project.Value == "*"
            ? _db.ApiPermissionMaps.Where(m => m.Status == ApiMapStatus.Active)
            : _db.ApiPermissionMaps.Where(m => m.Project == project && m.Status == ApiMapStatus.Active);
        return await query.ToListAsync(ct);
    }

    public async Task SaveAsync(RbacApiPermissionMap map, CancellationToken ct = default)
    {
        var existing = await _db.ApiPermissionMaps.FindAsync(new object[] { map.Id }, ct);
        if (existing is null)
            _db.ApiPermissionMaps.Add(map);
        else
            _db.Entry(existing).CurrentValues.SetValues(map);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.ApiPermissionMaps.FindAsync(new object[] { id }, ct);
        if (entity is not null)
        {
            _db.ApiPermissionMaps.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }
    }
}

// ── CasbinPolicy Repository ───────────────────────────────────────

/// <summary>
/// PATCH-07: ICasbinPolicyRepository 的 MySQL/EF Core 实现。
/// 复用 GroupRepository 中的查询逻辑，通过 EF Core 直接查询。
/// </summary>
public sealed class CasbinPolicyRepository : ICasbinPolicyRepository
{
    private readonly RbacDbContext _db;

    public CasbinPolicyRepository(RbacDbContext db) => _db = db;

    public async Task<IReadOnlyList<(string Userid, string GroupCode, string Project)>>
        GetGroupingPoliciesAsync(ProjectCode project, CancellationToken ct = default)
    {
        // g policy：用户-组关系，当前由 Casbin 策略文件管理
        // 此处返回空集合，等待 user_group_member 表建立后替换
        // （与 CasbinMySqlGroupingPolicyReader 保持一致）
        await Task.CompletedTask;
        return Array.Empty<(string, string, string)>();
    }

    public async Task<IReadOnlyList<(string GroupCode, string Project, string PermissionCode, string Action)>>
        GetPermissionPoliciesAsync(ProjectCode project, CancellationToken ct = default)
    {
        var groups = await _db.Groups
            .Where(g => (project.Value == "*" || g.Project.Value == project.Value)
                        && g.Status == GroupStatus.Active)
            .ToListAsync(ct);

        var apiMaps = await _db.ApiPermissionMaps
            .Where(m => (project.Value == "*" || m.Project.Value == project.Value)
                        && m.Status == ApiMapStatus.Active)
            .Select(m => new { PermCode = m.PermissionCode.Value, Action = m.Action })
            .ToListAsync(ct);

        var actionLookup = apiMaps
            .GroupBy(m => m.PermCode)
            .ToDictionary(g => g.Key, g => g.First().Action, StringComparer.OrdinalIgnoreCase);

        var result = new List<(string, string, string, string)>();
        foreach (var group in groups)
        {
            foreach (var permCode in group.PermissionCodes)
            {
                var action = actionLookup.TryGetValue(permCode.Value, out var a) ? a : "access";
                result.Add((group.GroupCode.Value, group.Project.Value, permCode.Value, action));
            }
        }
        return result;
    }
}

// ── ProjectGrantMySqlReader ───────────────────────────────────────

/// <summary>
/// PATCH-07: IProjectGrantMySqlReader 的 MySQL/EF Core 实现。
/// 供 RbacProjectGrantCache 在 FusionCache/Redis 均未命中时做 MySQL 兜底。
/// </summary>
public sealed class ProjectGrantMySqlReader : IProjectGrantMySqlReader
{
    private readonly RbacDbContext _db;
    private readonly ILogger<ProjectGrantMySqlReader> _logger;

    public ProjectGrantMySqlReader(RbacDbContext db, ILogger<ProjectGrantMySqlReader> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<UserProjectGrantMap?> GetUserGrantsAsync(
        string userid, CancellationToken ct = default)
    {
        _logger.LogDebug("MySQL fallback GetUserGrants userid={U}", userid);

        // 查用户是否存在且未禁用
        var userId = new UserId(userid);
        var admin = await _db.Administrators
            .FirstOrDefaultAsync(a => a.Userid == userId, ct);

        if (admin is null || admin.Status == AdminStatus.Disabled)
        {
            _logger.LogWarning("User not found or disabled userid={U}", userid);
            return null;
        }

        // 读取该用户所有 project 授权
        var grants = await _db.ProjectGrants
            .Where(g => g.Userid == userId)
            .ToListAsync(ct);

        if (grants.Count == 0)
        {
            _logger.LogDebug("No project grants for userid={U}", userid);
            return null;
        }

        var map = new UserProjectGrantMap();
        foreach (var grant in grants)
        {
            map.Projects[grant.Project.Value] = new ProjectGrantInfo
            {
                IsSuper = grant.IsSuper,
                PolicyVersion = 0, // 从 Redis policy-version key 读取，此处占位
            };
        }

        return map;
    }
}
