# RBAC Patch Plan — Code Completion Gaps

**生成时间**: 2026-05-10  
**基准报告**: rbac-code-core-completion-analysis.md  
**本文目的**: 对报告中每条 Finding 进行独立核实，标注"接受 / 驳回 / 修正"，并输出可交付给后续执行 Agent 的补丁任务清单。

---

## 一、报告核实结论总表

| 报告 ID | 原始结论 | 核实结果 | 处置 |
|---------|---------|---------|------|
| C1 | Api/Worker Program.cs 未注册 RBAC 服务 | **确认** | 接受 |
| C2 | IUserIdentityResolver 等接口无实现类 | **部分驳回** — 接口本身存在；但实现类确实缺失 | 修正范围 |
| C3 | OutboxEventEntity 未接入 DbContext | **确认** | 接受 |
| C4 | Outbox Worker polling loop 缺失 | **确认** | 接受 |
| C5 | ES 全量重建缺 permission_view/audit_log | **确认** | 接受 |
| C6 | Repository 实现类缺失 | **确认** | 接受 |
| C7 | RbacAuthorizationFilter 未注册为全局 Filter | **确认** | 接受 |
| C8 | Task 路径漂移（ES search service 分层） | **驳回** — 当前 Infrastructure 实现是正确架构，只需更新 tasks.md 注释 | 降级为文档任务 |
| C9 | 7 个路径准确缺失 | **部分驳回** — 其中 5 个属路径合并，真实缺失仅 2 项 | 修正范围 |
| C10 | Directory.Build.props 缺失 | **确认** | 接受 |
| C11 | IRbacSnapshotService 无实现 | **确认** | 接受 |
| C12 | Audit Channel Worker 未注册 | **确认** | 接受 |

---

## 二、驳回 / 修正详情

### 2.1 C2 — 接口实现范围修正

报告称 `IUserIdentityResolver`、`IProjectRequestReader`、`IRbacApiPermissionMapper`、`IRbacSnapshotService`、`ICasbinGroupingPolicyReader`、`ICasbinPermissionPolicyReader` 均无实现。

经核查：

- `ICasbinGroupingPolicyReader` 和 `ICasbinPermissionPolicyReader` 已合并定义在 `Rbac.Application/Policies/ICasbinPolicyReaders.cs`，**接口存在，实现缺失**，报告描述部分不准确（报告称"仅有契约"是正确的，但报告将路径 `ICasbinGroupingPolicyReader.cs` / `ICasbinPermissionPolicyReader.cs` 标为"缺失"属路径理解错误——文件已合并，不是真缺失）。
- `IRbacSnapshotService` 接口完整定义于 `Rbac.Application/Snapshots/IRbacSnapshotService.cs`，`UserPermissionSnapshot`、`SnapshotVersions` 也均存在——**缺失的是具体实现类**。
- `IUserIdentityResolver`、`IProjectRequestReader`、`IRbacApiPermissionMapper` 接口均存在，实现缺失。

**修正**：C2 描述的缺口真实存在，但报告在路径层面的指向有 2 处混淆，已在 C9 中合并处理。实现缺口本身全部保留为补丁任务。

### 2.2 C8 — ES Search Service 架构漂移（驳回）

报告称 T061 "要求 Application/Search 下实现 ES search service"，而当前正确位置是 Infrastructure.Elasticsearch。

经核查：

- `Rbac.Application/Search/IRbacManagementSearchService.cs` 存在，仅保留**接口和 DTO**。
- `Rbac.Infrastructure.Elasticsearch/Services/RbacManagementSearchService.cs` 存在实现，**完全符合分层原则**。
- 报告自身已注明"不建议恢复"，说明 C8 不是代码缺口，只是 tasks.md 文字描述过时。

**决策**：C8 不产生代码补丁任务，仅产生一条 tasks.md 注释更新任务（P3 优先级）。

### 2.3 C9 — 路径缺失修正

报告列出 7 个"准确缺失"路径，经逐条核实：

