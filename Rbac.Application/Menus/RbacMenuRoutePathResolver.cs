using Rbac.Application.Contracts.Menus;

namespace Rbac.Application.Menus;

public static class RbacMenuRoutePathResolver
{
    public static string ResolveRoutePath(
        IReadOnlyList<MenuNodeDto> menus,
        string fallbackPath)
        => FindFirstMenuPath(menus) ?? fallbackPath;

    public static string? FindFirstMenuPath(IReadOnlyList<MenuNodeDto> nodes)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Type, "menu", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(node.Path))
            {
                return node.Path;
            }

            if (node.Children.Count > 0)
            {
                var found = FindFirstMenuPath(node.Children);
                if (found is not null) return found;
            }
        }

        return null;
    }
}
