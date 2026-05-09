using Microsoft.Extensions.Logging;
using Rbac.Application.Contracts.Menus;
using Rbac.Application.Mapping;
using Rbac.Application.Repositories;
using Rbac.Domain.ValueObjects;
using Rbac.Infrastructure.Redis;

namespace Rbac.Application.Menus;

/// <summary>
/// project 全量菜单树服务。
///
/// 读取顺序：FusionCache L1 → Redis rbac:menu-tree:{project} → MySQL 重建。
/// 菜单树是 project 级别（不含用户裁剪），所有用户共享同一份缓存。
/// 用户级菜单裁剪由 RbacMenuBuilder 负责（基于用户 permissionCode 集合）。
///
/// 约束：
/// - 不调用 ES（ES 不参与实时链路）。
/// - 不暴露 Casbin（前端不感知策略引擎）。
/// - 菜单节点 DxEId 必须为 string。
/// </summary>
public sealed class RbacProjectMenuTreeService
{
    private readonly RbacFusionCacheFacade _fusionCache;
    private readonly IRuleRepository _ruleRepository;
    private readonly ILogger<RbacProjectMenuTreeService> _logger;

    public RbacProjectMenuTreeService(
        RbacFusionCacheFacade fusionCache,
        IRuleRepository ruleRepository,
        ILogger<RbacProjectMenuTreeService> logger)
    {
        _fusionCache = fusionCache;
        _ruleRepository = ruleRepository;
        _logger = logger;
    }

    /// <summary>
    /// 获取 project 全量启用菜单树（不含用户裁剪）。
    /// FusionCache L1 未命中时从 MySQL 重建并写入 Redis L2。
    /// </summary>
    public async Task<IReadOnlyList<MenuNodeDto>> GetProjectMenuTreeAsync(
        string project,
        CancellationToken ct = default)
    {
        return await _fusionCache.GetMenuTreeAsync<IReadOnlyList<MenuNodeDto>>(
            project,
            async (ctx, token) =>
            {
                _logger.LogDebug("MenuTree cache miss, loading from MySQL project={Project}", project);
                return await BuildFromMySqlAsync(project, token);
            },
            ct) ?? Array.Empty<MenuNodeDto>();
    }

    /// <summary>主动使菜单树缓存失效（菜单变更后由 Outbox Worker 调用）。</summary>
    public Task InvalidateAsync(string project) =>
        _fusionCache.EvictMenuTreeAsync(project);

    // ── 私有：从 MySQL 构建菜单树 ────────────────────────────────

    private async Task<IReadOnlyList<MenuNodeDto>> BuildFromMySqlAsync(
        string project, CancellationToken ct)
    {
        var rules = await _ruleRepository.FindActiveByProjectAsync(
            new ProjectCode(project), ct);

        if (rules.Count == 0)
        {
            _logger.LogWarning("No active rules found for project={Project}", project);
            return Array.Empty<MenuNodeDto>();
        }

        // 按 Weigh 排序后构建树（RbacCompatibilityMappers.ToMenuTree 递归构建）
        var sorted = rules.OrderBy(r => r.Weigh).ToList();
        var tree = sorted.ToMenuTree(parentRuleCode: null);

        _logger.LogDebug(
            "MenuTree built from MySQL project={Project} rootNodes={Count}",
            project, tree.Count);

        return tree;
    }
}