| 路径 | 实际状态 |
|------|---------|
| `Directory.Build.props` | **真实缺失** → 保留补丁任务 |
| `Rbac.Application/Search/RbacManagementSearchService.cs` | **故意不存在**（正确架构）→ 驳回 |
| `Rbac.Application/Policies/ICasbinGroupingPolicyReader.cs` | **已合并**到 `ICasbinPolicyReaders.cs` → 驳回（路径合并，非缺失） |
| `Rbac.Application/Policies/ICasbinPermissionPolicyReader.cs` | **已合并**到 `ICasbinPolicyReaders.cs` → 驳回 |
| `Rbac.Infrastructure.Elasticsearch/Bootstrap/RbacEsIndexTemplateBootstrapper.cs` | **已合并**到 `RbacEsBootstrap.cs` → 驳回 |
| `Rbac.Infrastructure.Elasticsearch/Bootstrap/RbacEsAliasBootstrapper.cs` | **已合并**到 `RbacEsBootstrap.cs` → 驳回 |
| `Rbac.Infrastructure.Elasticsearch/Reindex/RbacEsAliasPreflightChecker.cs` | **缺失逻辑**：`RbacEsBootstrap.cs` 中未见 alias preflight 校验实现 → **保留补丁任务** |

**修正结论**：真实路径缺失降为 2 项（`Directory.Build.props` + alias preflight 逻辑缺失）。

---

## 三、补丁任务清单

优先级定义：P1 = MVP 阻断，P2 = 高优先级，P3 = 中优先级（可在 US5/US6 阶段完成）。

---

### PATCH-01：Api/Worker 启动接线（P1）

**对应报告**：C1  
**文件**：`Rbac.Api/Program.cs`，`Rbac.Worker/Program.cs`

**Api/Program.cs 需完成**：

1. 注册 JWT Authentication（`AddAuthentication().AddJwtBearer()`），绑定 `RbacJwtOptions`。
2. 注册 `RbacProjectOptions`、`RbacAllowlistOptions`（`services.Configure<>()`）。
3. 注册 Application 层服务：`RbacProjectResolver`、`RbacPermissionChecker`、`RbacMenuBuilder`、`RbacProjectMenuTreeService`、`RbacVersionValidationService`、`RbacPermsetLazyRebuildCoordinator`。
4. 注册 Infrastructure 层服务：`RbacDbContext`（MySQL）、Redis `IConnectionMultiplexer`、FusionCache、`RbacFusionCacheFacade`、`RbacPermsetStore`、`RbacProjectGrantCache`、NEST `IElasticClient`、`CasbinEnforcerProvider`、`CasbinEnforcerFactory`、`RbacCasbinModelProvider`。
5. 注册接口实现绑定（见 PATCH-02 到 PATCH-07）。
6. 注册 `HttpContextRbacContextAccessor` 为 Scoped（绑定 `ICurrentRbacContextAccessor`）。
7. 注册 `RbacAuthorizationFilter` 为全局 Filter（`AddControllers(opt => opt.Filters.Add<RbacAuthorizationFilter>())`）。
8. 注册 Channel `IAuditEventEmitter`（`ChannelAuditEventEmitter`，Singleton）。
9. 管道顺序：`UseAuthentication()` → `UseMiddleware<CurrentRbacContextMiddleware>()` → `UseAuthorization()` → `MapControllers()`。
10. JSON 序列化：注册 `LongToStringConverter` 全局确保 `DxEId` 不以 number 输出。

**Worker/Program.cs 需完成**：

1. `Host.CreateDefaultBuilder().ConfigureServices()` 注册所有 Infrastructure 服务（同 Api，去掉 Http 相关）。
2. 注册 HostedService：`RbacAuditEventWorker`、`RbacCacheWarmupWorker`、`RbacPermsetInvalidationWorker`、`RbacCasbinOutboxProcessor`（作为 HostedService 或由 Outbox Polling Worker 调用）。
3. 注册 Outbox Polling Worker（见 PATCH-05）。

---

### PATCH-02：JWT UserIdentity Resolver 实现（P1）

**对应报告**：C2  
**新建文件**：`Rbac.Api/Security/JwtUserIdentityResolver.cs`

实现 `IUserIdentityResolver`：

```csharp
// 从 ClaimsPrincipal 按 RbacJwtOptions.UseridClaim 依次提取
// 顺序：UseridClaim → FallbackUseridClaims → null
public string? ResolveUserId(ClaimsPrincipal principal)
{
    // 优先主 claim
    var val = principal.FindFirstValue(_options.UseridClaim);
    if (!string.IsNullOrWhiteSpace(val)) return val;
    // 遍历 fallback claims
    foreach (var claim in _options.FallbackUseridClaims)
    {
        val = principal.FindFirstValue(claim);
        if (!string.IsNullOrWhiteSpace(val)) return val;
    }
    return null;
}
```

