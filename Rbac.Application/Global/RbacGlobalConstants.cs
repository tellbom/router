namespace Rbac.Application.Global;

/// <summary>
/// 统一权限中心（Unified Permission Center）保留系统常量。
/// </summary>
public static class RbacGlobalConstants
{
    /// <summary>
    /// 统一权限中心的保留 project 编码。
    /// </summary>
    public const string ReservedProjectCode = "__global__";

    /// <summary>
    /// 全局控制台顶级权限码。
    /// </summary>
    public const string PermAdmin = "rbac.global.admin";

    /// <summary>
    /// 跨 project 用户管理权限码。
    /// </summary>
    public const string PermUserManage = "rbac.global.user.manage";

    /// <summary>
    /// 跨 project 权限组管理权限码。
    /// </summary>
    public const string PermGroupManage = "rbac.global.group.manage";

    /// <summary>
    /// 跨 project 菜单/规则管理权限码。
    /// </summary>
    public const string PermMenuManage = "rbac.global.menu.manage";

    /// <summary>
    /// 判断给定 project 编码是否为系统保留的 global project。
    /// </summary>
    public static bool IsReservedProject(string? project) =>
        string.Equals(project, ReservedProjectCode, StringComparison.OrdinalIgnoreCase);
}
