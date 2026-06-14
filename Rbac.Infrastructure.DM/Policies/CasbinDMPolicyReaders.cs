using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rbac.Application.Policies;
using Rbac.Domain.Groups;
using Rbac.Domain.Permissions;
using Rbac.Domain.ValueObjects;
using Rbac.Infrastructure.DM.Mapping;

namespace Rbac.Infrastructure.DM.Policies;

/// <summary>
/// ICasbinGroupingPolicyReader 的真实 DM 实现。
///
/// 数据来源：rbac_group_member 表（RbacGroupMember 聚合根）。
/// 该表是 Casbin `g` policy 的唯一真相来源，对应三元组 (userid, groupCode, project)。
///
/// 替换了 Wave 1 中因缺失数据模型而返回空集合的占位实现。
/// 禁止从 rbac_project_grant × rbac_group 做笛卡尔积推导（会导致越权）。
/// 禁止从 Redis permset 或 ES 反向加载。
/// </summary>
public sealed class CasbinDMGroupingPolicyReader : ICasbinGroupingPolicyReader
{
    private readonly RbacDbContext _db;
    private readonly ILogger<CasbinDMGroupingPolicyReader> _logger;

    public CasbinDMGroupingPolicyReader(
        RbacDbContext db,
        ILogger<CasbinDMGroupingPolicyReader> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string Userid, string GroupCode, string Project)>> LoadAsync(
        ProjectCode project, CancellationToken ct = default)
    {
        _logger.LogDebug("LoadGroupingPolicy project={P}", project.Value);

        // 直接从 rbac_group_member 读取 (userid, groupCode, project) 三元组
        // ProjectCode("*") = 全项目，跳过 project 过滤
        var query = _db.GroupMembers.AsQueryable();
        if (project.Value != "*")
            query = query.Where(m => m.Project == project);

        var members = await query.ToListAsync(ct);
        var result = members
            .Select(m => ValueTuple.Create(
                m.Userid.Value,
                m.GroupCode.Value,
                m.Project.Value))
            .ToList();

        _logger.LogDebug(
            "GroupingPolicy loaded project={P} rows={N}", project.Value, result.Count);

        return result;
    }
}

/// <summary>
/// ICasbinPermissionPolicyReader 的 DM 实现。（与 Wave 1 实现一致，未修改）
///
/// 从 rbac_group.permission_codes 展开 p policy（组-权限码-action 四元组）。
/// action 从 rbac_api_permission_map 的 action 字段查找；未命中时默认 "access"。
/// </summary>
public sealed class CasbinDMPermissionPolicyReader : ICasbinPermissionPolicyReader
{
    private readonly RbacDbContext _db;
    private readonly ILogger<CasbinDMPermissionPolicyReader> _logger;

    public CasbinDMPermissionPolicyReader(
        RbacDbContext db,
        ILogger<CasbinDMPermissionPolicyReader> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string GroupCode, string Project, string PermissionCode, string Action)>>
        LoadAsync(ProjectCode project, CancellationToken ct = default)
    {
        _logger.LogDebug("LoadPermissionPolicy project={P}", project.Value);

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
            .GroupBy(m => m.PermCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g
                    .Select(m => m.Action)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var result = new List<(string, string, string, string)>();
        foreach (var group in groups)
        {
            foreach (var permCode in group.PermissionCodes)
            {
                var actions = actionLookup.TryGetValue(permCode.Value, out var a)
                    ? a
                    : new List<string> { "access" };

                foreach (var action in actions)
                {
                    result.Add((group.GroupCode.Value, group.Project.Value, permCode.Value, action));
                }
            }
        }

        _logger.LogDebug(
            "PermissionPolicy loaded project={P} policies={N}", project.Value, result.Count);

        return result;
    }
}