注册：`services.AddScoped<IUserIdentityResolver, JwtUserIdentityResolver>()`

---

### PATCH-03：Project Request Reader 实现（P1）

**对应报告**：C2  
**新建文件**：`Rbac.Api/Security/HttpProjectRequestReader.cs`

实现 `IProjectRequestReader`：

```csharp
// 按 RbacProjectOptions.AllowedSources 顺序读取：Header("X-Project") → Route → Query → Body
public string? ReadProject(HttpContext context)
{
    foreach (var source in _options.AllowedSources)
    {
        var val = source switch
        {
            "Header" => context.Request.Headers[_options.HeaderName].FirstOrDefault(),
            "Route"  => context.GetRouteValue("project")?.ToString(),
            "Query"  => context.Request.Query[_options.QueryKey].FirstOrDefault(),
            _        => null
        };
        if (!string.IsNullOrWhiteSpace(val)) return val;
    }
    return null;
}
```

注册：`services.AddScoped<IProjectRequestReader, HttpProjectRequestReader>()`

---

### PATCH-04：IRbacApiPermissionMapper 实现（P1）

**对应报告**：C2  
**新建文件**：`Rbac.Infrastructure.Redis/RbacApiPermissionMapCache.cs`（缓存层）  
**新建文件**：`Rbac.Api/Authorization/RoutePatternApiPermissionMapper.cs`（实现层）

实现 `IRbacApiPermissionMapper`：

1. 从 FusionCache / Redis 读取 project 的 `IReadOnlyList<RbacApiPermissionMap>`（缓存 key：`rbac:api-map:{project}`）。
2. 使用 `RouteTemplate.TryParse` + `TemplateMatcher` 逐条匹配 `(httpMethod, path)`。
3. 命中返回 `ApiPermissionMapping`，未命中返回 `null`（由 Filter 按 deny-by-default 处理）。
4. 缓存未命中时从 `IApiPermissionMapRepository` 回源 MySQL。

注册：`services.AddScoped<IRbacApiPermissionMapper, RoutePatternApiPermissionMapper>()`

---

### PATCH-05：IRbacSnapshotService 实现（P1）

**对应报告**：C2、C11  
**新建文件**：`Rbac.Infrastructure.Redis/RbacSnapshotService.cs`

实现 `IRbacSnapshotService`：

```
GetSnapshotAsync:
  1. FusionCache L1 查询 snapshot:{project}:{userid}
  2. 命中 → 返回
  3. 未命中 → Redis 直接 GET rbac:snapshot:{project}:{userid}（JSON 反序列化）
  4. 仍未命中 → 调用 RebuildSnapshotAsync

RebuildSnapshotAsync:
  1. 读取 versionAtStart（ReadUserVersionAsync）
  2. 从 MySQL 拉取用户所属 groups（ICasbinGroupingPolicyReader.LoadAsync）
  3. 从 MySQL 拉取 group → permissionCode 映射（ICasbinPermissionPolicyReader.LoadAsync）
  4. 构建 UserPermissionSnapshot
  5. version compare-before-write：再次读 Redis 版本，若 current > versionAtStart，则丢弃
  6. 写入 Redis（SET with TTL）+ 写入 FusionCache L1
  7. 返回 snapshot

InvalidateAsync:
  删除 FusionCache L1 条目 + Redis DEL rbac:snapshot:{project}:{userid}
```

注册：`services.AddScoped<IRbacSnapshotService, RbacSnapshotService>()`

---

### PATCH-06：ICasbinGroupingPolicyReader / ICasbinPermissionPolicyReader MySQL 实现（P1）

**对应报告**：C2  
**新建文件**：`Rbac.Infrastructure.MySql/Policies/CasbinMySqlPolicyReaders.cs`

实现 `ICasbinGroupingPolicyReader`：

```csharp
// SELECT userid, group_code, project FROM rbac_admin_group_member WHERE project = @project AND status = 'Active'
public async Task<IReadOnlyList<(string, string, string)>> LoadAsync(ProjectCode project, CancellationToken ct)
    => await _db.Set<AdminGroupMemberEntity>()
        .Where(x => x.Project == project.Value && x.Status == "Active")
        .Select(x => ValueTuple.Create(x.Userid, x.GroupCode, x.Project))
        .ToListAsync(ct);
```

