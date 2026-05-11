# RBAC Patch Plan v2 — 修正版

**生成时间**: 2026-05-10  
**版本**: v2（对 v1 中 5 处错误进行代码核实并修正）  
**执行依据**: 对分析报告逐条代码核查 + 对 v1 patch plan 自身 5 个修正点的独立二次核实

---

## 一、v1 → v2 修正说明（5 条修正点的核实结论）

### 修正点 1：C9 / PATCH-11 — ES alias preflight 状态

**外部评审意见**：`RbacEsBootstrap.cs` 已有 `RbacEsAliasPreflightChecker` 类和 `CheckAsync` 逻辑，patch plan 说"缺失逻辑"不准确。

**代码核实结果**：**完全确认**。

`RbacEsBootstrap.cs` 中确实存在完整的 `RbacEsAliasPreflightChecker` 类，包含：
- `CheckAsync(string alias)` 完整实现
- 验证 alias 存在性（`GetAliasAsync`）
- 验证 alias 指向唯一索引（`Indices.Count > 1` 检测）
- 返回 `AliasPreflightResult.Pass/Fail` 结果类

**真实缺口**：`RbacEsFullReindexService` 构造函数中**没有注入** `RbacEsAliasPreflightChecker`，`ExecuteReindexAsync` 方法也**没有调用** `CheckAsync`。问题是"接入"缺失，不是"逻辑"缺失。

**v1 错误描述**：  
> "在现有 Bootstrap 中补充 preflight 方法"

**v2 正确描述**：  
> 将已有的 `RbacEsAliasPreflightChecker` 注入 `RbacEsFullReindexService`，并在 `ExecuteReindexAsync` 开头调用 `CheckAsync`，结果为 `Fail` 时直接返回 `ReindexResult.Failure`。同时在 DI 中注册 `RbacEsAliasPreflightChecker`（当前未注册）。

---

### 修正点 2：PATCH-12 — Audit Channel 类型和已有实现

**外部评审意见**：v1 写的是 `Channel<AuditEvent>`，实际类型是 `RbacAuditEvent`；`ChannelAuditEventEmitter` 已存在于 `Rbac.Worker/Auditing/RbacAuditEventWorker.cs`，不应重新建类。

**代码核实结果**：**完全确认**。

`Rbac.Worker/Auditing/RbacAuditEventWorker.cs` 中已有：
- `Channel<RbacAuditEvent>`（`BoundedChannelOptions(10_000)`，`DropOldest`）
- `ChannelAuditEventEmitter`（实现 `IAuditEventEmitter`，通过静态 `RbacAuditEventWorker.Writer` 写入）
- `RbacAuditEventWorker`（`BackgroundService`，通过 `_channel.Reader.ReadAllAsync` 消费）

`IAuditEventEmitter` 接口签名为 `Task EmitAsync(RbacAuditEvent auditEvent)`，与 `AuditEvent` 无关。

**v1 错误内容**：
```csharp
// 错误：类型名和 Channel 都是新建的，与现有代码冲突
services.AddSingleton<Channel<AuditEvent>>(_ => Channel.CreateBounded<AuditEvent>(...));
services.AddSingleton<IAuditEventEmitter, ChannelAuditEventEmitter>();
services.AddHostedService<RbacAuditEventWorker>();
// 并附有"如不存在，需新建 ChannelAuditEventEmitter.cs"的错误提示
```

**v2 正确内容**：不创建任何新文件，只在 `Program.cs` 中注册已有类：
```csharp
// Rbac.Api/Program.cs 和 Rbac.Worker/Program.cs 均需注册：
services.AddSingleton<IAuditEventEmitter, ChannelAuditEventEmitter>();
services.AddHostedService<RbacAuditEventWorker>();
// 不需要手动注册 Channel<RbacAuditEvent>，Worker 内部使用静态 Channel
```

---

### 修正点 3：PATCH-13 — Directory.Build.props 会破坏 build

**外部评审意见**：`RbacGroup.RuleCodes` / `PermissionCodes` 已有 `CS8618` warning，先启用 `TreatWarningsAsErrors` 会让 build 从通过变失败。

**代码核实结果**：**完全确认**。

`Rbac.Domain/Groups/RbacGroup.cs` 中：
```csharp
public IReadOnlyList<RuleCode> RuleCodes { get; private set; }      // 无初始值 → CS8618
public IReadOnlyList<PermissionCode> PermissionCodes { get; private set; }  // 同上
```

私有无参构造器加上 EF Core 反射实例化路径，无法在声明时给定初始值——实际由 `Create()` 工厂方法或 `UpdateRules()` 赋值。当前 build 通过是因为没有 `TreatWarningsAsErrors`。

