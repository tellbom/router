# RBAC Code Core Completion Analysis

生成时间：2026-05-10

分析范围：以 `spec/main/tasks.md` 为基准，优先核查代码核心、架构边界和硬性指标；纯文档类任务缺失不作为阻断项。当前工作区可编译，但编译通过不等于运行链路完整。

## Executive Summary

结论：**当前项目未完全按 tasks.md 完成所有需求开发**。代码骨架和大量领域/基础设施类已存在，`dotnet build Rbac.sln` 结果为 **0 errors**，但运行时接线、关键接口实现、Outbox 消费闭环、ES 全量重建完整性等仍存在核心缺口。

最重要的判断：

- 编译状态：通过，仍有 22 个 warning。
- 代码路径覆盖：tasks.md 共 114 个任务，引用代码路径 94 个，按当前项目根路径解释有 87 个准确存在，7 个路径未按任务文件名存在。
- 架构边界：Application 层已无 NEST/Infrastructure 代码依赖，Search 实现已移动到 Infrastructure.Elasticsearch，分层方向正确。
- 可运行性：Api 和 Worker 入口几乎未注册 RBAC 服务、Middleware、Filter、HostedService，当前更像“类库集合”，不是完整可运行 RBAC 服务。

## Build Result

命令：

```powershell
dotnet build Rbac.sln
```

结果：成功，0 errors。

主要 warning：

- `NU1507`：Central Package Management 下存在多个 NuGet source，需要 package source mapping 或单一 source。
- `CS8618`：`Rbac.Domain/Groups/RbacGroup.cs` 的 `RuleCodes`、`PermissionCodes` nullable 初始化风险。
- `CS0618`：`RedisChannel` 隐式 string 转换已过时。
- `CS1998`：`Rbac.Worker/Cache/RbacPermsetInvalidationWorker.cs` 中 async 方法无 await。

## Core Findings

| ID | Severity | Area | Evidence | Finding | Recommendation |
|---|---|---|---|---|---|
| C1 | CRITICAL | Runtime wiring | `Rbac.Api/Program.cs`, `Rbac.Worker/Program.cs` | Api 只注册 RazorPages，Worker 只 `CreateDefaultBuilder().Build()`，没有注册 RBAC 服务、Redis、ES、Casbin、DbContext、FusionCache、Middleware、AuthorizationFilter、HostedService。 | 增加 DI/bootstrap 模块，把 Application/Infrastructure 服务和请求管线接入；否则核心代码不会被执行。 |
| C2 | CRITICAL | Missing implementations | `IUserIdentityResolver`, `IProjectRequestReader`, `IRbacApiPermissionMapper`, `IRbacSnapshotService`, `ICasbinGroupingPolicyReader`, `ICasbinPermissionPolicyReader` | 多个 MVP 阻断接口仅有契约，无实现类；鉴权链路无法从 JWT/project/API map/snapshot/policy reader 完整运行。 | 补实现并注册：JWT 用户解析、project 读取、API route-to-permission mapper、snapshot service、MySQL Casbin policy readers。 |
| C3 | HIGH | Outbox persistence | `Rbac.Infrastructure.MySql/Outbox/RbacOutboxMapping.cs`, `Rbac.Infrastructure.MySql/Mapping/RbacEntityMappings.cs` | `OutboxEventEntity` 和 mapping 存在，但 `RbacDbContext` 没有 `DbSet<OutboxEventEntity>`，也没有 `ApplyConfiguration(new OutboxEventMapping())`。 | 将 Outbox mapping 接入 DbContext，并实现 `IOutboxWriter/IOutboxReader`。 |
| C4 | HIGH | Outbox worker loop | `Rbac.Worker/Outbox/*`, `Rbac.Worker/Program.cs` | Redis/ES/Casbin Outbox processors 存在，但未看到统一 polling worker、`IOutboxReader` 消费循环、成功/失败状态更新和 DI 注册。 | 增加 Outbox hosted service，串联 FetchPending -> processors -> MarkSucceeded/MarkFailed。 |
| C5 | HIGH | ES full reindex completeness | `Rbac.Infrastructure.Elasticsearch/Reindex/RbacEsFullReindexService.cs` | 全量重建只覆盖 user/group/rule；未覆盖 permission_view/audit_log；alias preflight checker 未接入；`ProjectCode("*")` 依赖 repository 特殊语义但未定义。 | 补全 permission_view/audit_log 重建，接入 preflight，明确定义全项目读取接口。 |
| C6 | HIGH | Repository implementations | `Rbac.Application/Repositories/RbacRepositoryContracts.cs`, `Rbac.Infrastructure.MySql` | repository contracts 存在，但 MySQL 侧未发现具体 repository 实现；管理写保护、ES reindex、Outbox ES sync 都依赖这些接口。 | 实现 user/group/rule/project/api-map/policy repositories，并注册到 DI。 |
| C7 | HIGH | Auth filter activation | `Rbac.Api/Filters/RbacAuthorizationFilter.cs`, `Rbac.Api/Program.cs` | deny-by-default filter 已写，但没有在 MVC/Razor/API pipeline 中注册为全局 filter 或 endpoint filter。 | 在 `AddControllers/AddMvc` 或 endpoint pipeline 中挂载 `RbacAuthorizationFilter`，并确认 allowlist 顺序。 |
| C8 | MEDIUM | Task path drift | `tasks.md:T061`, current `Rbac.Infrastructure.Elasticsearch/Services/RbacManagementSearchService.cs` | T061 要求 Application/Search 下实现 ES search service，但当前正确架构是 Application 只保留接口/DTO，Infrastructure 实现。 | 更新 tasks.md 或后续对齐说明，避免其他 agent 误把 NEST 实现放回 Application。 |
| C9 | MEDIUM | Exact file-path coverage | task references | 未按任务路径存在：`Directory.Build.props`、`Rbac.Application/Search/RbacManagementSearchService.cs`、两个 Casbin reader 单文件、三个 ES bootstrap/preflight 单文件。部分是合并/重命名，不全是代码缺失。 | 对合并文件补任务映射说明；真正缺失的 `Directory.Build.props` 可补。 |
| C10 | MEDIUM | Quality gates | `Directory.Build.props` missing | T003 要求 shared build/analyzer config、domain/application warnings-as-errors、XML docs，但当前缺少 `Directory.Build.props`。 | 补全统一构建约束，至少对 Domain/Application 开启更严格 warning 策略。 |
| C11 | MEDIUM | Snapshot rebuild path | `IRbacSnapshotService`, `RbacPermissionChecker` | `RbacPermissionChecker` 依赖 `IRbacSnapshotService.GetSnapshotAsync/RebuildSnapshotAsync`，但未见实现；Redis miss -> Casbin -> snapshot rebuild 无法落地。 | 实现 snapshot service，从 MySQL/Casbin 派生 snapshot，并写 FusionCache/Redis。 |
| C12 | MEDIUM | Audit sink | `Rbac.Worker/Auditing/RbacAuditEventWorker.cs`, `Program.cs` | Channel audit emitter/worker 存在，但未注册，Api/Application 中依赖 `IAuditEventEmitter` 时运行会解析失败。 | 注册 `ChannelAuditEventEmitter` 与 audit worker；或提供临时 no-op emitter 但要明确审计风险。 |