实现 `ICasbinPermissionPolicyReader`：

```csharp
// 从 RbacGroup 中展开 (groupCode, project, permissionCode, action)
// action 由 permissionCode 的最后一段推断，或由 rbac_rule 补充字段提供
```

注册：
```csharp
services.AddScoped<ICasbinGroupingPolicyReader, CasbinMySqlGroupingPolicyReader>();
services.AddScoped<ICasbinPermissionPolicyReader, CasbinMySqlPermissionPolicyReader>();
```

---

### PATCH-07：MySQL Repository 实现（P1）

**对应报告**：C6  
**新建文件**：`Rbac.Infrastructure.MySql/Repositories/` 目录下：

- `AdministratorRepository.cs` → 实现 `IAdministratorRepository`
- `GroupRepository.cs` → 实现 `IGroupRepository`
- `RuleRepository.cs` → 实现 `IRuleRepository`
- `ProjectGrantRepository.cs` → 实现 `IProjectGrantRepository`
- `ApiPermissionMapRepository.cs` → 实现 `IApiPermissionMapRepository`
- `CasbinPolicyRepository.cs` → 实现 `ICasbinPolicyRepository`（复用 PATCH-06 查询逻辑）
- `ProjectGrantMySqlReader.cs` → 实现 `IProjectGrantMySqlReader`（供 `RbacProjectGrantCache` 回源）

所有 Repository 通过 `RbacDbContext` 注入，使用 EF Core。不使用 EF Migrations。

注册：
```csharp
services.AddScoped<IAdministratorRepository, AdministratorRepository>();
// ...（同上模式）
```

---

### PATCH-08：Outbox 接入 DbContext（P1）

**对应报告**：C3  
**修改文件**：`Rbac.Infrastructure.MySql/Mapping/RbacEntityMappings.cs`

在 `RbacDbContext` 中增加：

```csharp
public DbSet<OutboxEventEntity> OutboxEvents => Set<OutboxEventEntity>();

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // 原有配置...
    modelBuilder.ApplyConfiguration(new OutboxEventMapping()); // 新增
}
```

**新建文件**：`Rbac.Infrastructure.MySql/Outbox/OutboxReaderWriter.cs`

实现 `IOutboxWriter`：

```csharp
public void Append(RbacOutboxEvent evt)
    => _db.OutboxEvents.Add(MapToEntity(evt));

public void AppendRange(IEnumerable<RbacOutboxEvent> events)
    => _db.OutboxEvents.AddRange(events.Select(MapToEntity));
```

实现 `IOutboxReader`：

```csharp
public async Task<IReadOnlyList<OutboxEventEntity>> FetchPendingAsync(int batchSize, CancellationToken ct)
    => await _db.OutboxEvents
        .Where(x => x.Status == OutboxStatus.Pending && (x.NextRetryAt == null || x.NextRetryAt <= DateTimeOffset.UtcNow))
        .OrderBy(x => x.CreatedAt)
        .Take(batchSize)
        .ToListAsync(ct);

public Task MarkSucceededAsync(string eventId, CancellationToken ct)
    => _db.OutboxEvents
        .Where(x => x.EventId == eventId)
        .ExecuteUpdateAsync(s => s
            .SetProperty(e => e.Status, OutboxStatus.Succeeded)
            .SetProperty(e => e.ProcessedAt, DateTimeOffset.UtcNow), ct);

public Task MarkFailedAsync(string eventId, int retryCount, DateTimeOffset nextRetryAt, CancellationToken ct)
    => _db.OutboxEvents
        .Where(x => x.EventId == eventId)
        .ExecuteUpdateAsync(s => s
            .SetProperty(e => e.Status, OutboxStatus.Failed)
            .SetProperty(e => e.RetryCount, retryCount)
            .SetProperty(e => e.NextRetryAt, nextRetryAt), ct);
```

注册：
```csharp
services.AddScoped<IOutboxWriter, OutboxReaderWriter>();
services.AddScoped<IOutboxReader, OutboxReaderWriter>();
```

---

### PATCH-09：Outbox Polling HostedService（P1）

**对应报告**：C4  
**新建文件**：`Rbac.Worker/Outbox/RbacOutboxPollingWorker.cs`