**执行顺序要求**（v2 严格约束）：
1. 先修 `RbacGroup.cs`：在属性声明加 `= Array.Empty<RuleCode>()` / `= Array.Empty<PermissionCode>()`（对 EF Core 反射安全，工厂方法会随即覆盖）。
2. 确认 `dotnet build` 仍 0 errors 0 warnings（仅 Domain/Application 相关）。
3. 再新建 `Directory.Build.props`，启用 `TreatWarningsAsErrors`。

不允许在修 `RbacGroup.cs` 之前添加 `Directory.Build.props`。

---

### 修正点 4：PATCH-09 — Outbox 重试状态逻辑

**外部评审意见**：v1 中失败时设置 `Status = Failed`，而 `FetchPendingAsync` 只取 `Pending`，导致失败事件永远不会重试。

**代码核实结果**：**完全确认**。

`IOutboxReader.FetchPendingAsync` 契约注释明确：
> "读取待处理的 Outbox 事件（Status=Pending 且 NextRetryAt <= now）"

`OutboxStatus` 设计了四种状态：`Pending / Processing / Succeeded / Failed`。

`MarkFailedAsync` 契约：
> "标记事件处理失败，递增重试次数，设置下次重试时间"

这说明设计意图是：失败后**保留 `Pending` 状态**（或允许 `FetchPendingAsync` 同时取 `Status=Pending OR Status=Failed`），而不是立即锁死到 `Failed` 不可重试。

**v1 错误代码**：
```csharp
catch (Exception ex)
{
    await reader.MarkFailedAsync(evt.EventId, evt.RetryCount + 1, nextRetry, stoppingToken);
    // 这里 MarkFailed 若将 Status 设为 Failed，则事件永远不被重新取出
}
```

**v2 正确逻辑**（两种可选实现，执行 Agent 选其一）：

**选项 A（推荐）**：失败时保持 `Status = Pending`，更新 `RetryCount` 和 `NextRetryAt`，超过最大重试（如 5 次）再改为 `Failed`：
```csharp
const int MaxRetry = 5;
catch (Exception ex)
{
    var newRetryCount = evt.RetryCount + 1;
    if (newRetryCount >= MaxRetry)
    {
        // 超过最大重试，标记 Failed（DLQ 语义）
        await reader.MarkFailedAsync(evt.EventId, newRetryCount, DateTimeOffset.MaxValue, stoppingToken);
        _logger.LogError(ex, "Outbox DLQ eventId={Id} retries={N}", evt.EventId, newRetryCount);
    }
    else
    {
        // 保持 Pending，设置退避延迟
        var nextRetry = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, newRetryCount) * 5);
        await reader.MarkFailedAsync(evt.EventId, newRetryCount, nextRetry, stoppingToken);
        // MarkFailedAsync 实现中 Status 必须保持 Pending，否则选选项 B
    }
}
```

**选项 B**：若 `MarkFailedAsync` 内部确实写 `Status = Failed`，则 `FetchPendingAsync` 改为取 `Status IN ('Pending', 'Failed') AND NextRetryAt <= now AND RetryCount < MaxRetry`。

**原则**：两者任选，但必须保证失败事件在 `RetryCount < MaxRetry` 时最终仍会被重新消费。

---

### 修正点 5：PATCH-01 — 不要只注册 RazorPages

**外部评审意见**：判断正确，需补 `AddControllers` / filter / middleware / auth / DI。

**代码核实结果**：**完全确认**。

`Rbac.Api/Program.cs` 当前：
```csharp
builder.Services.AddRazorPages();
// ...
app.MapRazorPages();
```

这是纯 RazorPages 配置。RBAC 的 Controller、Filter、Middleware、JWT Auth 均不存在于管线中。`RbacAuthorizationFilter`（实现 `IAsyncActionFilter`）挂在 MVC pipeline 上，如果没有 `AddControllers`，Filter 永远不会执行。

**v2 明确约束**：`AddRazorPages()` 可保留（如项目仍需要 Razor 页面），但必须**额外**补充：
```csharp
builder.Services.AddControllers(opt =>
{
    opt.Filters.Add<RbacAuthorizationFilter>();
})
.AddJsonOptions(opt =>
{
    // DxEId long → string 全局转换
    opt.JsonSerializerOptions.Converters.Add(new LongToStringConverter());
});
// ... 其余 DI 注册 ...
app.MapControllers(); // 在 app.MapRazorPages() 之前或之后均可
```

---

## 二、完整修正后补丁清单（v2）

### PATCH-01：Api/Worker 启动接线（P1）✱已修正

**修改文件**：`Rbac.Api/Program.cs`，`Rbac.Worker/Program.cs`

**Api/Program.cs 完整变更清单**：

