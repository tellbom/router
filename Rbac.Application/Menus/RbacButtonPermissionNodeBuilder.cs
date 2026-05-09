using Rbac.Application.Contracts.Menus;

namespace Rbac.Application.Menus;

/// <summary>
/// 按钮权限节点构建器。
///
/// 负责将菜单树中 type=button 的节点解析为 authNode，
/// 供前端 auth("add") / v-auth="edit" 等判断使用。
///
/// 标准按钮权限节点 name 约定：
/// - add      → 新增
/// - edit     → 编辑
/// - del      → 删除
/// - sortable → 排序
///
/// 设计文档约定：按钮节点的 name 字段直接用于前端 auth() 匹配，
/// permissionCode 用于服务端鉴权，两者独立，前端不感知 permissionCode。
/// </summary>
public sealed class RbacButtonPermissionNodeBuilder
{
    /// <summary>标准按钮 name 常量。</summary>
    public static class ButtonNames
    {
        public const string Add = "add";
        public const string Edit = "edit";
        public const string Del = "del";
        public const string Sortable = "sortable";
    }

    /// <summary>
    /// 从已裁剪的用户菜单树中提取所有按钮权限节点，
    /// 构建 name → permissionCode 映射，供前端 authNode 使用。
    /// </summary>
    public static IReadOnlyDictionary<string, string> ExtractAuthNodes(
        IReadOnlyList<MenuNodeDto> menuTree)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CollectButtons(menuTree, result);
        return result;
    }

    /// <summary>
    /// 判断菜单树中是否包含指定 name 的按钮节点（对应前端 auth("name") 判断）。
    /// </summary>
    public static bool HasButton(IReadOnlyList<MenuNodeDto> menuTree, string buttonName)
    {
        return FindButton(menuTree, buttonName) is not null;
    }

    /// <summary>
    /// 从指定父菜单节点下提取标准按钮节点（add/edit/del/sortable）。
    /// 返回该父节点下存在的按钮 name 集合。
    /// </summary>
    public static IReadOnlySet<string> GetButtonsUnder(
        MenuNodeDto parentNode)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in parentNode.Children)
        {
            if (IsButtonNode(child))
                result.Add(child.Name);
        }
        return result;
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private static void CollectButtons(
        IReadOnlyList<MenuNodeDto> nodes,
        Dictionary<string, string> result)
    {
        foreach (var node in nodes)
        {
            if (IsButtonNode(node) && !string.IsNullOrEmpty(node.Name))
            {
                // name 唯一键（同名按钮取第一个）
                result.TryAdd(node.Name, node.PermissionCode);
            }

            if (node.Children.Count > 0)
                CollectButtons(node.Children, result);
        }
    }

    private static MenuNodeDto? FindButton(
        IReadOnlyList<MenuNodeDto> nodes, string buttonName)
    {
        foreach (var node in nodes)
        {
            if (IsButtonNode(node)
                && string.Equals(node.Name, buttonName, StringComparison.OrdinalIgnoreCase))
                return node;

            if (node.Children.Count > 0)
            {
                var found = FindButton(node.Children, buttonName);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static bool IsButtonNode(MenuNodeDto node) =>
        string.Equals(node.Type, "button", StringComparison.OrdinalIgnoreCase);
}
