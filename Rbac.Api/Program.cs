using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Nest;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using Rbac.Api.Authorization;
using Rbac.Api.Filters;
using Rbac.Api.Middleware;
using Rbac.Api.Options;
using Rbac.Api.Security;
using Rbac.Application.Auditing;
using Rbac.Application.Authorization;
using Rbac.Application.Cache;
using Rbac.Application.Identity;
using Rbac.Application.Management;
using Rbac.Application.Menus;
using Rbac.Application.Policies;
using Rbac.Application.Repositories;
using Rbac.Application.Search;
using Rbac.Application.Security;
using Rbac.Application.Serialization;
using Rbac.Application.Snapshots;
using Rbac.Infrastructure.Casbin;
using Rbac.Infrastructure.Elasticsearch.Bootstrap;
using Rbac.Infrastructure.Elasticsearch.Reindex;
using Rbac.Infrastructure.Elasticsearch.Services;
using Rbac.Infrastructure.MySql.Identity;
using Rbac.Infrastructure.MySql.Management;
using Rbac.Infrastructure.MySql.Mapping;
using Rbac.Infrastructure.MySql.Outbox;
using Rbac.Infrastructure.MySql.Policies;
using Rbac.Infrastructure.MySql.Repositories;
using Rbac.Infrastructure.Redis;
using Rbac.Application.Backend;

// ─── 注意：不引用 Rbac.Worker ────────────────────────────────────
// ChannelAuditEventEmitter / RbacAuditEventWorker 已移至
// Rbac.Application.Auditing，Api 可直接使用。

var builder = WebApplication.CreateBuilder(args);

// ── Options ───────────────────────────────────────────────────────
builder.Services.Configure<RbacJwtOptions>(
    builder.Configuration.GetSection(RbacJwtOptions.SectionName));
builder.Services.Configure<RbacProjectOptions>(
    builder.Configuration.GetSection(RbacProjectOptions.SectionName));
builder.Services.Configure<RbacAllowlistOptions>(
    builder.Configuration.GetSection(RbacAllowlistOptions.SectionName));
builder.Services.Configure<RbacProjectAccessAllowlistOptions>(
    builder.Configuration.GetSection(RbacProjectAccessAllowlistOptions.SectionName));
// RbacDxEIdGenerationOptions 在 Rbac.Infrastructure.MySql.Identity
builder.Services.Configure<RbacDxEIdGenerationOptions>(
    builder.Configuration.GetSection(RbacDxEIdGenerationOptions.SectionName));

// ── Authentication (JWT) ──────────────────────────────────────────
var jwtSection = builder.Configuration.GetSection(RbacJwtOptions.SectionName);
var jwtMode = jwtSection["Mode"] ?? "Oidc";
var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

if (string.Equals(jwtMode, "TrustedJwt", StringComparison.OrdinalIgnoreCase))
{
    authBuilder.AddScheme<AuthenticationSchemeOptions, TrustedJwtAuthenticationHandler>(
        JwtBearerDefaults.AuthenticationScheme, _ => { });
}
else
{
    authBuilder.AddJwtBearer(options =>
    {
        options.Authority            = jwtSection["Authority"];
        options.Audience             = jwtSection["Audience"];
        options.RequireHttpsMetadata = bool.Parse(jwtSection["RequireHttpsMetadata"] ?? "true");
    });
}

// ── Controllers + Global Filter + JSON ───────────────────────────
builder.Services.AddControllers(opt =>
{
    opt.Filters.Add<RbacAuthorizationFilter>(); // deny-by-default
})
.AddJsonOptions(opt =>
{
    opt.JsonSerializerOptions.Converters.Add(new LongToStringConverter());
});

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── Infrastructure: MySQL ─────────────────────────────────────────
builder.Services.AddDbContext<RbacDbContext>(opt =>
    opt.UseMySql(
        builder.Configuration.GetConnectionString("Rbac")!,
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("Rbac")!)));

// Outbox（同一 Scoped 实例实现两个接口）
builder.Services.AddScoped<OutboxReaderWriter>();
builder.Services.AddScoped<IOutboxWriter>(sp => sp.GetRequiredService<OutboxReaderWriter>());
builder.Services.AddScoped<IOutboxReader>(sp => sp.GetRequiredService<OutboxReaderWriter>());

// Repositories（PATCH-07）
builder.Services.AddScoped<IAdministratorRepository,   AdministratorRepository>();
builder.Services.AddScoped<IGroupRepository,           GroupRepository>();
builder.Services.AddScoped<IGroupMemberRepository,     GroupMemberRepository>();
builder.Services.AddScoped<IRuleRepository,            RuleRepository>();
builder.Services.AddScoped<IProjectGrantRepository,    ProjectGrantRepository>();
builder.Services.AddScoped<IApiPermissionMapRepository, ApiPermissionMapRepository>();
builder.Services.AddScoped<ICasbinPolicyRepository,    CasbinPolicyRepository>();
builder.Services.AddScoped<IProjectGrantMySqlReader,   ProjectGrantMySqlReader>();
builder.Services.AddScoped<RbacManagementWriteGuard>();
builder.Services.AddScoped<IRbacManagementWriteService, RbacManagementWriteService>();