```csharp
// ── Authentication ────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt => builder.Configuration.GetSection("RbacJwt").Bind(opt));

// ── Options ───────────────────────────────────────────────────────
builder.Services.Configure<RbacJwtOptions>(builder.Configuration.GetSection("RbacJwt"));
builder.Services.Configure<RbacProjectOptions>(builder.Configuration.GetSection("RbacProject"));
builder.Services.Configure<RbacAllowlistOptions>(builder.Configuration.GetSection("RbacAllowlist"));

// ── MVC + Filter + JSON ───────────────────────────────────────────
builder.Services.AddControllers(opt =>
{
    opt.Filters.Add<RbacAuthorizationFilter>(); // deny-by-default
})
.AddJsonOptions(opt =>
{
    opt.JsonSerializerOptions.Converters.Add(new LongToStringConverter()); // DxEId string
});

// ── Http Context / Scoped context accessor ────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentRbacContextAccessor, HttpContextRbacContextAccessor>();

// ── Application Services ──────────────────────────────────────────
builder.Services.AddScoped<IUserIdentityResolver, JwtUserIdentityResolver>();    // PATCH-02
builder.Services.AddScoped<IProjectRequestReader, HttpProjectRequestReader>();   // PATCH-03
builder.Services.AddScoped<IRbacProjectResolver, RbacProjectResolver>();
builder.Services.AddScoped<IRbacPermissionChecker, RbacPermissionChecker>();
builder.Services.AddScoped<IRbacApiPermissionMapper, RoutePatternApiPermissionMapper>(); // PATCH-04
builder.Services.AddScoped<IRbacSnapshotService, RbacSnapshotService>();        // PATCH-05
builder.Services.AddScoped<RbacMenuBuilder>();
builder.Services.AddScoped<RbacProjectMenuTreeService>();
builder.Services.AddScoped<RbacVersionValidationService>();
builder.Services.AddScoped<RbacPermsetLazyRebuildCoordinator>();
builder.Services.AddScoped<RbacAuthorizationAuditWriter>();
builder.Services.AddScoped<ProjectAuthorizationAuditService>();

// ── Infrastructure: MySQL ─────────────────────────────────────────
builder.Services.AddDbContext<RbacDbContext>(opt =>
    opt.UseMySQL(builder.Configuration.GetConnectionString("Rbac")!));
builder.Services.AddScoped<IOutboxWriter, OutboxReaderWriter>();       // PATCH-08
builder.Services.AddScoped<IOutboxReader, OutboxReaderWriter>();       // PATCH-08
builder.Services.AddScoped<IAdministratorRepository, AdministratorRepository>();  // PATCH-07
builder.Services.AddScoped<IGroupRepository, GroupRepository>();
builder.Services.AddScoped<IRuleRepository, RuleRepository>();
builder.Services.AddScoped<IProjectGrantRepository, ProjectGrantRepository>();
builder.Services.AddScoped<IApiPermissionMapRepository, ApiPermissionMapRepository>();
builder.Services.AddScoped<ICasbinPolicyRepository, CasbinPolicyRepository>();
builder.Services.AddScoped<IProjectGrantMySqlReader, ProjectGrantMySqlReader>();
builder.Services.AddScoped<ICasbinGroupingPolicyReader, CasbinMySqlGroupingPolicyReader>(); // PATCH-06
builder.Services.AddScoped<ICasbinPermissionPolicyReader, CasbinMySqlPermissionPolicyReader>();

// ── Infrastructure: Redis / FusionCache ──────────────────────────
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"]!));
builder.Services.AddFusionCache();
builder.Services.AddSingleton<RbacFusionCacheFacade>();
builder.Services.AddScoped<RbacPermsetStore>();
builder.Services.AddScoped<RbacProjectGrantCache>();
builder.Services.AddScoped<IRbacCacheInvalidator, RbacCacheInvalidator>();

// ── Infrastructure: Elasticsearch ────────────────────────────────
builder.Services.AddSingleton<IElasticClient>(_ =>
{
    var settings = new ConnectionSettings(new Uri(builder.Configuration["Elasticsearch:Uri"]!));
    return new ElasticClient(settings);
});
builder.Services.AddScoped<IRbacManagementSearchService, RbacManagementSearchService>();
builder.Services.AddSingleton<RbacEsAliasPreflightChecker>();  // PATCH-11（接入，非新建）

// ── Infrastructure: Casbin ────────────────────────────────────────
builder.Services.AddSingleton<RbacCasbinModelProvider>();
builder.Services.AddSingleton<CasbinEnforcerFactory>();
builder.Services.AddSingleton<CasbinEnforcerProvider>();
builder.Services.AddScoped<ICasbinEnforcer>(sp => sp.GetRequiredService<CasbinEnforcerProvider>());

// ── Audit（注册已有类，不新建）────────────────────────────────────
builder.Services.AddSingleton<IAuditEventEmitter, ChannelAuditEventEmitter>(); // PATCH-12 修正
builder.Services.AddHostedService<RbacAuditEventWorker>();                     // PATCH-12 修正

// ── Pipeline ─────────────────────────────────────────────────────
var app = builder.Build();
app.UseAuthentication();
app.UseMiddleware<CurrentRbacContextMiddleware>();  // 必须在 UseAuthentication 之后
app.UseAuthorization();
app.MapControllers();
app.MapRazorPages();  // 如仍需要
app.Run();
```