## Hard Constraints Check

| Constraint | Status | Evidence | Notes |
|---|---|---|---|
| Redis `permset` 必须由 MySQL/Casbin 派生，不得另建权限真相 | Partially met | `RbacPermsetStore.BuildAndWriteAsync` 检查 `PermsetInputSource.MySqlCasbinDerived` | 写入约束存在，但 snapshot/policy reader 实现缺失，派生链路未完整闭环。 |
| `project` 校验必须前置到 ProjectResolver/CurrentRbacContext | Partially met | `CurrentRbacContextMiddleware`, `RbacProjectResolver` | 代码存在，但 Program.cs 未挂 middleware，运行时不生效。 |
| ES7 必须具备全量重建 + Outbox 增量同步 | Partially met | `RbacEsFullReindexService`, `RbacElasticsearchOutboxProcessor` | 类存在；未完整覆盖所有索引，Outbox 消费循环/DI 未接入。 |
| FusionCache 不包办所有 Redis 操作，`SISMEMBER` 必须直接 StackExchange.Redis | Met in code | `RedisPermsetOperations`, `RbacPermsetStore`, `RbacFusionCacheFacade` | `permset` 读取未走 FusionCache，符合核心边界。 |
| `DxE_id` 对 API 和 ES 按 string/keyword 处理 | Mostly met | DTO `string DxEId`, ES `[Keyword(Name="dxe_id")]`, mapping keyword | 核心类型正确；但 API 全局 JSON options 未接入 Program.cs，需接线验证。 |
| 不继续使用 ABP / PHP batoken / refreshToken / member RBAC | Mostly met | rg 未发现 ABP、refreshToken 输出 DTO 明确排除 | 当前代码未发现明显违背项。 |
| Application 不依赖 NEST/Infrastructure | Met | `rg "using Nest|using Rbac.Infrastructure" Rbac.Application` 无代码依赖，仅注释提及 | Search 实现已在 Infrastructure.Elasticsearch，分层正确。 |

