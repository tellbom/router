using Rbac.Application.Contracts.Menus;

namespace Rbac.Application.Migration;

/// <summary>
/// PHP 旧系统与新 RBAC 权限对比服务契约。
///
/// 灰度迁移阶段 2（只读对比）使用，比较两套系统在以下维度的差异：
/// - menus 树结构差异（节点增减、排序变化）
/// - 按钮权限差异（add/edit/del/sortable 的有无）
/// - permissionCode 覆盖差异
///
/// 对比结果写入审计日志，供迁移团队分析和修复。
/// 不影响线上鉴权结果。
/// </summary>
public interface IRbacCompatibilityDiffService
{
    /// <summary>
    /// 对比新旧系统菜单树差异。
    /// </summary>
    Task<MenuDiffReport> DiffMenusAsync(
        string userid, string project,
        IReadOnlyList<MenuNodeDto> newMenus,
        IReadOnlyList<LegacyMenuNode> legacyMenus,
        CancellationToken ct = default);

    /// <summary>
    /// 对比新旧系统权限码差异。
    /// </summary>
    Task<PermissionDiffReport> DiffPermissionsAsync(
        string userid, string project,
        IReadOnlySet<string> newPermissionCodes,
        IReadOnlySet<string> legacyPermissionCodes,
        CancellationToken ct = default);
}

/// <summary>旧 PHP 系统菜单节点（最小兼容结构）。</summary>
public sealed class LegacyMenuNode
{
    public string Id { get; init; } = string.Empty;
    public string Pid { get; init; } = "0";
    public string Title { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public IReadOnlyList<LegacyMenuNode> Children { get; init; } = Array.Empty<LegacyMenuNode>();
}

/// <summary>菜单树对比报告。</summary>
public sealed class MenuDiffReport
{
    public string Userid { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public bool HasDiff { get; init; }

    /// <summary>新系统有但旧系统无的菜单 name。</summary>
    public IReadOnlyList<string> OnlyInNew { get; init; } = Array.Empty<string>();

    /// <summary>旧系统有但新系统无的菜单 name。</summary>
    public IReadOnlyList<string> OnlyInLegacy { get; init; } = Array.Empty<string>();

    /// <summary>两套系统都有但内容不一致的菜单 name。</summary>
    public IReadOnlyList<string> ContentMismatch { get; init; } = Array.Empty<string>();

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>权限码对比报告。</summary>
public sealed class PermissionDiffReport
{
    public string Userid { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public bool HasDiff { get; init; }

    /// <summary>新系统有但旧系统无（新系统可能多给权限）。</summary>
    public IReadOnlyList<string> OnlyInNew { get; init; } = Array.Empty<string>();

    /// <summary>旧系统有但新系统无（新系统可能少给权限，需排查）。</summary>
    public IReadOnlyList<string> OnlyInLegacy { get; init; } = Array.Empty<string>();

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// IRbacCompatibilityDiffService 默认实现。
/// </summary>
public sealed class RbacCompatibilityDiffService : IRbacCompatibilityDiffService
{
    public Task<MenuDiffReport> DiffMenusAsync(
        string userid, string project,
        IReadOnlyList<MenuNodeDto> newMenus,
        IReadOnlyList<LegacyMenuNode> legacyMenus,
        CancellationToken ct = default)
    {
        var newNames = FlattenMenuNames(newMenus);
        var legacyNames = FlattenLegacyNames(legacyMenus);

        var onlyInNew = newNames.Except(legacyNames, StringComparer.OrdinalIgnoreCase).ToList();
        var onlyInLegacy = legacyNames.Except(newNames, StringComparer.OrdinalIgnoreCase).ToList();

        return Task.FromResult(new MenuDiffReport
        {
            Userid = userid,
            Project = project,
            HasDiff = onlyInNew.Count > 0 || onlyInLegacy.Count > 0,
            OnlyInNew = onlyInNew,
            OnlyInLegacy = onlyInLegacy,
        });
    }

    public Task<PermissionDiffReport> DiffPermissionsAsync(
        string userid, string project,
        IReadOnlySet<string> newPermissionCodes,
        IReadOnlySet<string> legacyPermissionCodes,
        CancellationToken ct = default)
    {
        var onlyInNew = newPermissionCodes
            .Except(legacyPermissionCodes, StringComparer.OrdinalIgnoreCase).ToList();
        var onlyInLegacy = legacyPermissionCodes
            .Except(newPermissionCodes, StringComparer.OrdinalIgnoreCase).ToList();

        return Task.FromResult(new PermissionDiffReport
        {
            Userid = userid,
            Project = project,
            HasDiff = onlyInNew.Count > 0 || onlyInLegacy.Count > 0,
            OnlyInNew = onlyInNew,
            OnlyInLegacy = onlyInLegacy,
        });
    }

    private static IReadOnlySet<string> FlattenMenuNames(IReadOnlyList<MenuNodeDto> nodes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Flatten(nodes, result);
        return result;

        static void Flatten(IReadOnlyList<MenuNodeDto> nodes, HashSet<string> result)
        {
            foreach (var n in nodes)
            {
                if (!string.IsNullOrEmpty(n.Name)) result.Add(n.Name);
                Flatten(n.Children, result);
            }
        }
    }

    private static IReadOnlySet<string> FlattenLegacyNames(IReadOnlyList<LegacyMenuNode> nodes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Flatten(nodes, result);
        return result;

        static void Flatten(IReadOnlyList<LegacyMenuNode> nodes, HashSet<string> result)
        {
            foreach (var n in nodes)
            {
                if (!string.IsNullOrEmpty(n.Name)) result.Add(n.Name);
                Flatten(n.Children, result);
            }
        }
    }
}