**Worker/Program.cs**：去掉 HTTP 相关（`AddControllers`、`AddHttpContextAccessor`、Middleware、`MapControllers`），其余 Infrastructure 注册相同；额外补充：
```csharp
services.AddHostedService<RbacOutboxPollingWorker>();  // PATCH-09
services.AddHostedService<RbacCacheWarmupWorker>();
services.AddHostedService<RbacPermsetInvalidationWorker>();
services.AddHostedService<RbacAuditEventWorker>();     // PATCH-12 修正
services.AddSingleton<IAuditEventEmitter, ChannelAuditEventEmitter>(); // PATCH-12 修正
```

---

### PATCH-02：JwtUserIdentityResolver 实现（P1）

**新建文件**：`Rbac.Api/Security/JwtUserIdentityResolver.cs`

```csharp
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Rbac.Api.Options;
using Rbac.Application.Security;

namespace Rbac.Api.Security;

public sealed class JwtUserIdentityResolver : IUserIdentityResolver
{
    private readonly RbacJwtOptions _options;

    public JwtUserIdentityResolver(IOptions<RbacJwtOptions> options)
        => _options = options.Value;

    public string? ResolveUserId(ClaimsPrincipal principal)
    {
        var val = principal.FindFirstValue(_options.UseridClaim);
        if (!string.IsNullOrWhiteSpace(val)) return val;

        foreach (var fallback in _options.FallbackUseridClaims)
        {
            val = principal.FindFirstValue(fallback);
            if (!string.IsNullOrWhiteSpace(val)) return val;
        }
        return null;
    }
}
```

---

### PATCH-03：HttpProjectRequestReader 实现（P1）

**新建文件**：`Rbac.Api/Security/HttpProjectRequestReader.cs`

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Rbac.Api.Options;
using Rbac.Application.Security;

namespace Rbac.Api.Security;

public sealed class HttpProjectRequestReader : IProjectRequestReader
{
    private readonly RbacProjectOptions _options;

    public HttpProjectRequestReader(IOptions<RbacProjectOptions> options)
        => _options = options.Value;

    public string? ReadProject(HttpContext context)
    {
        foreach (var source in _options.AllowedSources)
        {
            var val = source switch
            {
                "Header" => context.Request.Headers[_options.HeaderName].FirstOrDefault(),
                "Route"  => context.GetRouteValue("project")?.ToString(),
                "Query"  => context.Request.Query[_options.QueryKey].FirstOrDefault(),
                _        => null,
            };
            if (!string.IsNullOrWhiteSpace(val)) return val;
        }
        return null;
    }
}
```

---

### PATCH-04：RoutePatternApiPermissionMapper 实现（P1）

**新建文件**：`Rbac.Api/Authorization/RoutePatternApiPermissionMapper.cs`

核心逻辑：
1. 通过 `RbacFusionCacheFacade` 取 project 下所有 `RbacApiPermissionMap`（缓存 key `rbac:api-map:{project}`）。
2. 过滤 `Status = Active`，按 `HttpMethod` 分组。
3. 对每条记录用 `RouteTemplate.TryParse` + `TemplateMatcher` 匹配当前请求路径。
4. 命中第一条返回 `ApiPermissionMapping`；未命中返回 `null`。

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Template;
using Rbac.Application.Authorization;
using Rbac.Application.Repositories;
using Rbac.Domain.ValueObjects;
using Rbac.Infrastructure.Redis;

namespace Rbac.Api.Authorization;

public sealed class RoutePatternApiPermissionMapper : IRbacApiPermissionMapper
{
    private readonly IApiPermissionMapRepository _repo;
    private readonly RbacFusionCacheFacade _cache;

    public RoutePatternApiPermissionMapper(
        IApiPermissionMapRepository repo, RbacFusionCacheFacade cache)
    {
        _repo = repo;
        _cache = cache;
    }

    public async Task<ApiPermissionMapping?> ResolveAsync(
        string project, HttpContext context, CancellationToken ct = default)
    {
        var method = context.Request.Method.ToUpperInvariant();
        var path = context.Request.Path.Value ?? string.Empty;

        // 从缓存/DB 取所有 active 映射
        var maps = await _cache.GetOrSetApiMapAsync(project,
            () => _repo.FindActiveByProjectAsync(new ProjectCode(project), ct));

        foreach (var map in maps.Where(m => m.HttpMethod == method))
        {
            var template = TemplateParser.Parse(map.RoutePattern);
            var matcher = new TemplateMatcher(template,
                new RouteValueDictionary());
            if (matcher.TryMatch(path, new RouteValueDictionary()))
            {
                return new ApiPermissionMapping
                {
                    PermissionCode = map.PermissionCode.Value,
                    Action = map.Action,
                    MatchedRoutePattern = map.RoutePattern,
                };
            }
        }
        return null;
    }
}
```

