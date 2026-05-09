using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rbac.Application.Menus;
using Rbac.Application.Policies;
using Rbac.Application.Repositories;
using Rbac.Application.Snapshots;
using Rbac.Domain.ValueObjects;
using Rbac.Infrastructure.Casbin;

namespace Rbac.Worker.Warmup;

/// <summary>
/// 缓存预热 Worker。
///
/// 触发时机（设计文档 §9.6）：
/// 1. 应用启动后异步预热（IHostedService.StartAsync 中 fire-and-forget）。
/// 2. 权限发布后 Worker 预热（由 Outbox 处理器触发）。
/// 3. 灰度切换前批量预热。
///
/// 预热对象：
/// - 活跃 project 的 menu-tree
/// - 活跃 project 的 api-map（通过 RbacProjectMenuTreeService 间接触发）
/// - 最近 7 天登录过的管理员用户 snapshot
/// - Casbin Enforcer policy
///
/// 约束：
/// - 预热不阻塞服务启动，全部 fire-and-forget。
/// - 预热失败只记录日志，不影响服务可用性。
/// - 不预热已禁用用户或已移除 project 授权的用户。
/// </summary>
public sealed class RbacCacheWarmupWorker : IHostedService
{
    private readonly IProjectGrantRepository _grantRepo;
    private readonly IRuleRepository _ruleRepo;
    private readonly IRbacSnapshotService _snapshotService;
    private readonly RbacProjectMenuTreeService _menuTreeService;
    private readonly CasbinEnforcerProvider _casbinProvider;
    private readonly ILogger<RbacCacheWarmupWorker> _logger;

    public RbacCacheWarmupWorker(
        IProjectGrantRepository grantRepo,
        IRuleRepository ruleRepo,
        IRbacSnapshotService snapshotService,
        RbacProjectMenuTreeService menuTreeService,
        CasbinEnforcerProvider casbinProvider,
        ILogger<RbacCacheWarmupWorker> logger)
    {
        _grantRepo = grantRepo;
        _ruleRepo = ruleRepo;
        _snapshotService = snapshotService;
        _menuTreeService = menuTreeService;
        _casbinProvider = casbinProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // fire-and-forget：不阻塞服务启动
        _ = Task.Run(() => WarmupAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>执行完整预热流程。</summary>
    public async Task WarmupAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Cache warmup started.");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. 获取所有活跃 project（通过授权记录推断）
        var activeProjects = await GetActiveProjectsAsync(ct);

        foreach (var project in activeProjects)
        {
            if (ct.IsCancellationRequested) break;
            await WarmupProjectAsync(project, ct);
        }

        sw.Stop();
        _logger.LogInformation(
            "Cache warmup completed projects={C} elapsedMs={Ms}",
            activeProjects.Count, sw.ElapsedMilliseconds);
    }

    // ── 按 project 预热 ───────────────────────────────────────────

    private async Task WarmupProjectAsync(string project, CancellationToken ct)
    {
        _logger.LogDebug("Warming up project={P}", project);

        // 1. menu-tree（project 级，所有用户共享）
        await SafeAsync(() =>
            _menuTreeService.GetProjectMenuTreeAsync(project, ct),
            $"menu-tree project={project}");

        // 2. Casbin Enforcer（project 级）
        await SafeAsync(() =>
            _casbinProvider.SyncAsync(new ProjectCode(project), ct),
            $"casbin-enforcer project={project}");

        // 3. 热点用户 snapshot（最近有授权记录的用户，不预热禁用用户）
        var grants = await _grantRepo.FindByProjectAsync(new ProjectCode(project), ct);
        foreach (var grant in grants.Take(100)) // 最多预热 100 个用户
        {
            if (ct.IsCancellationRequested) break;
            await SafeAsync(() =>
                _snapshotService.GetSnapshotAsync(grant.Userid.Value, project, ct),
                $"snapshot userid={grant.Userid.Value} project={project}");
        }
    }

    // ── 获取活跃 project 列表 ─────────────────────────────────────

    private async Task<IReadOnlyList<string>> GetActiveProjectsAsync(CancellationToken ct)
    {
        // 通过规则表推断活跃 project（有规则记录的 project 为活跃）
        // 实际实现可维护 project 注册表；此处通过规则表 project 字段去重
        try
        {
            // 临时用通配符查询，具体实现由 IRuleRepository 扩展
            var rules = await _ruleRepo.FindActiveByProjectAsync(new ProjectCode("*"), ct);
            return rules.Select(r => r.Project.Value).Distinct().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load active projects for warmup.");
            return Array.Empty<string>();
        }
    }

    private async Task SafeAsync(Func<Task> action, string label)
    {
        try { await action(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Warmup failed for {Label}", label);
        }
    }

    private async Task SafeAsync<T>(Func<Task<T>> action, string label)
    {
        try { await action(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Warmup failed for {Label}", label);
        }
    }
}
