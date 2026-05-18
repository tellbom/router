using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Management;
using Rbac.Application.Repositories;
using Rbac.Application.Search;
using Rbac.Application.Security;
using Rbac.Domain.Groups;
using Rbac.Domain.ValueObjects;

namespace Rbac.Api.Controllers;

/// <summary>
/// 权限组管理接口。
///
/// 写操作：WriteGuard 先从 MySQL 加载聚合根，再通过 IRbacManagementWriteService 持久化 + Outbox。
/// 读操作：ES 查询（管理端列表），MySQL 查询（详情/成员列表）。
/// project 来自 CurrentRbacContext，不信任 Request body 中的 project 字段。
/// </summary>
[ApiController]
[Route("api/group")]
public sealed partial class GroupController : ControllerBase
{
    private readonly ICurrentRbacContextAccessor _ctx;
    private readonly IRbacManagementSearchService _search;
    private readonly IRbacManagementWriteService _write;
    private readonly RbacManagementWriteGuard _guard;
    private readonly IGroupRepository _groupRepo;
    private readonly IRuleRepository _ruleRepo;
    private readonly IGroupMemberRepository _memberRepo;

    public GroupController(
        ICurrentRbacContextAccessor ctx,
        IRbacManagementSearchService search,
        IRbacManagementWriteService write,
        RbacManagementWriteGuard guard,
        IGroupRepository groupRepo,
        IRuleRepository ruleRepo,
        IGroupMemberRepository memberRepo)
    {
        _ctx = ctx;
        _search = search;
        _write = write;
        _guard = guard;
        _groupRepo = groupRepo;
        _ruleRepo = ruleRepo;
        _memberRepo = memberRepo;
    }

    // ── 列表 ──────────────────────────────────────────────────────

    /// <summary>GET /api/group/index - BuildAdmin-compatible group tree/options.</summary>
    [HttpGet("index")]
    public async Task<ApiResponse<object>> Index(
        [FromQuery] GroupIndexQuery query, CancellationToken ct)
    {
        var ctx = RequireContext();
        var groups = await _groupRepo.FindByProjectAsync(new ProjectCode(ctx.Project), ct);
        var filtered = FilterGroups(groups, query.QuickSearch);
        var rows = filtered
            .Select(ToGroupRow)
            .ToDictionary(r => r.GroupCode, StringComparer.OrdinalIgnoreCase);

        var roots = BuildGroupTree(rows);
        var currentGroupIds = await GetCurrentGroupIdsAsync(ctx.Userid, ctx.Project, rows, ct);

        if (query.Select)
        {
            return ApiResponse<object>.Ok(new
            {
                options = FlattenGroupOptions(roots)
            });
        }

        return ApiResponse<object>.Ok(new
        {
            list = roots,
            total = rows.Count,
            group = currentGroupIds,
            remark = GroupIndexRemark
        });
    }

    /// <summary>GET /api/group/list — ES 分页查询权限组列表。</summary>
    [HttpGet("list")]
    public async Task<ApiResponse<PagedData<GroupSearchResult>>> List(
        [FromQuery] GroupSearchQuery query, CancellationToken ct)
    {
        query.Project = RequireContext().Project;
        return ApiResponse<PagedData<GroupSearchResult>>.Ok(
            await _search.SearchGroupsAsync(query, ct));
    }

    // ── 创建 ──────────────────────────────────────────────────────

    /// <summary>POST /api/group — 新建权限组。</summary>
    [HttpPost]
    public async Task<ApiResponse<object>> Create(
        [FromBody] CreateGroupRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(req.GroupCode)) return Fail(40001, "groupCode 不能为空");
        if (string.IsNullOrWhiteSpace(req.GroupName)) return Fail(40001, "groupName 不能为空");

        var group = RbacGroup.Create(
            Guid.NewGuid(),
            new GroupCode(req.GroupCode),
            new ProjectCode(ctx.Project),
            req.GroupName,
            parentGroupCode: string.IsNullOrWhiteSpace(req.ParentGroupCode)
                ? null : new GroupCode(req.ParentGroupCode));

        var changedFields = new List<string> { "created" };

        if (req.Status is not null)
        {
            if (req.Status == "Disabled" || req.Status == "0") group.Disable();
            else group.Enable();
            changedFields.Add("status");
        }