---

### PATCH-05：RbacSnapshotService 实现（P1）

**新建文件**：`Rbac.Infrastructure.Redis/RbacSnapshotService.cs`

实现 `IRbacSnapshotService`，读取顺序：FusionCache L1 → Redis GET → MySQL/Casbin 重建。  
重建时严格执行 version compare-before-write（`versionAtStart` 与当前版本比较，版本已推进则丢弃结果）。

```csharp
public async Task<UserPermissionSnapshot?> RebuildSnapshotAsync(
    string userid, string project, CancellationToken ct = default)
{
    var versionAtStart = await _versionStore.ReadUserVersionAsync(project, userid);

    // 从 MySQL 拉取数据构建快照
    var projectCode = new ProjectCode(project);
    var groupings = await _groupingReader.LoadAsync(projectCode, ct);
    var permissions = await _permissionReader.LoadAsync(projectCode, ct);

    var userGroups = groupings
        .Where(g => g.Userid == userid)
        .Select(g => g.GroupCode)
        .ToList();

    var permCodes = permissions
        .Where(p => userGroups.Contains(p.GroupCode))
        .Select(p => p.PermissionCode)
        .Distinct()
        .ToList();

    // version compare-before-write
    var versionNow = await _versionStore.ReadUserVersionAsync(project, userid);
    if (versionNow > versionAtStart)
    {
        _logger.LogDebug("Snapshot rebuild discarded (version advanced) userid={U} project={P}", userid, project);
        return null; // 调用方重新 GetSnapshotAsync
    }

    var snapshot = new UserPermissionSnapshot
    {
        Userid = userid,
        Project = project,
        Groups = userGroups,
        PermissionCodes = permCodes,
        Versions = new SnapshotVersions
        {
            User = versionNow,
            Project = await _versionStore.ReadProjectVersionAsync(project),
            Policy = await _versionStore.ReadPolicyVersionAsync(project),
        },
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // 写 Redis + FusionCache
    await _redisDb.StringSetAsync(
        RbacRedisKeys.Snapshot(project, userid),
        JsonSerializer.Serialize(snapshot),
        TimeSpan.FromMinutes(30));
    _fusionCache.Set(RbacRedisKeys.Snapshot(project, userid), snapshot);

    return snapshot;
}
```

---

### PATCH-06：Casbin MySQL Policy Reader 实现（P1）

**新建文件**：`Rbac.Infrastructure.MySql/Policies/CasbinMySqlPolicyReaders.cs`

注意：两个接口（`ICasbinGroupingPolicyReader`、`ICasbinPermissionPolicyReader`）均在 `ICasbinPolicyReaders.cs` 中定义，实现类放在 Infrastructure.MySql，不要重复定义接口。

实现从 `RbacDbContext` 读取：
- `Grouping`：从 `rbac_group` 展开 `(userid → groupCode)` 关系（需要中间表或从 `RbacGroup.Members` 反查）
- `Permission`：从 `RbacGroup.PermissionCodes` 展开 `(groupCode, project, permCode, action)`

**注意**：`action` 字段可从 `PermissionCode` 的格式中推断（最后一段），或通过关联 `rbac_api_permission_map` 获取。具体方案由执行 Agent 根据实际表结构决定，但不得从 Redis/ES 反向读取。

---

### PATCH-07：MySQL Repository 实现（P1）

**新建目录**：`Rbac.Infrastructure.MySql/Repositories/`

实现以下 6 个 Repository（均继承 `RbacDbContext`，通过构造函数注入）：

- `AdministratorRepository` → `IAdministratorRepository`
- `GroupRepository` → `IGroupRepository`  
- `RuleRepository` → `IRuleRepository`
- `ProjectGrantRepository` → `IProjectGrantRepository`
- `ApiPermissionMapRepository` → `IApiPermissionMapRepository`
- `CasbinPolicyRepository` → `ICasbinPolicyRepository`（复用 PATCH-06 查询逻辑）
- `ProjectGrantMySqlReader` → `IProjectGrantMySqlReader`

**特殊约定**：`FindByProjectAsync(new ProjectCode("*"))` 语义为"全项目读取"，实现中需识别此标记并省略 `WHERE project = @p` 条件。

---

### PATCH-08：Outbox 接入 DbContext（P1）

**修改文件**：`Rbac.Infrastructure.MySql/Mapping/RbacEntityMappings.cs`

在 `RbacDbContext` 中补充（仅新增两行，不改动已有配置）：
```csharp
public DbSet<OutboxEventEntity> OutboxEvents => Set<OutboxEventEntity>();

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // 原有 5 条...
    modelBuilder.ApplyConfiguration(new OutboxEventMapping()); // 新增
}
```