```csharp
public sealed class RbacOutboxPollingWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var reader = scope.ServiceProvider.GetRequiredService<IOutboxReader>();
            var redisProcessor = scope.ServiceProvider.GetRequiredService<RbacRedisOutboxProcessor>();
            var esProcessor   = scope.ServiceProvider.GetRequiredService<RbacElasticsearchOutboxProcessor>();
            var casbinProcessor = scope.ServiceProvider.GetRequiredService<RbacCasbinOutboxProcessor>();

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
                    _logger.LogError(ex, "Outbox processing failed eventId={Id}", evt.EventId);
                    var nextRetry = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, evt.RetryCount + 1) * 5);
                    await reader.MarkFailedAsync(evt.EventId, evt.RetryCount + 1, nextRetry, stoppingToken);
                }
            }

            if (pending.Count == 0)
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
```

注册：`services.AddHostedService<RbacOutboxPollingWorker>()`

---

### PATCH-10：ES 全量重建补全（P2）

**对应报告**：C5  
**修改文件**：`Rbac.Infrastructure.Elasticsearch/Reindex/RbacEsFullReindexService.cs`

补充 `permission_view` 和 `audit_log` 索引重建：

1. 读取 `IApiPermissionMapRepository.FindActiveByProjectAsync(ProjectCode("*"))` — 需要在 Repository 中明确支持全项目读取（约定：`ProjectCode("*")` 表示全量）。
2. 将 `RbacApiPermissionMap` 批量写入 `rbac_permission_view_{alias}` 索引。
3. 补充 alias preflight（见 PATCH-11）前置检查调用。

---

### PATCH-11：ES Alias Preflight 逻辑（P2）

**对应报告**：C9（alias preflight 缺失）  
**修改文件**：`Rbac.Infrastructure.Elasticsearch/Bootstrap/RbacEsBootstrap.cs`

在现有 Bootstrap 中补充 preflight 方法：

```csharp
public async Task<bool> AliasPreflightCheckAsync(string aliasName, CancellationToken ct = default)
{
    var aliasExists = await _client.Indices.ExistsAliasAsync(aliasName, ct: ct);
    if (!aliasExists.Exists)
    {
        _logger.LogError("ES alias '{Alias}' does not exist. Reindex aborted.", aliasName);
        return false;
    }
    return true;
}
```

在 `RbacEsFullReindexService.ReindexProjectAsync` 中调用 preflight，失败时提前返回并记录错误。

---

### PATCH-12：Audit Channel Worker 注册（P1）

**对应报告**：C12  
**修改文件**：`Rbac.Api/Program.cs`，`Rbac.Worker/Program.cs`

```csharp
// 注册 Channel 基础设施
services.AddSingleton<Channel<AuditEvent>>(_ =>
    Channel.CreateBounded<AuditEvent>(new BoundedChannelOptions(1024)
    {
        FullMode = BoundedChannelFullMode.DropOldest
    }));

// 注册 emitter（从 Channel writer 派生）
services.AddSingleton<IAuditEventEmitter, ChannelAuditEventEmitter>();

// 注册消费 Worker
services.AddHostedService<RbacAuditEventWorker>();
```

**注意**：`ChannelAuditEventEmitter` 类如不存在，需新建于 `Rbac.Application/Auditing/ChannelAuditEventEmitter.cs`，实现 `IAuditEventEmitter`，内部持有 `ChannelWriter<AuditEvent>`。

---

### PATCH-13：Directory.Build.props（P2）

**对应报告**：C10  
**新建文件**：`Directory.Build.props`（解决方案根目录）

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <!-- Domain / Application 严格模式 -->
  <PropertyGroup Condition="$(MSBuildProjectName.StartsWith('Rbac.Domain')) Or $(MSBuildProjectName.StartsWith('Rbac.Application'))">
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsAsErrors>CS8618;CS8601;CS8602;CS8603;CS8604</WarningsAsErrors>
  </PropertyGroup>
</Project>
```

---

### PATCH-14：tasks.md 注释更新（P3，文档任务）

**对应报告**：C8  
**修改文件**：`tasks.md`（T061 条目）

在 T061 旁增加说明注释：

```
- [x] T061 ... NOTE: Implementation correctly resides in Rbac.Infrastructure.Elasticsearch/Services/
      RbacManagementSearchService.cs. Application layer retains interface + DTO only (IRbacManagementSearchService.cs).
      Do NOT move implementation back to Application layer.
