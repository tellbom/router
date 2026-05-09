using Microsoft.Extensions.Logging;
using Rbac.Application.Cache;
using Rbac.Application.Contracts.Menus;
using Rbac.Application.Mapping;
using Rbac.Application.Repositories;
using Rbac.Domain.ValueObjects;

namespace Rbac.Application.Menus;

/// <summary>
/// project 全量菜單樹服務。
/// 读取顺序：IMenuTreeCache(FusionCache L1) → Redis L2 → MySQL 重建。
/// 不引用 Infrastructure.Redis，通過 IMenuTreeCache 接口訪問緩存。
/// </summary>
public sealed class RbacProjectMenuTreeService
{
    private readonly IMenuTreeCache _menuTreeCache;
    private readonly IRuleRepository _ruleRepository;
    private readonly ILogger<RbacProjectMenuTreeService> _logger;

    public RbacProjectMenuTreeService(
        IMenuTreeCache menuTreeCache,
        IRuleRepository ruleRepository,
        ILogger<RbacProjectMenuTreeService> logger)
    {
        _menuTreeCache = menuTreeCache;
        _ruleRepository = ruleRepository;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MenuNodeDto>> GetProjectMenuTreeAsync(
        string project, CancellationToken ct = default)
    {
        return await _menuTreeCache.GetMenuTreeAsync(
            project,
            async token =>
            {
                _logger.LogDebug("MenuTree cache miss, loading from MySQL project={Project}", project);
                return await BuildFromMySqlAsync(project, token);
            },
            ct) ?? Array.Empty<MenuNodeDto>();
    }

    public Task InvalidateAsync(string project) =>
        _menuTreeCache.EvictMenuTreeAsync(project);

    private async Task<IReadOnlyList<MenuNodeDto>> BuildFromMySqlAsync(
        string project, CancellationToken ct)
    {
        var rules = await _ruleRepository.FindActiveByProjectAsync(new ProjectCode(project), ct);
        if (rules.Count == 0)
        {
            _logger.LogWarning("No active rules found for project={Project}", project);
            return Array.Empty<MenuNodeDto>();
        }
        var sorted = rules.OrderBy(r => r.Weigh).ToList();
        var tree = sorted.ToMenuTree(parentRuleCode: null);
        _logger.LogDebug("MenuTree built project={Project} rootNodes={Count}", project, tree.Count);
        return tree;
    }
}