**新建文件**：`Rbac.Infrastructure.MySql/Outbox/OutboxReaderWriter.cs`

实现 `IOutboxWriter` + `IOutboxReader`（同一个类实现两个接口）。

`FetchPendingAsync` 实现（见 PATCH-09 修正说明，必须能取到可重试的失败事件）：
```csharp
// 取 Status=Pending 且到达重试时间的事件
return await _db.OutboxEvents
    .Where(x => x.Status == OutboxStatus.Pending
        && (x.NextRetryAt == null || x.NextRetryAt <= DateTimeOffset.UtcNow))
    .OrderBy(x => x.CreatedAt)
    .Take(batchSize)
    .ToListAsync(ct);
```

`MarkFailedAsync` 实现（与 PATCH-09 协同，失败后保持 Pending，超限才改 Failed）：
```csharp
// 在 OutboxReaderWriter 中，接收 retryCount 参数，由调用方判断是否超限
// 这里统一设为 Pending（使事件可被重试），超限由 PATCH-09 传入 nextRetryAt=MaxValue 表示
public Task MarkFailedAsync(string eventId, int retryCount, DateTimeOffset nextRetryAt, CancellationToken ct)
    => _db.OutboxEvents
        .Where(x => x.EventId == eventId)
        .ExecuteUpdateAsync(s => s
            .SetProperty(e => e.RetryCount, retryCount)
            .SetProperty(e => e.NextRetryAt, nextRetryAt)
            // 注意：Status 保持 Pending，超限时由上层传 DateTimeOffset.MaxValue 标记放弃
            .SetProperty(e => e.Status, nextRetryAt == DateTimeOffset.MaxValue
                ? OutboxStatus.Failed : OutboxStatus.Pending), ct);
```

---

### PATCH-09：Outbox Polling HostedService（P1）✱已修正

**新建文件**：`Rbac.Worker/Outbox/RbacOutboxPollingWorker.cs`

修正后的重试逻辑（选项 A，保持 Pending 直到超限）：

```csharp
public sealed class RbacOutboxPollingWorker : BackgroundService
{
    private const int MaxRetry = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var reader = sp.GetRequiredService<IOutboxReader>();
            var redisProcessor = sp.GetRequiredService<RbacRedisOutboxProcessor>();
            var esProcessor = sp.GetRequiredService<RbacElasticsearchOutboxProcessor>();
            var casbinProcessor = sp.GetRequiredService<RbacCasbinOutboxProcessor>();

            var pending = await reader.FetchPendingAsync(50, stoppingToken);

            foreach (var evt in pending)
            {
                try
                {
                    await redisProcessor.ProcessAsync(evt, stoppingToken);
                    await esProcessor.ProcessAsync(evt, stoppingToken);
                    await casbinProcessor.ProcessAsync(evt, stoppingToken);
                    await reader.MarkSucceededAsync(evt.EventId, stoppingToken);
                }
                catch (Exception ex)
                {
                    var newRetryCount = evt.RetryCount + 1;
                    _logger.LogError(ex,
                        "Outbox processing failed eventId={Id} retry={N}/{Max}",
                        evt.EventId, newRetryCount, MaxRetry);

                    DateTimeOffset nextRetry;
                    if (newRetryCount >= MaxRetry)
                    {
                        // 超过最大重试：标记 Failed（DLQ），设 MaxValue 防止再次取出
                        nextRetry = DateTimeOffset.MaxValue;
                        _logger.LogError("Outbox DLQ eventId={Id}", evt.EventId);
                    }
                    else
                    {
                        // 指数退避，保持 Pending
                        nextRetry = DateTimeOffset.UtcNow.AddSeconds(
                            Math.Pow(2, newRetryCount) * 5);
                    }
                    await reader.MarkFailedAsync(evt.EventId, newRetryCount, nextRetry, stoppingToken);
                }
            }

            if (pending.Count == 0)
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
```

---

### PATCH-10：ES 全量重建补全 permission_view / audit_log（P2）

**修改文件**：`Rbac.Infrastructure.Elasticsearch/Reindex/RbacEsFullReindexService.cs`

补充两个方法：

```csharp
public async Task<ReindexResult> ReindexPermissionViewAsync(
    string? project = null, CancellationToken ct = default)
{
    var alias = RbacPermissionViewIndexMapping.IndexName;
    var newIndex = BuildVersionedIndexName(alias);

    return await ExecuteReindexAsync(alias, newIndex, ct, async () =>
    {
        var maps = project is not null
            ? await _apiMapRepo.FindActiveByProjectAsync(new ProjectCode(project), ct)
            : await _apiMapRepo.FindActiveByProjectAsync(new ProjectCode("*"), ct);

        var docs = maps.Select(m => new PermissionViewDocument
        {
            Id = m.Id.ToString(),
            Project = m.Project.Value,
            PermissionCode = m.PermissionCode.Value,
            Action = m.Action,
            ResourceType = "api",
            Path = m.RoutePattern,
            Status = m.Status.ToString(),
        }).ToList();

        await BulkIndexAsync<PermissionViewDocument>(newIndex, docs, ct);
        return docs.Count;
    });
}
```

