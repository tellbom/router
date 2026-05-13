using Microsoft.Extensions.Logging;
using Rbac.Application.Contracts.Menus;
using Rbac.Application.Snapshots;

namespace Rbac.Application.Menus;

/// <summary>
/// 用户菜单裁剪器。
///
/// 基于用户 permissionCode 集合对 project 全量菜单树进行裁剪，
/// 返回该用户可见的菜单树（含按钮节点）。
///
/// 约束：
/// - 不调用 ES（ES 不参与实时链路）。
/// - 不暴露 Casbin 结构（前端只感知 menus，不感知策略引擎）。
/// - 菜单裁剪只依赖 permissionCode，不依赖 DxEId。
/// - super 用户返回完整菜单树（不裁剪）。
/// </summary>
public sealed class RbacMenuBuilder
{
    private readonly RbacProjectMenuTreeService _menuTreeService;
    private readonly IRbacSnapshotService _snapshotService;
    private readonly ILogger<RbacMenuBuilder> _logger;

    public RbacMenuBuilder(
        RbacProjectMenuTreeService menuTreeService,
        IRbacSnapshotService snapshotService,
        ILogger<RbacMenuBuilder> logger)
    {
        _menuTreeService = menuTreeService;
        _snapshotService = snapshotService;
        _logger = logger;
    }

    /// <summary>
    /// 构建当前用户可见的菜单树。
    /// </summary>
    /// <param name="userid">已验证的用户 ID。</param>
    /// <param name="project">已验证的 project。</param>
    /// <param name="isSuper">是否为 project-scoped super（super 返回完整树）。</param>
    /// <param name="ct"></param>
    public async Task<IReadOnlyList<MenuNodeDto>> BuildUserMenusAsync(
        string userid,
        string project,
        bool isSuper,
        CancellationToken ct = default)
    {
        // 1. 获取 project 全量菜单树
        var fullTree = await _menuTreeService.GetProjectMenuTreeAsync(project, ct);

        if (fullTree.Count == 0) return Array.Empty<MenuNodeDto>();

        // 2. super 用户返回完整菜单树
        if (isSuper)
        {
            _logger.LogDebug("SuperUser full menu tree userid={U} project={P}", userid, project);
            return fullTree;
        }

        // 3. 获取用户权限快照（含 permissionCodes）
        var snapshot = await _snapshotService.GetSnapshotAsync(userid, project, ct);
        if (snapshot is null)
        {
            _logger.LogWarning("No snapshot found, returning empty menus userid={U} project={P}", userid, project);
            return Array.Empty<MenuNodeDto>();
        }

        // 4. 按 permissionCode 裁剪菜单树
        var allowedCodes = new HashSet<string>(
            snapshot.PermissionCodes, StringComparer.OrdinalIgnoreCase);

        var pruned = PruneTree(fullTree, allowedCodes);

        _logger.LogDebug(
            "MenuTree pruned userid={U} project={P} rootNodes={Count}",
            userid, project, pruned.Count);

        return pruned;
    }

    // ── 递归裁剪 ─────────────────────────────────────────────────

    private static IReadOnlyList<MenuNodeDto> PruneTree(
        IReadOnlyList<MenuNodeDto> nodes,
        HashSet<string> allowedCodes)
    {
        var result = new List<MenuNodeDto>();

        foreach (var node in nodes)
        {
            // 按钮节点：直接按 permissionCode 判断
            if (string.Equals(node.Type, "button", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(node.PermissionCode)
                    && allowedCodes.Contains(node.PermissionCode))
                {
                    result.Add(node);
                }
                continue;
            }

            // 目录或菜单节点：递归裁剪子节点
            var prunedChildren = PruneTree(node.Children, allowedCodes);

            // 有访问权限的菜单节点，或目录下有可见子节点
            var hasPermission = !string.IsNullOrEmpty(node.PermissionCode)
                && allowedCodes.Contains(node.PermissionCode);
            var hasVisibleChildren = prunedChildren.Count > 0;

            if (hasPermission || hasVisibleChildren)
            {
                // 重建节点，替换子节点为裁剪后的列表
                result.Add(new MenuNodeDto
                {
                    DxEId = node.DxEId,
                    Pid = node.Pid,
                    Title = node.Title,
                    Name = node.Name,
                    Path = node.Path,
                    Icon = node.Icon,
                    Type = node.Type,
                    MenuType = node.MenuType,
                    Url = node.Url,
                    Component = node.Component,
                    Extend = node.Extend,
                    Remark = node.Remark,
                    Keepalive = node.Keepalive,
                    PermissionCode = node.PermissionCode,
                    RuleCode = node.RuleCode,
                    Children = prunedChildren,
                });
            }
        }

        return result;
    }
}