## Task Coverage Summary

| Phase | Code Core Status | Notes |
|---|---|---|
| Phase 1 Setup | Partial | sln/projects/CPM 存在；`Directory.Build.props` 缺失；启动工程未接线。 |
| Phase 2 Foundational | Mostly structural | DTO、value objects、audit/outbox contracts、Redis keys 存在；运行集成未完成。 |
| US1 JWT + project context | Partial | options/middleware/resolver/cache contract 存在；JWT resolver、project reader实现与管线注册缺失。 |
| US2 MySQL truth model | Partial | domain aggregates 和 EF mapping 存在；repository 实现缺失，Outbox mapping 未接入 DbContext。 |
| US3 Runtime auth | Partial | permission checker、Redis direct SISMEMBER、Casbin model/provider 存在；API mapper/snapshot/policy readers/DI 缺失。 |
| US4 Frontend menus compatibility | Partial | DTO、menu builder、backend index service 存在；实际 endpoint/controller 和 DI 未接入。 |
| US5 ES management query | Partial | mapping/query builder/search service 存在且分层正确；任务路径需更新；写保护依赖 repository 实现。 |
| US6 Outbox + cache invalidation + ES rebuild | Partial | processors/cache invalidator/reindex classes 存在；缺 Outbox polling loop、reader/writer 实现、完整 reindex。 |
| US7 Scale/observability | Partial | metrics、warmup、version validation、key guard、gray migration contracts 存在；缺运行接线和验证。 |
| Implementation Blocking Corrections | Partial | 多数契约/类存在；MVP blocker 中 API mapper、snapshot service、Casbin reader实现、audit worker接线仍阻断。 |

## Path Coverage Detail

按 tasks.md 中 `src/...` 路径去掉 `src/` 后映射当前项目根目录：

- 任务总数：114
- 代码路径引用：94
- 准确存在：87
- 准确缺失：7

缺失路径：

```text
Directory.Build.props
Rbac.Application/Search/RbacManagementSearchService.cs
Rbac.Application/Policies/ICasbinGroupingPolicyReader.cs
Rbac.Application/Policies/ICasbinPermissionPolicyReader.cs
Rbac.Infrastructure.Elasticsearch/Bootstrap/RbacEsIndexTemplateBootstrapper.cs
Rbac.Infrastructure.Elasticsearch/Bootstrap/RbacEsAliasBootstrapper.cs
Rbac.Infrastructure.Elasticsearch/Reindex/RbacEsAliasPreflightChecker.cs
```

解释：

- `Rbac.Application/Search/RbacManagementSearchService.cs` 不建议恢复。当前移动到 Infrastructure 是正确分层。
- 两个 Casbin reader 已合并在 `Rbac.Application/Policies/ICasbinPolicyReaders.cs`，属于路径合并。
- ES bootstrap/preflight 三个类已合并在 `Rbac.Infrastructure.Elasticsearch/Bootstrap/RbacEsBootstrap.cs`，属于路径合并，但 task 精确路径不一致。
- `Directory.Build.props` 是真实缺失。

## Positive Signals

- Solution 可完整编译通过。
- Central Package Management 已存在，NEST 保持 7.17.x。
- Application/Search 只保留接口和 DTO，NEST 实现在 Infrastructure，避免了之前的分层污染。
- Redis permset 高频读取用 `IDatabase.SetContainsAsync`，未被 FusionCache 包装。
- DTO 与 ES Document 中 `DxEId` 多数为 string，ES mapping 使用 keyword。
- Casbin model 使用 RBAC with domains，Enforcer provider 有原子替换旧实例思路。
- `RbacRedisKeyGuard`、version validation、compare-before-write 等 10W 规模约束已有代码雏形。

## Final Assessment

当前代码状态适合描述为：

> “核心分层和大部分契约/骨架已搭建，编译通过，但尚未完成可运行闭环；不能认为 tasks.md 所有需求和硬性指标已如期完成。”

若只看文档类任务，可跳过很多缺口；但若看代码核心，至少 C1-C7 需要优先处理。

## Recommended Next Steps

1. 先补启动接线：Api/Worker Program、DI extension、middleware/filter/hosted service 注册。
2. 补 MVP 阻断接口实现：JWT resolver、project reader、API permission mapper、snapshot service、Casbin policy readers、repository implementations。
3. 接通 Outbox：DbContext mapping、writer/reader、polling worker、processor 成功/失败状态。
4. 补 ES reindex 完整性：permission_view/audit_log、alias preflight、bootstrap 调用。
5. 最后补 `Directory.Build.props` 和 warning 收敛，把“能编译”提升到“可守门”。