同时在 `RbacEsReindexWorker.ReindexAllAsync` 中补充对 permission_view 的调用（audit_log 如无 MySQL 真相数据来源可暂缓，tasks.md 中标注 Deferred）。

**同时修改构造函数**：注入 `IApiPermissionMapRepository _apiMapRepo`（已有接口，PATCH-07 提供实现）。

---

### PATCH-11：ES AliasPreflightChecker 接入（P2）✱已修正

**修正说明**：`RbacEsAliasPreflightChecker` 类及其 `CheckAsync` 方法已完整存在于 `RbacEsBootstrap.cs`，不需要任何新代码。只需两处改动：

**改动 1**：修改 `RbacEsFullReindexService.cs` 构造函数：
```csharp
// 新增注入
private readonly RbacEsAliasPreflightChecker _preflight;

public RbacEsFullReindexService(
    IElasticClient esClient,
    IAdministratorRepository adminRepo,
    IGroupRepository groupRepo,
    IRuleRepository ruleRepo,
    RbacEsAliasPreflightChecker preflight,  // 新增
    ILogger<RbacEsFullReindexService> logger)
{
    // ...
    _preflight = preflight;
}
```

**改动 2**：在 `ExecuteReindexAsync` 方法开头，在"创建新索引"之前插入：
```csharp
var preflightResult = await _preflight.CheckAsync(alias, ct);
if (!preflightResult.IsPass)
{
    _logger.LogError("Preflight failed alias={Alias} reason={Reason}",
        alias, preflightResult.FailReason);
    return ReindexResult.Failure(alias, alias, $"Preflight: {preflightResult.FailReason}");
}
```

**改动 3**：在 DI 注册（`Program.cs`）中添加：
```csharp
builder.Services.AddSingleton<RbacEsAliasPreflightChecker>();
```

---

### PATCH-12：Audit Worker 注册（P1）✱已修正

**无需新建任何文件**。`ChannelAuditEventEmitter` 和 `RbacAuditEventWorker` 均已存在于 `Rbac.Worker/Auditing/RbacAuditEventWorker.cs`。

仅在 `Program.cs` 中注册（已在 PATCH-01 中体现）：
```csharp
services.AddSingleton<IAuditEventEmitter, ChannelAuditEventEmitter>();
services.AddHostedService<RbacAuditEventWorker>();
```

**不需要**：
- 创建 `Rbac.Application/Auditing/ChannelAuditEventEmitter.cs`（已存在）
- 创建 `Channel<AuditEvent>`（类型错误，实际是 `Channel<RbacAuditEvent>`，且为静态字段）
- 任何与 `AuditEvent` 类型相关的代码

---

### PATCH-13：Directory.Build.props（P2）✱执行顺序已修正

**严格执行顺序**：

**Step 1（必须先做）**：修改 `Rbac.Domain/Groups/RbacGroup.cs`：
```csharp
// 修改前（会触发 CS8618）：
public IReadOnlyList<RuleCode> RuleCodes { get; private set; }
public IReadOnlyList<PermissionCode> PermissionCodes { get; private set; }

// 修改后（加初始值，对 EF Core 反射安全）：
public IReadOnlyList<RuleCode> RuleCodes { get; private set; } = Array.Empty<RuleCode>();
public IReadOnlyList<PermissionCode> PermissionCodes { get; private set; } = Array.Empty<PermissionCode>();
```

**Step 2**：验证 `dotnet build Rbac.sln` 仍为 0 errors。

**Step 3（Step 2 通过后才能做）**：新建 `Directory.Build.props`：
```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <!-- Domain / Application 严格模式（其他项目不受影响）-->
  <PropertyGroup Condition="$(MSBuildProjectName.StartsWith('Rbac.Domain')) Or $(MSBuildProjectName.StartsWith('Rbac.Application'))">
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <!-- 仅锁定已知必须修复的 nullable 类别，不一次性全部 error -->
    <WarningsAsErrors>CS8618;CS8601;CS8602;CS8603;CS8604</WarningsAsErrors>
  </PropertyGroup>
</Project>
```

**Step 4**：再次 `dotnet build Rbac.sln`，应仍 0 errors（Step 1 已修复 CS8618）。

---

### PATCH-14：tasks.md 注释更新（P3，文档任务）

同 v1，在 T061 旁补充注释，说明 `RbacManagementSearchService` 的正确位置在 `Infrastructure.Elasticsearch`，不得移回 `Application` 层。

---

## 三、补丁交付结构（v2）

