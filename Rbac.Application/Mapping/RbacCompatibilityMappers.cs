using Rbac.Application.Contracts.Menus;
using Rbac.Application.Contracts.Compatibility;
using Rbac.Domain.Groups;
using Rbac.Domain.Permissions;
using Rbac.Domain.Projects;
using Rbac.Domain.Rules;
using Rbac.Domain.Users;

namespace Rbac.Application.Mapping;

/// <summary>
/// 领域聚合根 → 前端兼容 DTO 的映射器。
/// </summary>
public static class RbacCompatibilityMappers
{
    // ── 管理员 ──────────────────────────────────────────────────────

    /// <summary>管理员聚合根 → AdminInfoDto。</summary>
    public static AdminInfoDto ToAdminInfoDto(this RbacAdministrator admin, string project, bool isSuper) =>
        new()
        {
            Userid = admin.Userid.Value,
            Username = admin.Username,
            Project = project,
            Super = isSuper,
        };

    // ── 权限组 ──────────────────────────────────────────────────────

    /// <summary>权限组聚合根 → GroupSummaryDto。</summary>
    public static GroupSummaryDto ToGroupSummaryDto(this RbacGroup group) =>
        new()
        {
            GroupCode = group.GroupCode.Value,
            Project = group.Project.Value,
            GroupName = group.GroupName,
            ParentGroupCode = group.ParentGroupCode?.Value,
            RuleCodes = group.RuleCodes.Select(r => r.Value).ToList(),
            PermissionCodes = group.PermissionCodes.Select(p => p.Value).ToList(),
            Status = group.Status.ToString(),
        };

    // ── 菜单规则 ────────────────────────────────────────────────────

    /// <summary>
    /// 规则列表 → 前端 menus 树（递归构建）。
    /// 仅包含 Active 规则。调用方需先按 Weigh 排序。
    /// </summary>
    public static IReadOnlyList<MenuNodeDto> ToMenuTree(
        this IReadOnlyList<RbacRule> rules,
        string? parentRuleCode = null)
    {
        return rules
            .Where(r => r.Status == RuleStatus.Active
                && r.ParentRuleCode?.Value == parentRuleCode)
            .OrderBy(r => r.Weigh)
            .Select(r => r.ToMenuNodeDto(rules))
            .ToList();
    }

    /// <summary>规则聚合根 → MenuNodeDto（含递归子节点）。</summary>
    public static MenuNodeDto ToMenuNodeDto(this RbacRule rule, IReadOnlyList<RbacRule> allRules) =>
        new()
        {
            Pid = rule.ParentRuleCode?.Value ?? "0",
            Title = rule.Title,
            Name = rule.Name,
            Path = rule.Path,
            Icon = rule.Icon ?? string.Empty,
            Type = ToFrontendRuleType(rule.Type),
            MenuType = ToFrontendMenuType(rule.MenuType),
            Url = rule.Url ?? string.Empty,
            Component = rule.Component ?? string.Empty,
            Extend = rule.Extend ?? string.Empty,
            Remark = rule.Remark ?? string.Empty,
            Keepalive = rule.Keepalive,
            Weigh = rule.Weigh,
            PermissionCode = rule.PermissionCode.Value,
            RuleCode = rule.RuleCode.Value,
            Children = allRules.ToMenuTree(rule.RuleCode.Value),
        };

    public static string ToFrontendRuleType(RuleType type) => type switch
    {
        RuleType.MenuDir => "menu_dir",
        RuleType.Menu => "menu",
        RuleType.Button => "button",
        _ => type.ToString().ToLowerInvariant(),
    };

    public static string ToFrontendMenuType(MenuType? type) => type switch
    {
        MenuType.Tab => "tab",
        MenuType.Link => "link",
        MenuType.Iframe => "iframe",
        null => string.Empty,
        _ => type.ToString()!.ToLowerInvariant(),
    };

    public static bool TryParseRuleType(string? value, out RuleType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim().Replace("_", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse(normalized, ignoreCase: true, out type);
    }

    public static bool TryParseMenuType(string? value, out MenuType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim().Replace("_", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse(normalized, ignoreCase: true, out type);
    }

    // ── 权限视图 ────────────────────────────────────────────────────

    /// <summary>API 映射聚合根 → PermissionViewDto。</summary>
    public static PermissionViewDto ToPermissionViewDto(this RbacApiPermissionMap map) =>
        new()
        {
            Project = map.Project.Value,
            PermissionCode = map.PermissionCode.Value,
            Action = map.Action,
            ResourceType = "api",
            Path = map.RoutePattern,
            Status = map.Status.ToString(),
        };
}

// ── 对外 DTO（非前端 menus 节点，补充管理端列表用途）────────────────

/// <summary>权限组列表 DTO。</summary>
public sealed class GroupSummaryDto
{
    public string GroupCode { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public string GroupName { get; init; } = string.Empty;
    public string? ParentGroupCode { get; init; }
    public IReadOnlyList<string> RuleCodes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PermissionCodes { get; init; } = Array.Empty<string>();
    public string Status { get; init; } = string.Empty;
}

/// <summary>权限码视图 DTO（管理端排查用）。</summary>
public sealed class PermissionViewDto
{
    public string Project { get; init; } = string.Empty;
    public string PermissionCode { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}