```

---

## 四、补丁交付结构

```
patch/
├── Rbac.Api/
│   ├── Program.cs                          (PATCH-01)
│   ├── Security/
│   │   ├── JwtUserIdentityResolver.cs      (PATCH-02)
│   │   └── HttpProjectRequestReader.cs     (PATCH-03)
│   └── Authorization/
│       └── RoutePatternApiPermissionMapper.cs  (PATCH-04)
├── Rbac.Application/
│   └── Auditing/
│       └── ChannelAuditEventEmitter.cs     (PATCH-12, if missing)
├── Rbac.Infrastructure.MySql/
│   ├── Mapping/
│   │   └── RbacEntityMappings.cs           (PATCH-08, DbSet + ApplyConfiguration)
│   ├── Outbox/
│   │   └── OutboxReaderWriter.cs           (PATCH-08)
│   ├── Policies/
│   │   └── CasbinMySqlPolicyReaders.cs     (PATCH-06)
│   └── Repositories/
│       ├── AdministratorRepository.cs      (PATCH-07)
│       ├── GroupRepository.cs              (PATCH-07)
│       ├── RuleRepository.cs               (PATCH-07)
│       ├── ProjectGrantRepository.cs       (PATCH-07)
│       ├── ApiPermissionMapRepository.cs   (PATCH-07)
│       ├── CasbinPolicyRepository.cs       (PATCH-07)
│       └── ProjectGrantMySqlReader.cs      (PATCH-07)
├── Rbac.Infrastructure.Redis/
│   ├── RbacSnapshotService.cs              (PATCH-05)
│   └── RbacApiPermissionMapCache.cs        (PATCH-04, cache layer)
├── Rbac.Infrastructure.Elasticsearch/
│   ├── Bootstrap/
│   │   └── RbacEsBootstrap.cs              (PATCH-11, alias preflight addition)
│   └── Reindex/
│       └── RbacEsFullReindexService.cs     (PATCH-10, permission_view + audit_log)
├── Rbac.Worker/
│   ├── Program.cs                          (PATCH-01)
│   └── Outbox/
│       └── RbacOutboxPollingWorker.cs      (PATCH-09)
└── Directory.Build.props                   (PATCH-13)
```

---

## 五、执行顺序建议

```
Wave 1（MVP 阻断，并行可行）：
  PATCH-13 Directory.Build.props
  PATCH-06 Casbin MySQL Policy Readers
  PATCH-07 Repository 实现
  PATCH-08 Outbox DbContext 接入

Wave 2（依赖 Wave 1 接口，部分并行）：
  PATCH-02 JwtUserIdentityResolver
  PATCH-03 HttpProjectRequestReader
  PATCH-04 RoutePatternApiPermissionMapper（依赖 PATCH-07）
  PATCH-05 RbacSnapshotService（依赖 PATCH-06 + PATCH-07）
  PATCH-12 Audit Channel Worker

Wave 3（依赖 Wave 1 + Wave 2）：
  PATCH-01 Api/Worker Program.cs（所有服务注册）
  PATCH-09 Outbox Polling Worker（依赖 PATCH-08）

Wave 4（可延后到 US5/US6 阶段）：
  PATCH-10 ES 全量重建补全
  PATCH-11 ES Alias Preflight
  PATCH-14 tasks.md 注释更新
```

---

## 六、验收检查点

补丁合并后，执行以下验证：

1. `dotnet build Rbac.sln` — 0 errors，warnings 数量不增加。
2. `dotnet run --project Rbac.Api` — 启动日志包含 Middleware / Filter 注册确认。
3. 发起无 JWT 请求 → 返回 401。
4. 发起有效 JWT + 未授权 project 请求 → 返回 403。
5. 发起有效 JWT + 有效 project + 无 permissionCode 映射 API 请求 → 返回 403。
6. 发起有效 JWT + 有效 project + 有效权限请求 → 返回 200（需 MySQL 数据）。
7. 写操作触发 Outbox 事件 → Worker 轮询消费 → Redis / ES 同步（Integration Test 验证）。
8. `dotnet run --project Rbac.Worker` — Outbox Polling Worker 启动，无 DI 解析错误。