```
patch/
├── Rbac.Api/
│   ├── Program.cs                              (PATCH-01，完整重写)
│   ├── Security/
│   │   ├── JwtUserIdentityResolver.cs          (PATCH-02，新建)
│   │   └── HttpProjectRequestReader.cs         (PATCH-03，新建)
│   └── Authorization/
│       └── RoutePatternApiPermissionMapper.cs  (PATCH-04，新建)
├── Rbac.Domain/
│   └── Groups/
│       └── RbacGroup.cs                        (PATCH-13 Step 1，仅修改2行)
├── Rbac.Infrastructure.MySql/
│   ├── Mapping/
│   │   └── RbacEntityMappings.cs               (PATCH-08，新增DbSet+ApplyConfiguration)
│   ├── Outbox/
│   │   └── OutboxReaderWriter.cs               (PATCH-08，新建)
│   ├── Policies/
│   │   └── CasbinMySqlPolicyReaders.cs         (PATCH-06，新建)
│   └── Repositories/
│       ├── AdministratorRepository.cs          (PATCH-07，新建)
│       ├── GroupRepository.cs                  (PATCH-07，新建)
│       ├── RuleRepository.cs                   (PATCH-07，新建)
│       ├── ProjectGrantRepository.cs           (PATCH-07，新建)
│       ├── ApiPermissionMapRepository.cs       (PATCH-07，新建)
│       ├── CasbinPolicyRepository.cs           (PATCH-07，新建)
│       └── ProjectGrantMySqlReader.cs          (PATCH-07，新建)
├── Rbac.Infrastructure.Redis/
│   └── RbacSnapshotService.cs                  (PATCH-05，新建)
├── Rbac.Infrastructure.Elasticsearch/
│   └── Reindex/
│       └── RbacEsFullReindexService.cs         (PATCH-10+11，修改：注入preflight+补2个方法)
├── Rbac.Worker/
│   ├── Program.cs                              (PATCH-01，完整重写)
│   └── Outbox/
│       └── RbacOutboxPollingWorker.cs          (PATCH-09，新建)
└── Directory.Build.props                       (PATCH-13 Step 3，在Step 1验证后新建)
```

**不需要新建的文件**（v1 错误指示，v2 已纠正）：
- ~~`Rbac.Application/Auditing/ChannelAuditEventEmitter.cs`~~ → 已存在
- ~~`Rbac.Infrastructure.Elasticsearch/Bootstrap/RbacEsBootstrap.cs`（preflight方法）~~ → 已存在

---

## 四、执行波次（v2）

```
Wave 1（并行，基础层，无依赖）：
  PATCH-13 Step 1：修 RbacGroup.cs（先于 Directory.Build.props）
  PATCH-06：Casbin MySQL Policy Readers
  PATCH-07：Repository 实现（7个文件）
  PATCH-08：Outbox DbContext + OutboxReaderWriter

Wave 2（依赖 Wave 1，部分并行）：
  PATCH-02：JwtUserIdentityResolver
  PATCH-03：HttpProjectRequestReader
  PATCH-04：RoutePatternApiPermissionMapper（依赖 PATCH-07）
  PATCH-05：RbacSnapshotService（依赖 PATCH-06 + PATCH-07）
  PATCH-13 Step 3：Directory.Build.props（依赖 Step 1 验证通过）

Wave 3（依赖 Wave 1 + Wave 2）：
  PATCH-01：Api + Worker Program.cs（所有 DI 接线）
  PATCH-09：RbacOutboxPollingWorker
  PATCH-12：仅注册，无新建文件（已在 PATCH-01 中体现）

Wave 4（可延后到 US5/US6 阶段）：
  PATCH-10：ES 全量重建补 permission_view（依赖 PATCH-07）
  PATCH-11：RbacEsFullReindexService 注入 Preflight（改3行）
  PATCH-14：tasks.md 注释更新
```

---

## 五、验收检查点（v2，较 v1 新增 build 保护）

1. **`dotnet build Rbac.sln` — 0 errors**（在 PATCH-13 之前和之后都必须通过）
2. `dotnet run --project Rbac.Api` — 启动日志无 DI 解析错误，能看到 Middleware 注册
3. 无 JWT 请求 → 401；有效 JWT + 无效 project → 403；有效 JWT + allowlist 路由 → 200
4. 有效 JWT + 有效 project + 有 permissionCode 映射 + 有权限 → 200
5. 有效 JWT + 有效 project + 无 permissionCode 映射 → 403（deny-by-default）
6. 写操作产生 Outbox 记录 → Polling Worker 消费 → Redis 版本递增（可查 Redis key 验证）
7. 失败 Outbox 事件在 RetryCount < 5 时会被重新取出（验证重试语义）
8. `dotnet run --project Rbac.Worker` — 无 DI 错误，Polling Worker 正常轮询
9. ES 重建前 alias preflight 失败时 ReindexService 返回 Failure 而不抛异常
