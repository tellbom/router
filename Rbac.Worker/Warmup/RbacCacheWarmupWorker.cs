using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rbac.Application.Menus;
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
///
/// 生命周期修正（smoke test 发现）：
/// IHostedService 由 DI 以 Singleton 生命周期托管。
/// 原实现直接在构造函数注入 Scoped 服务（IProjectGrantRepository 等），
/// 导致 .NET DI 生命周期校验报错（Singleton 持有 Scoped）。
/// 修正方案：只注入 IServiceScopeFactory，每次 WarmupAsync 执行时
/// 创建独立 Scope，从 Scope 中解析 Scoped 依赖，用完即释放。
/// 模式与 RbacOutboxPollingWorker.ProcessBatchAsync 完全一致。
/// </summary>
public sealed class RbacCacheWarmupWorker : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    // CasbinEnforcerProvider 是 Singleton，可以直接持有
    private readonly CasbinEnforcerProvider _casbinProvider;
    private readonly ILogger<RbacCacheWarmupWorker> _logger;

    public RbacCacheWarmupWorker(
        IServiceScopeFactory scopeFactory,
        CasbinEnforcerProvider casbinProvider,
        ILogger<RbacCacheWarmupWorker> logger)
    {
        _scopeFactory  = scopeFactory;
        _casbinProvider = casbinProvider;
        _logger        = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // fire-and-forget：不阻塞服务启动
        _ = Task.Run(() => WarmupAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// 执行完整预热流程。
    /// 每次调用创建独立 Scope，保证 DbContext / Repository 等 Scoped 服务
    /// 在预热结束后正确释放，不跨请求持有连接。
    /// </summary>
    public async Task WarmupAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Cache warmup started.");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 每次预热使用独立 Scope，Scoped 服务（DbContext 等）在 using 结束时释放
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var grantRepo       = sp.GetRequiredService<IProjectGrantRepository>();
        var ruleRepo        = sp.GetRequiredService<IRuleRepository>();
        var snapshotService = sp.GetRequiredService<IRbacSnapshotService>();
        var menuTreeService = sp.GetRequiredService<RbacProjectMenuTreeService>();

        var activeProjects = await GetActiveProjectsAsync(ruleRepo, ct);

        foreach (var project in activeProjects)
        {
            if (ct.IsCancellationRequested) break;
            await WarmupProjectAsync(
                project, grantRepo, snapshotService, menuTreeService, ct);
        }

        sw.Stop();
        _logger.LogInformation(
            "Cache warmup completed projects={C} elapsedMs={Ms}",
            activeProjects.Count, sw.ElapsedMilliseconds);
    }

    // ── 按 project 预热 ───────────────────────────────────────────

    private async Task WarmupProjectAsync(
        string project,
        IProjectGrantRepository grantRepo,
        IRbacSnapshotService snapshotService,
        RbacProjectMenuTreeService menuTreeService,
        CancellationToken ct)
    {
        _logger.LogDebug("Warming up project={P}", project);

        // 1. menu-tree（project 级，所有用户共享）
        await SafeAsync(
            () => menuTreeService.GetProjectMenuTreeAsync(project, ct),
            $"menu-tree project={project}");

        // 2. Casbin Enforcer（Singleton，直接调用）
        await SafeAsync(
            () => _casbinProvider.SyncAsync(new ProjectCode(project), ct),
            $"casbin-enforcer project={project}");

        // 3. 热点用户 snapshot（最多预热 100 个用户）
        var grants = await grantRepo.FindByProjectAsync(new ProjectCode(project), ct);
        foreach (var grant in grants.Take(100))
        {
            if (ct.IsCancellationRequested) break;
            await SafeAsync(
                () => snapshotService.GetSnapshotAsync(grant.Userid.Value, project, ct),
                $"snapshot userid={grant.Userid.Value} project={project}");
        }
    }

    // ── 获取活跃 project 列表 ─────────────────────────────────────

    private async Task<IReadOnlyList<string>> GetActiveProjectsAsync(
        IRuleRepository ruleRepo, CancellationToken ct)
    {
        try
        {
            var rules = await ruleRepo.FindActiveByProjectAsync(new ProjectCode("*"), ct);
            return rules.Select(r => r.Project.Value).Distinct().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load active projects for warmup.");
            return Array.Empty<string>();
        }
    }

    // ── SafeAsync 辅助 ────────────────────────────────────────────

    private async Task SafeAsync(Func<Task> action, string label)
    {
        try   { await action(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Warmup failed for {Label}", label);
        }
    }

    private async Task SafeAsync<T>(Func<Task<T>> action, string label)
    {
        try   { await action(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Warmup failed for {Label}", label);
        }
    }
}