        if (req.RuleCodes is not null)
        {
            var allRules = await _ruleRepo.FindActiveByProjectAsync(
                new ProjectCode(ctx.Project), ct);
            var ruleCodeSet = req.RuleCodes
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var selectedRules = allRules
                .Where(r => ruleCodeSet.Contains(r.RuleCode.Value))
                .ToList();
            var newRuleCodes = ruleCodeSet.Contains("*")
                ? new List<RuleCode> { new("*") }
                : selectedRules.Select(r => r.RuleCode).ToList();
            var derivedPermCodes = ruleCodeSet.Contains("*")
                ? new List<PermissionCode> { new("*") }
                : selectedRules.Select(r => r.PermissionCode).ToList();

            // ExtraPermissionCodes comes from api-map and is unioned with permissions derived from ruleCodes.
            var extraPerms = (req.ExtraPermissionCodes ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p));
            var finalPermCodes = derivedPermCodes
                .Select(p => p.Value)
                .Union(extraPerms, StringComparer.OrdinalIgnoreCase)
                .Select(p => new PermissionCode(p))
                .ToList();

            group.UpdateRules(newRuleCodes, finalPermCodes);
            changedFields.Add("ruleCodes");
            changedFields.Add("permissionCodes");
        }

        await _write.SaveGroupAsync(
            group,
            changedFields,
            oldRuleCodes: Array.Empty<string>(),
            oldPermissionCodes: Array.Empty<string>(),
            affectedUserids: Array.Empty<string>(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(new
        {
            groupCode = group.GroupCode.Value
        });
    }

    // ── 更新规则/权限码 ────────────────────────────────────────────

    /// <summary>PUT /api/group/{groupCode}/rules — 更新权限组的 ruleCodes + permissionCodes。</summary>
    [HttpPut("{groupCode}/rules")]
    public async Task<ApiResponse<object>> UpdateRules(
        string groupCode, [FromBody] UpdateGroupRulesRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        var group = await _guard.LoadGroupByCodeAsync(groupCode, ctx.Project, ct);
        if (group is null) return Fail(40400, "权限组不存在");

        var oldRuleCodes = group.RuleCodes.Select(r => r.Value).ToList();
        var oldPermCodes = group.PermissionCodes.Select(p => p.Value).ToList();

        var allRules = await _ruleRepo.FindActiveByProjectAsync(
            new ProjectCode(ctx.Project), ct);
        var requestedRuleCodes = req.RuleCodes ?? Array.Empty<string>();
        var ruleCodeSet = requestedRuleCodes
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selectedRules = allRules
            .Where(r => ruleCodeSet.Contains(r.RuleCode.Value))
            .ToList();
        var newRuleCodes = ruleCodeSet.Contains("*")
            ? new List<RuleCode> { new("*") }
            : selectedRules.Select(r => r.RuleCode).ToList();
        var derivedPermCodes = ruleCodeSet.Contains("*")
            ? new List<string> { "*" }
            : selectedRules
            .Select(r => r.PermissionCode.Value)
            .ToList();

        var mergedPermCodes = oldPermCodes
            .Union(derivedPermCodes, StringComparer.OrdinalIgnoreCase)
            .Union(
                (req.ExtraPermissionCodes ?? Array.Empty<string>())
                    .Where(p => !string.IsNullOrWhiteSpace(p)),
                StringComparer.OrdinalIgnoreCase)
            .Select(p => new PermissionCode(p))
            .ToList();

        group.UpdateRules(newRuleCodes, mergedPermCodes);

        // affectedUserids 由调用方提供；如未提供则从 MySQL 查询
        var members = await _memberRepo.FindByGroupCodeAndProjectAsync(
            group.GroupCode.Value, ctx.Project, ct);
        var affectedUserids = members.Select(m => m.Userid.Value).ToList();

        await _write.SaveGroupAsync(
            group,
            changedFields: new[] { "ruleCodes", "permissionCodes" },
            oldRuleCodes,
            oldPermCodes,
            affectedUserids,
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // ── 变更状态 ──────────────────────────────────────────────────

    /// <summary>PUT /api/group/{groupCode}/status — 启用/禁用权限组。</summary>
    [HttpPut("{groupCode}/status")]
    public async Task<ApiResponse<object>> ChangeStatus(
        string groupCode, [FromBody] ChangeGroupStatusRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        var group = await _guard.LoadGroupByCodeAsync(groupCode, ctx.Project, ct);
        if (group is null) return Fail(40400, "权限组不存在");

        if (req.Status == "Disabled") group.Disable();
        else group.Enable();

        var members = await _memberRepo.FindByGroupCodeAndProjectAsync(
            group.GroupCode.Value, ctx.Project, ct);
        var affectedUserids = members.Select(m => m.Userid.Value).ToList();

        await _write.SaveGroupAsync(
            group,
            changedFields: new[] { "status" },
            oldRuleCodes: group.RuleCodes.Select(r => r.Value).ToList(),
            oldPermissionCodes: group.PermissionCodes.Select(p => p.Value).ToList(),
            affectedUserids,
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // ── 成员管理 ──────────────────────────────────────────────────

    /// <summary>POST /api/group/{groupCode}/members — 将用户加入权限组。</summary>
    [HttpPost("{groupCode}/members")]
    public async Task<ApiResponse<object>> AddMember(
        string groupCode, [FromBody] GroupMemberRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(req.Userid)) return Fail(40001, "userid 不能为空");

        var group = await _guard.LoadGroupByCodeAsync(groupCode, ctx.Project, ct);
        if (group is null) return Fail(40400, "权限组不存在");

        var member = RbacGroupMember.Create(
            Guid.NewGuid(),
            new UserId(req.Userid),
            group.GroupCode,
            group.Project,
            grantedBy: ctx.Userid);

        await _write.SaveGroupMemberAsync(
            member,
            affectedUserids: new[] { req.Userid },
            groupPermissionCodes: group.PermissionCodes.Select(p => p.Value).ToList(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    /// <summary>DELETE /api/group/{groupCode}/members/{userid} — 将用户从权限组移除。</summary>
    [HttpDelete("{groupCode}/members/{userid}")]
    public async Task<ApiResponse<object>> RemoveMember(
        string groupCode, string userid, CancellationToken ct)
    {
        var ctx = RequireContext();

        var group = await _guard.LoadGroupByCodeAsync(groupCode, ctx.Project, ct);
        if (group is null) return Fail(40400, "权限组不存在");

        var memberRepo = HttpContext.RequestServices
            .GetRequiredService<IGroupMemberRepository>();
        var member = (await memberRepo.FindByUseridAndProjectAsync(userid, ctx.Project, ct))
            .FirstOrDefault(m => m.GroupCode == group.GroupCode);

        if (member is null) return Fail(40400, "成员关系不存在");

        await _write.DeleteGroupMemberAsync(
            member,
            affectedUserids: new[] { userid },
            groupPermissionCodes: group.PermissionCodes.Select(p => p.Value).ToList(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private CurrentRbacContext RequireContext() =>
        _ctx.Context ?? throw new InvalidOperationException("RbacContext missing");

    private static ApiResponse<object> Fail(int code, string msg) =>
        ApiResponse<object>.Fail(code, msg);

    private const string GroupIndexRemark =
        "Group hierarchy is for display. Effective access is determined by permission codes.";

    private static IReadOnlyList<RbacGroup> FilterGroups(
        IReadOnlyList<RbacGroup> groups, string? quickSearch)
    {
        if (string.IsNullOrWhiteSpace(quickSearch))
            return groups;

        var keyword = quickSearch.Trim();
        return groups.Where(g =>
            g.GroupName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || g.GroupCode.Value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static GroupIndexRowDto ToGroupRow(RbacGroup group)
    {
        return new GroupIndexRowDto
        {
            Id = group.GroupCode.Value,
            Pid = "0",
            GroupCode = group.GroupCode.Value,
            ParentGroupCode = group.ParentGroupCode?.Value,
            Name = group.GroupName,
            Rules = FormatRules(group),
            Status = group.Status == GroupStatus.Active ? "1" : "0",
            UpdateTime = group.UpdatedAt.ToUnixTimeSeconds(),
            CreateTime = group.CreatedAt.ToUnixTimeSeconds(),
            Children = new List<GroupIndexRowDto>()
        };
    }

    private static string FormatRules(RbacGroup group)
    {
        if (group.PermissionCodes.Any(p => p.Value == "*")
            || group.RuleCodes.Any(r => r.Value == "*"))
        {
            return "All permissions";
        }

        var count = group.RuleCodes.Count > 0
            ? group.RuleCodes.Count
            : group.PermissionCodes.Count;
        return count == 0 ? "0 permissions" : $"{count} permissions";
    }

    private static IReadOnlyList<GroupIndexRowDto> BuildGroupTree(
        Dictionary<string, GroupIndexRowDto> rows)
    {
        foreach (var row in rows.Values)
        {
            if (string.IsNullOrWhiteSpace(row.ParentGroupCode)
                || !rows.TryGetValue(row.ParentGroupCode, out var parent))
            {
                row.Pid = "0";
                continue;
            }

            row.Pid = parent.Id;
            parent.Children.Add(row);
        }

        return rows.Values
            .Where(r => r.Pid == "0")
            .OrderBy(r => r.CreateTime)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> GetCurrentGroupIdsAsync(
        string userid,
        string project,
        Dictionary<string, GroupIndexRowDto> rows,
        CancellationToken ct)
    {
        var memberRepo = HttpContext.RequestServices.GetRequiredService<IGroupMemberRepository>();
        var memberships = await memberRepo.FindByUseridAndProjectAsync(userid, project, ct);
        return memberships
            .Select(m => m.GroupCode.Value)
            .Where(rows.ContainsKey)
            .Select(code => rows[code].Id)
            .ToList();
    }

    private static IReadOnlyList<GroupIndexOptionDto> FlattenGroupOptions(
        IReadOnlyList<GroupIndexRowDto> roots)
    {
        var result = new List<GroupIndexOptionDto>();
        foreach (var root in roots)
        {
            AppendOption(root, depth: 0, result);
        }
        return result;
    }

    private static void AppendOption(
        GroupIndexRowDto row,
        int depth,
        List<GroupIndexOptionDto> result)
    {
        result.Add(GroupIndexOptionDto.FromRow(row, FormatOptionName(row.Name, depth)));

        foreach (var child in row.Children
            .OrderBy(c => c.CreateTime)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            AppendOption(child, depth + 1, result);
        }
    }

    private static string FormatOptionName(string name, int depth)
    {
        if (depth <= 0)
            return name;

        return new string(' ', depth * 4) + "└" + name;
    }
}

// ── Request DTOs ───────────────────────────────────────────────────

public sealed class CreateGroupRequest
{
    public string GroupCode { get; init; } = $"group_{Guid.NewGuid():N}";

    public string GroupName { get; init; } = string.Empty;

    public string? ParentGroupCode { get; init; }

    public string? Status { get; init; }

    public string[]? RuleCodes { get; init; }

    /// <summary>
    /// Extra permissionCodes from api-map. They are unioned with permissionCodes derived from RuleCodes.
    /// </summary>
    public string[]? ExtraPermissionCodes { get; init; }
}

public sealed record UpdateGroupRulesRequest(
    string[]? RuleCodes,
    string[]? ExtraPermissionCodes = null);

public sealed record ChangeGroupStatusRequest(
    string Status);

public sealed record GroupMemberRequest(string Userid);

public sealed class GroupIndexQuery
{
    [JsonPropertyName("select")]
    public bool Select { get; init; }

    [JsonPropertyName("isTree")]
    public bool IsTree { get; init; }

    [JsonPropertyName("quickSearch")]
    public string? QuickSearch { get; init; }
}

public sealed class GroupIndexRowDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonIgnore]
    public string GroupCode { get; init; } = string.Empty;

    [JsonIgnore]
    public string? ParentGroupCode { get; init; }

    [JsonPropertyName("pid")]
    public string Pid { get; set; } = "0";

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("rules")]
    public string Rules { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("update_time")]
    public long UpdateTime { get; init; }

    [JsonPropertyName("create_time")]
    public long CreateTime { get; init; }

    [JsonPropertyName("children")]
    public List<GroupIndexRowDto> Children { get; init; } = new();
}

public sealed class GroupIndexOptionDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("pid")]
    public string Pid { get; init; } = "0";

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("rules")]
    public string Rules { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("update_time")]
    public long UpdateTime { get; init; }

    [JsonPropertyName("create_time")]
    public long CreateTime { get; init; }

    public static GroupIndexOptionDto FromRow(GroupIndexRowDto row, string name) =>
        new()
        {
            Id = row.Id,
            Pid = row.Pid,
            Name = name,
            Rules = row.Rules,
            Status = row.Status,
            UpdateTime = row.UpdateTime,
            CreateTime = row.CreateTime
        };
}