// Casbin policy readers（PATCH-06）
builder.Services.AddScoped<ICasbinGroupingPolicyReader,   CasbinMySqlGroupingPolicyReader>();
builder.Services.AddScoped<ICasbinPermissionPolicyReader, CasbinMySqlPermissionPolicyReader>();

// DxEId generator（RbacDxEIdGenerationOptions 在 Infrastructure.MySql.Identity）
builder.Services.AddSingleton<IRbacDxEIdGenerator>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RbacDxEIdGenerationOptions>>().Value;
    return new SnowflakeDxEIdGenerator(opts);
});

// ── Infrastructure: Redis + FusionCache ───────────────────────────
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));

builder.Services.AddSingleton<IDatabase>(sp =>
    sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
builder.Services.AddSingleton<ISubscriber>(sp =>
    sp.GetRequiredService<IConnectionMultiplexer>().GetSubscriber());

builder.Services.AddFusionCache();

builder.Services.AddSingleton<RbacFusionCacheFacade>();
builder.Services.AddSingleton<IMenuTreeCache>(sp => sp.GetRequiredService<RbacFusionCacheFacade>());
builder.Services.AddSingleton<RbacFusionCacheEvictionHandler>();

builder.Services.AddScoped<IVersionStore,         RedisVersionStore>();
builder.Services.AddScoped<IPermsetOperations,    RedisPermsetOperations>();
builder.Services.AddScoped<IRbacPermsetBuilder,   RbacPermsetStore>();
builder.Services.AddScoped<IProjectGrantStore,    RbacProjectGrantCache>();
builder.Services.AddScoped<IRbacCacheInvalidator, RbacCacheInvalidator>();

// Snapshot service（PATCH-05）
builder.Services.AddScoped<IRbacSnapshotService, RbacSnapshotService>();

// Redis Pub/Sub 订阅端（驱逐 L1 缓存）
builder.Services.AddHostedService<RbacCacheInvalidationSubscriber>();

// ── Infrastructure: Elasticsearch ────────────────────────────────
builder.Services.AddSingleton<IElasticClient>(_ =>
{
    var uri = builder.Configuration["Elasticsearch:Uri"]!;
    return new ElasticClient(new ConnectionSettings(new Uri(uri)));
});

// RbacManagementSearchService 在 Infrastructure.Elasticsearch（不在 Application）
builder.Services.AddScoped<IRbacManagementSearchService, RbacManagementSearchService>();
builder.Services.AddScoped<RbacEsFullReindexService>();
// PATCH-11: RbacEsAliasPreflightChecker 已在 RbacEsBootstrap.cs 实现，注入 RbacEsFullReindexService
builder.Services.AddSingleton<RbacEsAliasPreflightChecker>();

// ── Infrastructure: Casbin ────────────────────────────────────────
builder.Services.AddSingleton<RbacCasbinModelProvider>();
builder.Services.AddSingleton<CasbinEnforcerFactory>();
builder.Services.AddSingleton<CasbinEnforcerProvider>();
builder.Services.AddSingleton<CasbinPolicyVersionWatcher>();
// ICasbinEnforcer 定义在 Rbac.Application.Authorization
builder.Services.AddSingleton<ICasbinEnforcer>(sp =>
    sp.GetRequiredService<CasbinEnforcerProvider>());

// ── Application: Security ─────────────────────────────────────────
builder.Services.AddScoped<ICurrentRbacContextAccessor, HttpContextRbacContextAccessor>();
builder.Services.AddScoped<IUserIdentityResolver,  JwtUserIdentityResolver>();  // PATCH-02
builder.Services.AddScoped<IProjectRequestReader,  HttpProjectRequestReader>(); // PATCH-03
builder.Services.AddScoped<IRbacProjectResolver,   RbacProjectResolver>();

// ── Application: Authorization ────────────────────────────────────
builder.Services.AddScoped<IRbacApiPermissionMapper, RoutePatternApiPermissionMapper>(); // PATCH-04
builder.Services.AddScoped<IRbacPermissionChecker,   RbacPermissionChecker>();
builder.Services.AddScoped<RbacVersionValidationService>();
builder.Services.AddScoped<RbacPermsetLazyRebuildCoordinator>();

// ── Application: Audit（PATCH-12）────────────────────────────────
// ChannelAuditEventEmitter 和 RbacAuditEventWorker 已移至 Application.Auditing
// Api 无需引用 Worker 项目
builder.Services.AddSingleton<IAuditEventEmitter, ChannelAuditEventEmitter>();
builder.Services.AddHostedService<RbacAuditEventWorker>();
builder.Services.AddScoped<IRbacAuthorizationAuditWriter, RbacAuthorizationAuditWriter>();

// ── Application: Menus ────────────────────────────────────────────
builder.Services.AddScoped<RbacMenuBuilder>();
builder.Services.AddScoped<RbacProjectMenuTreeService>();

// ── Observability ─────────────────────────────────────────────────
builder.Services.AddSingleton<Rbac.Application.Observability.RbacMetrics>();


// —— Controller
builder.Services.AddScoped<RbacBackendIndexService>();


// ── Build & Pipeline ─────────────────────────────────────────────
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseRouting();

// 管道顺序：Authentication → RbacContext → Authorization → Controllers
app.UseAuthentication();
app.UseMiddleware<CurrentRbacContextMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

app.Run();
