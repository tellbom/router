using Microsoft.EntityFrameworkCore;
using Nest;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using Rbac.Application.Auditing;
using Rbac.Application.Authorization;
using Rbac.Application.Cache;
using Rbac.Application.Management;
using Rbac.Application.Menus;
using Rbac.Application.Policies;
using Rbac.Application.Repositories;
using Rbac.Application.Search;
using Rbac.Application.Security;
using Rbac.Application.Snapshots;
using Rbac.Infrastructure.Casbin;
using Rbac.Infrastructure.Elasticsearch.Bootstrap;
using Rbac.Infrastructure.Elasticsearch.Reindex;
using Rbac.Infrastructure.Elasticsearch.Services;
using Rbac.Infrastructure.DM.Management;
using Rbac.Infrastructure.DM.Mapping;
using Rbac.Infrastructure.DM.Outbox;
using Rbac.Infrastructure.DM.Policies;
using Rbac.Infrastructure.DM.Repositories;
using Rbac.Infrastructure.Redis;
using Rbac.Worker.Cache;
using Rbac.Worker.Outbox;
using Rbac.Worker.Reindex;
using Rbac.Worker.Warmup;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        // ── Options ───────────────────────────────────────────────
        // ── Infrastructure: relational database ───────────────────
        services.AddDbContext<RbacDbContext>(opt =>
            opt.UseRbacRelationalDatabase(
                config["Database:Provider"],
                config.GetConnectionString("Rbac")!));

        // Outbox（同一 Scoped 实例实现两个接口）
        services.AddScoped<OutboxReaderWriter>();
        services.AddScoped<IOutboxWriter>(sp => sp.GetRequiredService<OutboxReaderWriter>());
        services.AddScoped<IOutboxReader>(sp => sp.GetRequiredService<OutboxReaderWriter>());

        // Repositories（PATCH-07）
        services.AddScoped<IAdministratorRepository,   AdministratorRepository>();
        services.AddScoped<IGroupRepository,           GroupRepository>();
        services.AddScoped<IGroupMemberRepository,     GroupMemberRepository>();
        services.AddScoped<IRuleRepository,            RuleRepository>();
        services.AddScoped<IProjectGrantRepository,    ProjectGrantRepository>();
        services.AddScoped<IApiPermissionMapRepository, ApiPermissionMapRepository>();
        services.AddScoped<ICasbinPolicyRepository,    CasbinPolicyRepository>();
        services.AddScoped<IProjectGrantMySqlReader,   ProjectGrantMySqlReader>();
        services.AddScoped<RbacManagementWriteGuard>();
        services.AddScoped<IRbacManagementWriteService, RbacManagementWriteService>();

        // Casbin policy readers（PATCH-06）
        services.AddScoped<ICasbinGroupingPolicyReader,   CasbinMySqlGroupingPolicyReader>();
        services.AddScoped<ICasbinPermissionPolicyReader, CasbinMySqlPermissionPolicyReader>();

        // ── Infrastructure: Redis + FusionCache ───────────────────
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(config.GetConnectionString("Redis")!));

        services.AddSingleton<IDatabase>(sp =>
            sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
        services.AddSingleton<ISubscriber>(sp =>
            sp.GetRequiredService<IConnectionMultiplexer>().GetSubscriber());

        services.AddFusionCache();

        services.AddSingleton<RbacFusionCacheFacade>();
        services.AddSingleton<IMenuTreeCache>(sp => sp.GetRequiredService<RbacFusionCacheFacade>());
        services.AddSingleton<RbacFusionCacheEvictionHandler>();

        services.AddScoped<IVersionStore,         RedisVersionStore>();
        services.AddScoped<IPermsetOperations,    RedisPermsetOperations>();
        services.AddScoped<IRbacPermsetBuilder,   RbacPermsetStore>();
        services.AddScoped<IProjectGrantStore,    RbacProjectGrantCache>();
        services.AddScoped<IRbacCacheInvalidator, RbacCacheInvalidator>();

        // Snapshot service（PATCH-05）
        services.AddScoped<IRbacSnapshotService, RbacSnapshotService>();

        // ── Infrastructure: Elasticsearch ─────────────────────────
        services.AddSingleton<IElasticClient>(_ =>
        {
            var uri = config["Elasticsearch:Uri"]!;
            return new ElasticClient(new ConnectionSettings(new Uri(uri)));
        });

        services.AddScoped<RbacEsFullReindexService>();
        // PATCH-11: RbacEsAliasPreflightChecker 已在 RbacEsBootstrap.cs 实现，注入 RbacEsFullReindexService
        services.AddSingleton<RbacEsAliasPreflightChecker>();
        // IRbacManagementSearchService 在 Rbac.Application.Search（using 已加）
        services.AddScoped<IRbacManagementSearchService, RbacManagementSearchService>();

        // ── Infrastructure: Casbin ────────────────────────────────
        services.AddSingleton<RbacCasbinModelProvider>();
        services.AddSingleton<CasbinEnforcerFactory>();
        services.AddSingleton<CasbinEnforcerProvider>();
        services.AddSingleton<CasbinPolicyVersionWatcher>();
        // ICasbinEnforcer 在 Rbac.Application.Authorization（using 已加）
        services.AddSingleton<ICasbinEnforcer>(sp =>
            sp.GetRequiredService<CasbinEnforcerProvider>());

        // ── Application Services ──────────────────────────────────
        services.AddScoped<RbacVersionValidationService>();
        services.AddScoped<RbacPermsetLazyRebuildCoordinator>();
        services.AddScoped<RbacProjectMenuTreeService>();

        // Outbox processors（Scoped：依赖 Repository 等 Scoped 服务）
        services.AddScoped<RbacRedisOutboxProcessor>();
        services.AddScoped<RbacElasticsearchOutboxProcessor>();
        services.AddScoped<RbacCasbinOutboxProcessor>();

        // 非 HostedService 辅助 Worker（由 Outbox 处理器调用）
        services.AddScoped<RbacPermsetInvalidationWorker>();

        // 手动触发 Reindex Worker
        services.AddScoped<RbacEsReindexWorker>();

        // ── Audit（PATCH-12）──────────────────────────────────────
        // ChannelAuditEventEmitter / RbacAuditEventWorker 已移至 Application.Auditing
        // using Rbac.Application.Auditing 已在文件顶部引入
        services.AddSingleton<IAuditEventEmitter, ChannelAuditEventEmitter>();

        // ── Observability ─────────────────────────────────────────
        services.AddSingleton<Rbac.Application.Observability.RbacMetrics>();

        // ── HostedServices（注册顺序 = 启动顺序）────────────────
        services.AddHostedService<RbacAuditEventWorker>();    // PATCH-12：审计消费
        services.AddHostedService<RbacCacheWarmupWorker>();   // 启动预热（内部创建 Scope）
        services.AddHostedService<RbacOutboxPollingWorker>(); // PATCH-09：Outbox 轮询
    })
    .Build();

await host.RunAsync();
