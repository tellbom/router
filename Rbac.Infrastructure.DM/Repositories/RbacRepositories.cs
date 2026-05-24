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
using Rbac.Infrastructure.DM.Mapping;
using Rbac.Infrastructure.DM.Outbox;

namespace Rbac.Infrastructure.DM.Repositories;

// 鈹€鈹€ 绠＄悊鍛?Repository 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

/// <summary>
/// PATCH-07: IAdministratorRepository 鐨?MySQL/EF Core 瀹炵幇銆?/// ProjectCode("*") 绾﹀畾锛氳烦杩?project 杩囨护锛岃鍙栨墍鏈?project 涓嬬殑璁板綍銆?/// </summary>
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

    public async Task<IReadOnlyList<RbacAdministrator>> FindByProjectAsync(
        ProjectCode project, CancellationToken ct = default)
    {
        // project="*" 绾﹀畾锛氳繑鍥炴墍鏈夎褰曪紙鍏ㄩ噺閲嶅缓鐢級
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

// 鈹€鈹€ 鏉冮檺缁?Repository 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

/// <summary>
/// PATCH-07: IGroupRepository 鐨?MySQL/EF Core 瀹炵幇銆?/// </summary>
public sealed class GroupRepository : IGroupRepository
{
    private readonly RbacDbContext _db;

    public GroupRepository(RbacDbContext db) => _db = db;

    public Task<RbacGroup?> FindByGuidAsync(Guid id, CancellationToken ct = default)
        => _db.Groups.FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<RbacGroup?> FindByGroupCodeAsync(GroupCode groupCode, ProjectCode project, CancellationToken ct = default)
        => _db.Groups.FirstOrDefaultAsync(g => g.GroupCode == groupCode && g.Project == project, ct);

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

// 鈹€鈹€ 瑙勫垯 Repository 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

/// <summary>
/// PATCH-07: IRuleRepository 鐨?MySQL/EF Core 瀹炵幇銆?/// </summary>

public sealed class RuleRepository : IRuleRepository
{
    private readonly RbacDbContext _db;

    public RuleRepository(RbacDbContext db) => _db = db;

    public Task<RbacRule?> FindByGuidAsync(Guid id, CancellationToken ct = default)
        => _db.Rules.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<RbacRule?> FindByRuleCodeAsync(RuleCode ruleCode, ProjectCode project, CancellationToken ct = default)
        => _db.Rules.FirstOrDefaultAsync(r => r.RuleCode == ruleCode && r.Project == project, ct);

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
        // 浠庣粍鐨?RuleCodes 闆嗗悎鍖归厤瑙勫垯
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

// 鈹€鈹€ ProjectGrant Repository 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

/// <summary>
/// PATCH-07: IProjectGrantRepository 鐨?MySQL/EF Core 瀹炵幇銆?/// </summary>
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

// 鈹€鈹€ ApiPermissionMap Repository 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

/// <summary>
/// PATCH-07: IApiPermissionMapRepository 鐨?MySQL/EF Core 瀹炵幇銆?/// </summary>
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

    public async Task<(IReadOnlyList<RbacApiPermissionMap> Items, int Total)> FindByProjectPagedAsync(
        ProjectCode project,
        string? keyword,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var q = project.Value == "*"
            ? _db.ApiPermissionMaps.AsQueryable()
            : _db.ApiPermissionMaps.Where(m => m.Project == project);

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<ApiMapStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            q = q.Where(m => m.Status == parsedStatus);
        }

        if (string.IsNullOrWhiteSpace(keyword))
        {
            var dbTotal = await q.CountAsync(ct);
            var dbItems = await q
                .OrderBy(m => m.HttpMethod)
                .ThenBy(m => m.RoutePattern)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return (dbItems, dbTotal);
        }

        var kw = keyword.Trim();
        var all = await q
            .OrderBy(m => m.HttpMethod)
            .ThenBy(m => m.RoutePattern)
            .ToListAsync(ct);

        var filtered = all
            .Where(m =>
                m.RoutePattern.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                m.PermissionCode.Value.Contains(kw, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Project.Value)
            .ThenBy(m => m.HttpMethod)
            .ThenBy(m => m.RoutePattern)
            .ToList();

        var total = filtered.Count;
        var items = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (items, total);
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

// 鈹€鈹€ CasbinPolicy Repository 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

/// <summary>
/// PATCH-07: ICasbinPolicyRepository 鐨?MySQL/EF Core 瀹炵幇銆?/// 澶嶇敤 GroupRepository 涓殑鏌ヨ閫昏緫锛岄€氳繃 EF Core 鐩存帴鏌ヨ銆?/// </summary>
public sealed class CasbinPolicyRepository : ICasbinPolicyRepository
{
    private readonly RbacDbContext _db;

    public CasbinPolicyRepository(RbacDbContext db) => _db = db;

    public async Task<IReadOnlyList<(string Userid, string GroupCode, string Project)>>
        GetGroupingPoliciesAsync(ProjectCode project, CancellationToken ct = default)
    {
        var query = _db.GroupMembers.AsQueryable();
        if (project.Value != "*")
            query = query.Where(m => m.Project == project);

        return await query
            .Select(m => ValueTuple.Create(
                m.Userid.Value,
                m.GroupCode.Value,
                m.Project.Value))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<(string GroupCode, string Project, string PermissionCode, string Action)>>
        GetPermissionPoliciesAsync(ProjectCode project, CancellationToken ct = default)
    {
        var groupsQuery = _db.Groups.Where(g => g.Status == GroupStatus.Active);
        if (project.Value != "*")
            groupsQuery = groupsQuery.Where(g => g.Project == project);

        var groups = await groupsQuery.ToListAsync(ct);

        var apiMapsQuery = _db.ApiPermissionMaps.Where(m => m.Status == ApiMapStatus.Active);
        if (project.Value != "*")
            apiMapsQuery = apiMapsQuery.Where(m => m.Project == project);

        var apiMaps = (await apiMapsQuery.ToListAsync(ct))
            .Select(m => new { PermCode = m.PermissionCode.Value, Action = m.Action })
            .ToList();

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

// 鈹€鈹€ ProjectGrantMySqlReader 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

/// <summary>
/// PATCH-07: IProjectGrantMySqlReader 鐨?MySQL/EF Core 瀹炵幇銆?/// 渚?RbacProjectGrantCache 鍦?FusionCache/Redis 鍧囨湭鍛戒腑鏃跺仛 MySQL 鍏滃簳銆?/// </summary>
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

        var userId = new UserId(userid);
        var admin = await _db.Administrators
            .FirstOrDefaultAsync(a => a.Userid == userId, ct);

        if (admin is null || admin.Status == AdminStatus.Disabled)
        {
            _logger.LogWarning("User not found or disabled userid={U}", userid);
            return null;
        }

        // 璇诲彇璇ョ敤鎴锋墍鏈?project 鎺堟潈
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
                PolicyVersion = 0,
            };
        }

        return map;
    }
}
