using Microsoft.Extensions.Logging;
using Rbac.Application.Contracts.Compatibility;
using Rbac.Application.Contracts.Menus;
using Rbac.Application.Menus;
using Rbac.Application.Repositories;
using Rbac.Application.Security;
using Rbac.Domain.Users;
using Rbac.Domain.ValueObjects;

namespace Rbac.Application.Backend;

/// <summary>
/// 后台首页初始化用例。
///
/// 对应 /api/admin/index 接口（前端登录成功后第一个请求）。
/// 返回 adminInfo + menus，不返回 refreshToken / siteConfig / terminal。
///
/// 首页初始化链路（设计文档 §9.1）：
/// 1. JWT 解析 userid（已由中间件完成）。
/// 2. IRbacProjectResolver 校验 project（已由中间件完成）。
/// 3. FusionCache 获取 menus（用户级缓存）。
/// 4. 未命中时 MySQL 重建用户快照和菜单树，再裁剪。
///
/// 常态下不查询多张表，通过 snapshot + menu-tree 两级缓存完成。
/// </summary>
public sealed class RbacBackendIndexService
{
    private readonly IAdministratorRepository _adminRepository;
    private readonly RbacMenuBuilder _menuBuilder;
    private readonly ILogger<RbacBackendIndexService> _logger;

    public RbacBackendIndexService(
        IAdministratorRepository adminRepository,
        RbacMenuBuilder menuBuilder,
        ILogger<RbacBackendIndexService> logger)
    {
        _adminRepository = adminRepository;
        _menuBuilder = menuBuilder;
        _logger = logger;
    }

    /// <summary>
    /// 构建后台首页初始化响应。
    /// </summary>
    public async Task<BackendIndexDto> BuildAsync(
        CurrentRbacContext context,
        CancellationToken ct = default)
    {
        // 1. 读取管理员信息（MySQL → FusionCache 兜底）
        var admin = await _adminRepository.FindByUseridAsync(
            new UserId(context.Userid), ct);

        if (admin is null)
        {
            _logger.LogWarning(
                "Admin not found userid={U} project={P} traceId={T}",
                context.Userid, context.Project, context.TraceId);
        }

        var adminInfo = BuildAdminInfo(admin, context);

        // 2. 构建用户菜单树（已裁剪，不暴露 Casbin 结构）
        var menus = await _menuBuilder.BuildUserMenusAsync(
            context.Userid, context.Project, context.IsProjectSuper, ct);

        // 3. 计算 routePath（第一个可见 menu 节点的 path）
        var routePath = ResolveRoutePath(menus);

        _logger.LogDebug(
            "BackendIndex built userid={U} project={P} menuRootCount={M} routePath={R}",
            context.Userid, context.Project, menus.Count, routePath);

        return new BackendIndexDto
        {
            AdminInfo = adminInfo,
            Menus = menus,
            RoutePath = routePath,
        };
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private static AdminInfoDto BuildAdminInfo(
        RbacAdministrator? admin, CurrentRbacContext ctx)
    {
        if (admin is null)
        {
            return new AdminInfoDto
            {
                DxEId = string.Empty,
                Userid = ctx.Userid,
                Username = ctx.Userid,
                Project = ctx.Project,
                Super = ctx.IsProjectSuper,
            };
        }

        return new AdminInfoDto
        {
            DxEId = admin.DxEId.Value,   // 必须为 string
            Userid = admin.Userid.Value,
            Username = admin.Username,
            Project = ctx.Project,
            Super = ctx.IsProjectSuper,
        };
    }

    /// <summary>
    /// 从菜单树中取第一个 type=menu 节点的 path 作为初始路由。
    /// 若无可见菜单，返回 "/dashboard" 作为前端兜底路径。
    /// </summary>
    private static string ResolveRoutePath(IReadOnlyList<MenuNodeDto> menus)
        => FindFirstMenuPath(menus) ?? "/dashboard";

    private static string? FindFirstMenuPath(IReadOnlyList<MenuNodeDto> nodes)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Type, "menu", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(node.Path))
                return node.Path;

            if (node.Children.Count > 0)
            {
                var found = FindFirstMenuPath(node.Children);
                if (found is not null) return found;
            }
        }
        return null;
    }
}
