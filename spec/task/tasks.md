# ASP.NET Core RBAC 权限中心 tasks.md

**生成依据**:

- `spec/main/rbac-contract-readonly-audit.md`
- `spec/main/rbac-frontend-compatibility-contract.md`
- `spec/main/rbac-intranet-compatibility-boundary-analysis.md`
- `spec/main/ASP.NET Core RBAC 重构实施计划.md`
- `spec/main/Redis + ES7 + FusionCache + NetCasbin 完整增强设计.md`

**范围**: 仅后台管理员 RBAC；不分析会员 RBAC；不复刻 PHP；不继续使用 ABP；不生成数据库迁移；不接入前端 Casbin 感知。

**硬性约束**:

- Redis `permset` 必须由 MySQL/Casbin 策略生成，不能另搞一套权限真相。
- `project` 校验必须前置成 `ProjectResolver` / `CurrentRbacContext` 统一上下文，不能散落在业务 Service 中。
- ES7 必须具备全量重建索引与 Outbox 增量同步机制，不能只设计查询 mapping。
- FusionCache 不包办所有 Redis 操作，`permset` 高频 `SISMEMBER` 判断必须直接走 StackExchange.Redis。
- `DxE_id` 对前端 API 和 ES 一律按 string/keyword 处理，不能以 JSON number 返回。

## Phase 1: Setup

**目标**: 建立独立 ASP.NET Core RBAC 权限中心的工程骨架、技术依赖和全局约束。

- [ ] T001 Create solution skeleton with projects `src/Rbac.Api`, `src/Rbac.Application`, `src/Rbac.Domain`, `src/Rbac.Infrastructure.MySql`, `src/Rbac.Infrastructure.Redis`, `src/Rbac.Infrastructure.Elasticsearch`, `src/Rbac.Infrastructure.Casbin`, and `src/Rbac.Worker`
- [ ] T002 Add package reference plan for ASP.NET Core, MySQL provider, StackExchange.Redis, FusionCache, NetCasbin, NEST 7.17.x, Hangfire or Worker services in `src/Directory.Packages.props`
- [ ] T003 Create shared build and analyzer configuration for nullable reference types, warnings as errors for domain/application projects, and XML docs in `src/Directory.Build.props`
- [ ] T004 Create environment configuration template for JWT, MySQL, Redis, Elasticsearch, FusionCache, Casbin, and worker options in `src/Rbac.Api/appsettings.Development.json`
- [ ] T005 Document implementation guardrails including no ABP, no PHP batoken, no refreshToken, no member RBAC, and no migration generation in `docs/rbac/implementation-guardrails.md`
- [ ] T006 Create project dependency rules so Api depends on Application, Application depends on Domain abstractions, and Infrastructure projects implement interfaces in `docs/rbac/project-dependency-rules.md`

## Phase 2: Foundational

**目标**: 完成所有 user story 的阻塞前置能力，包括响应合同、统一上下文、领域基础对象、缓存 key 规范和审计基础。

- [ ] T007 Define unified response envelope `code/msg/data/time` and pagination DTO conventions in `src/Rbac.Application/Contracts/Common/ApiResponseContracts.cs`
- [ ] T008 Define frontend compatibility DTO conventions for `DxE_id` string, `userid`, `project`, `menus`, `routePath`, and `super` in `src/Rbac.Application/Contracts/Compatibility/FrontendCompatibilityContracts.cs`
- [ ] T009 Create domain value objects for `ProjectCode`, `UserId`, `DxEId`, `PermissionCode`, `RuleCode`, and `GroupCode` in `src/Rbac.Domain/ValueObjects/RbacValueObjects.cs`
- [ ] T010 Define `CurrentRbacContext` and `ICurrentRbacContextAccessor` in `src/Rbac.Application/Security/CurrentRbacContext.cs`
- [ ] T011 Define `IRbacProjectResolver` contract for centralized project parsing and userid-project authorization in `src/Rbac.Application/Security/IRbacProjectResolver.cs`
- [ ] T012 Define Redis key naming constants for snapshot, menus, permset, user-projects, project-users, versions, api-map, menu-tree, and policy-version in `src/Rbac.Infrastructure.Redis/RbacRedisKeys.cs`
- [ ] T013 Define base audit event contract for authorization, management writes, cache invalidation, ES sync, and Casbin policy sync in `src/Rbac.Application/Auditing/RbacAuditContracts.cs`
- [ ] T014 Define Outbox event contracts and fixed payload field lists for UserChanged, GroupChanged, MenuChanged, PolicyChanged, ProjectGrantChanged, and ApiMapChanged in `src/Rbac.Application/Outbox/RbacOutboxEvents.cs`
- [ ] T015 Define global serialization rules requiring `DxE_id` as JSON string and ES keyword in `src/Rbac.Application/Serialization/RbacSerializationRules.cs`
- [ ] T016 Create architecture decision record covering MySQL truth, Redis cache, FusionCache boundary, ES query-only role, and NetCasbin runtime role in `docs/rbac/adr-001-rbac-runtime-architecture.md`

## Phase 3: User Story 1 - JWT 登录上下文与 project 前置校验 (P1)

**目标**: API 请求进入后能够从 JWT 得到 `userid`，从请求得到 `project`，并在进入业务 Service 前生成可信 `CurrentRbacContext`。

**独立验收标准**: 任意后台 RBAC API 请求必须具备已校验的 `CurrentRbacContext`；业务 Service 不再读取原始 header/body 中的 project；未授权 project 返回 403。

- [ ] T017 [P] [US1] Define JWT userid claim mapping options for company portal and Keycloak in `src/Rbac.Api/Options/RbacJwtOptions.cs`
- [ ] T018 [P] [US1] Define project request source options for header, route, query, and body with preferred `X-Project` header in `src/Rbac.Api/Options/RbacProjectOptions.cs`
- [ ] T019 [US1] Implement JWT userid extraction contract in `src/Rbac.Application/Security/IUserIdentityResolver.cs`
- [ ] T020 [US1] Implement request project extraction contract in `src/Rbac.Application/Security/IProjectRequestReader.cs`
- [ ] T021 [US1] Implement centralized project authorization flow using `IRbacProjectResolver` in `src/Rbac.Application/Security/RbacProjectResolver.cs`
- [ ] T022 [US1] Implement request-scoped `CurrentRbacContext` middleware in `src/Rbac.Api/Middleware/CurrentRbacContextMiddleware.cs`
- [ ] T023 [US1] Add project authorization cache read path using FusionCache and Redis `rbac:user-projects:{userid}` in `src/Rbac.Infrastructure.Redis/RbacProjectGrantCache.cs`
- [ ] T024 [US1] Add audit events for missing project, forged project, unauthorized project, and resolved project in `src/Rbac.Application/Auditing/ProjectAuthorizationAuditService.cs`
- [ ] T025 [US1] Document failure modes and HTTP status mapping for JWT/project validation in `docs/rbac/authentication-project-context.md`

## Phase 4: User Story 2 - MySQL 真相库与兼容 ID 模型 (P1)

**目标**: 建立 RBAC 领域模型和 MySQL 真相库边界，内部使用 Guid，对外兼容 `DxE_id` string，长期权限判断使用 `permissionCode` / `ruleCode`。

**独立验收标准**: 领域模型不依赖自增 int；所有对前端暴露的 `DxE_id` 都是 string；权限判断不以 `DxE_id` 为依据。

- [ ] T026 [P] [US2] Define administrator aggregate with Guid internal ID, `DxE_id` string, `userid`, `username`, `status`, and project grants in `src/Rbac.Domain/Users/RbacAdministrator.cs`
- [ ] T027 [P] [US2] Define group aggregate with Guid internal ID, `DxE_id` string, `groupCode`, `project`, rules, and status in `src/Rbac.Domain/Groups/RbacGroup.cs`
- [ ] T028 [P] [US2] Define rule aggregate with Guid internal ID, `DxE_id` string, `ruleCode`, `permissionCode`, menu metadata, and button metadata in `src/Rbac.Domain/Rules/RbacRule.cs`
- [ ] T029 [P] [US2] Define project grant aggregate for userid-project authorization and project-scoped super in `src/Rbac.Domain/Projects/RbacProjectGrant.cs`
- [ ] T030 [P] [US2] Define API permission map aggregate for method, route pattern, `permissionCode`, action, project, and status in `src/Rbac.Domain/Permissions/RbacApiPermissionMap.cs`
- [ ] T031 [US2] Define repository interfaces for users, groups, rules, project grants, API maps, and policies in `src/Rbac.Application/Repositories/RbacRepositoryContracts.cs`
- [ ] T032 [US2] Define MySQL persistence mappings without generating migrations in `src/Rbac.Infrastructure.MySql/Mapping/RbacEntityMappings.cs`
- [ ] T033 [US2] Define DTO mappers that serialize `DxE_id` as string for users, groups, rules, menus, and permission views in `src/Rbac.Application/Mapping/RbacCompatibilityMappers.cs`
- [ ] T034 [US2] Define validation rules preventing `DxE_id` from becoming authorization truth in `src/Rbac.Domain/Validation/RbacIdentityValidationRules.cs`
- [ ] T035 [US2] Document MySQL truth model and Guid/DxE_id/permissionCode relationship in `docs/rbac/mysql-truth-model.md`

## Phase 5: User Story 3 - 运行态鉴权：Redis permset + FusionCache + NetCasbin (P1)

**目标**: 高频接口鉴权优先走 Redis `permset`，缓存缺失或版本不一致时通过 NetCasbin 兜底，所有 `permset` 从 MySQL/Casbin 策略派生。

**独立验收标准**: 任意 API 能映射到 `permissionCode:action` 并完成 allow/deny；`permset` 高频判断直接使用 StackExchange.Redis `SISMEMBER`；Redis 不成为权限真相库。

- [ ] T036 [P] [US3] Define `IRbacPermissionChecker` request and result contracts in `src/Rbac.Application/Authorization/IRbacPermissionChecker.cs`
- [ ] T037 [P] [US3] Define API route-to-permission mapper contract using project, HTTP method, route pattern, action, and ASP.NET Core `RouteTemplate.TryParse` + `TemplateMatcher` matching in `src/Rbac.Application/Authorization/IRbacApiPermissionMapper.cs`
- [ ] T038 [P] [US3] Define `IRbacSnapshotService` for building user snapshots from MySQL/Casbin policy in `src/Rbac.Application/Snapshots/IRbacSnapshotService.cs`
- [ ] T039 [P] [US3] Define `IRbacPermsetBuilder` that only accepts MySQL/Casbin-derived policy inputs in `src/Rbac.Application/Snapshots/IRbacPermsetBuilder.cs`
- [ ] T040 [US3] Implement StackExchange.Redis `SISMEMBER` access for `rbac:permset:{project}:{userid}` in `src/Rbac.Infrastructure.Redis/RbacPermsetStore.cs`
- [ ] T041 [US3] Implement FusionCache wrapper for snapshots, API maps, project grants, and menu trees but not permset SISMEMBER in `src/Rbac.Infrastructure.Redis/RbacFusionCacheFacade.cs`
- [ ] T042 [US3] Implement NetCasbin model loader for RBAC with domains in `src/Rbac.Infrastructure.Casbin/RbacCasbinModelProvider.cs`
- [ ] T043 [US3] Implement policy version detection and Enforcer reload boundary in `src/Rbac.Infrastructure.Casbin/CasbinPolicyVersionWatcher.cs`
- [ ] T044 [US3] Implement permission checker orchestration order Redis permset -> NetCasbin -> snapshot rebuild in `src/Rbac.Application/Authorization/RbacPermissionChecker.cs`
- [ ] T045 [US3] Add deny-by-default handling for unmapped APIs except explicit anonymous allowlist in `src/Rbac.Api/Filters/RbacAuthorizationFilter.cs`
- [ ] T046 [US3] Document runtime authorization sequence, route matching via ASP.NET Core `RouteTemplate.TryParse` + `TemplateMatcher`, and cache fallback behavior in `docs/rbac/runtime-authorization-flow.md`

## Phase 6: User Story 4 - 前端 menus / auth() / v-auth 兼容输出 (P1)

**目标**: 后端返回当前 `project` 与当前用户授权范围内的 `menus`，继续兼容前端 `menus -> authNode -> auth() / v-auth` 机制。

**独立验收标准**: 前端无需感知 Casbin；菜单、按钮、排序、编辑、删除所需字段完整；按钮权限节点可生成 `add/edit/del/sortable`。

- [ ] T047 [P] [US4] Define frontend menu DTO with `DxE_id`, `pid`, `title`, `name`, `path`, `type`, `menu_type`, `url`, `component`, `extend`, `keepalive`, `children`, `permissionCode`, and `ruleCode` in `src/Rbac.Application/Contracts/Menus/RbacMenuDtos.cs`
- [ ] T048 [P] [US4] Define login and backend index compatibility DTOs for `userInfo`, `routePath`, `adminInfo`, and `menus` in `src/Rbac.Application/Contracts/Compatibility/BackendIndexDtos.cs`
- [ ] T049 [US4] Implement project menu tree cache read path using `rbac:menu-tree:{project}` in `src/Rbac.Application/Menus/RbacProjectMenuTreeService.cs`
- [ ] T050 [US4] Implement user menu pruning by `permissionCode` without calling ES or exposing Casbin in `src/Rbac.Application/Menus/RbacMenuBuilder.cs`
- [ ] T051 [US4] Implement button node inclusion rules for `add`, `edit`, `del`, and `sortable` in `src/Rbac.Application/Menus/RbacButtonPermissionNodeBuilder.cs`
- [ ] T052 [US4] Implement backend initialization use case returning JWT-compatible admin info and menus without `refreshToken`, `siteConfig`, or `terminal` in `src/Rbac.Application/Backend/RbacBackendIndexService.cs`
- [ ] T053 [US4] Implement compatibility routePath behavior for successful login and auth failure redirects in `src/Rbac.Application/Authentication/RbacLoginResultFactory.cs`
- [ ] T054 [US4] Document frontend compatibility contract for menus, buttons, and `DxE_id` string behavior in `docs/rbac/frontend-compatibility-contract.md`

## Phase 7: User Story 5 - 管理端 ES7 查询与审计检索 (P2)

**目标**: 管理页面查询优先走 ES7，支持精确过滤、模糊搜索、全字段搜索和审计检索；编辑、删除、保存必须回写 MySQL。

**独立验收标准**: 用户、权限组、规则、权限视图、审计日志均可按 project/status/userid/permissionCode 精确过滤，并可用 allText 模糊搜索；ES 结果不能作为保存真相。

- [ ] T055 [P] [US5] Define ES index mapping for `rbac_user_index` with `DxE_id` keyword and allText copy_to from `userid`, `username`, `groupNames`, `projectCodes`, `groupCodes`, `status`, and `DxE_id` in `src/Rbac.Infrastructure.Elasticsearch/Indexes/RbacUserIndexMapping.cs`
- [ ] T056 [P] [US5] Define ES index mapping for `rbac_group_index` with allText copy_to from `groupCode`, `groupName`, `parentGroupCode`, `ruleCodes`, `permissionCodes`, `project`, `status`, and `DxE_id` in `src/Rbac.Infrastructure.Elasticsearch/Indexes/RbacGroupIndexMapping.cs`
- [ ] T057 [P] [US5] Define ES index mapping for `rbac_rule_index` with allText copy_to from `ruleCode`, `permissionCode`, `parentRuleCode`, `title`, `name`, `path`, `type`, `menu_type`, `component`, `url`, `project`, `status`, and `DxE_id` in `src/Rbac.Infrastructure.Elasticsearch/Indexes/RbacRuleIndexMapping.cs`
- [ ] T058 [P] [US5] Define ES index mapping for `rbac_permission_view_index` with allText copy_to from `permissionCode`, `ruleCode`, `action`, `resourceType`, `title`, `path`, `groupCodes`, `groupNames`, `project`, and `status` in `src/Rbac.Infrastructure.Elasticsearch/Indexes/RbacPermissionViewIndexMapping.cs`
- [ ] T059 [P] [US5] Define ES index mapping for `rbac_audit_log_index` with allText copy_to from `auditId`, `traceId`, `userid`, `project`, `requestedProject`, `permissionCode`, `action`, `result`, `reason`, `apiPath`, `httpMethod`, `clientIp`, and `userAgent` in `src/Rbac.Infrastructure.Elasticsearch/Indexes/RbacAuditLogIndexMapping.cs`
- [ ] T060 [US5] Implement NEST query builders for exact filters, date ranges, and allText fuzzy search in `src/Rbac.Infrastructure.Elasticsearch/Search/RbacElasticQueryBuilder.cs`
- [ ] T061 [US5] Implement management search application service that reads ES and maps to `data.list/data.total` in `src/Rbac.Application/Search/RbacManagementSearchService.cs`
- [ ] T062 [US5] Implement save safeguards that re-load MySQL by `project + DxE_id` or Guid before edit/delete operations in `src/Rbac.Application/Management/RbacManagementWriteGuard.cs`
- [ ] T063 [US5] Document ES query-only rule and MySQL write-truth rule in `docs/rbac/elasticsearch-management-query.md`

## Phase 8: User Story 6 - Outbox 同步、缓存失效与 ES 全量重建 (P2)

**目标**: 权限配置变更后，通过 MySQL Outbox 驱动 Redis 版本递增、缓存失效、ES 增量同步、Casbin policy 刷新，并提供 ES 全量重建能力。

**独立验收标准**: MySQL 写入与 Outbox 同事务；同步失败可重试；ES 可 alias 全量重建；缓存失效不依赖扫描 10W 用户 key。

- [ ] T064 [P] [US6] Define Outbox persistence mapping, processing status, and persisted payload schema validation without generating migrations in `src/Rbac.Infrastructure.MySql/Outbox/RbacOutboxMapping.cs`
- [ ] T065 [P] [US6] Define cache invalidation event payload for project, userid, groupCode, resourceType, version, and traceId in `src/Rbac.Application/Cache/RbacCacheInvalidationEvents.cs`
- [ ] T066 [US6] Implement `IRbacCacheInvalidator` for version increments, targeted key deletion, and invalidation publishing in `src/Rbac.Infrastructure.Redis/RbacCacheInvalidator.cs`
- [ ] T067 [US6] Implement Outbox event processor for Redis invalidation and version updates in `src/Rbac.Worker/Outbox/RbacRedisOutboxProcessor.cs`
- [ ] T068 [US6] Implement Outbox event processor for ES incremental indexing in `src/Rbac.Worker/Outbox/RbacElasticsearchOutboxProcessor.cs`
- [ ] T069 [US6] Implement Outbox event processor for Casbin policy version refresh in `src/Rbac.Worker/Outbox/RbacCasbinOutboxProcessor.cs`
- [ ] T070 [US6] Implement ES full reindex service with versioned index names and alias switch in `src/Rbac.Infrastructure.Elasticsearch/Reindex/RbacEsFullReindexService.cs`
- [ ] T071 [US6] Implement ES full reindex worker entry point with count validation and failure-safe alias behavior in `src/Rbac.Worker/Reindex/RbacEsReindexWorker.cs`
- [ ] T072 [US6] Document Outbox consistency, retry, compensation, and ES full rebuild operations in `docs/rbac/outbox-sync-and-reindex.md`

## Phase 9: User Story 7 - 10W 用户规模性能、灰度迁移与观测 (P3)

**目标**: 在 10W 用户规模下避免大 key、避免全量清理缓存，支持热点预热、版本懒失效、灰度迁移和可观测性。

**独立验收标准**: 权限变更不扫描删除所有用户 key；关键链路有指标和审计；可按 project 灰度；热点数据可预热。

- [ ] T073 [P] [US7] Define metrics for Redis hit rate, FusionCache L1/L2 hit rate, Casbin Enforce QPS, ES sync lag, and Outbox retry count in `src/Rbac.Application/Observability/RbacMetrics.cs`
- [ ] T074 [P] [US7] Define audit log write contract for allow/deny/error authorization results in `src/Rbac.Application/Auditing/RbacAuthorizationAuditWriter.cs`
- [ ] T075 [US7] Implement cache warmup plan for active project menu-tree, api-map, hot user snapshots, menus, and Casbin policy in `src/Rbac.Worker/Warmup/RbacCacheWarmupWorker.cs`
- [ ] T076 [US7] Implement lazy version invalidation strategy for project, user, group, menu, and policy versions in `src/Rbac.Application/Cache/RbacVersionValidationService.cs`
- [ ] T077 [US7] Implement safeguards preventing single Redis key from storing all users or all permissions in `src/Rbac.Infrastructure.Redis/RbacRedisKeyGuard.cs`
- [ ] T078 [US7] Define project-level gray migration flags and read-only comparison mode in `src/Rbac.Application/Migration/RbacGrayMigrationOptions.cs`
- [ ] T079 [US7] Implement PHP-vs-new-RBAC menu and permission comparison report contract in `src/Rbac.Application/Migration/RbacCompatibilityDiffService.cs`
- [ ] T080 [US7] Document 10W-user performance model, warmup plan, gray migration steps, and rollback criteria in `docs/rbac/scale-and-gray-migration.md`

## Final Phase: Polish & Cross-Cutting

**目标**: 完成安全复核、文档复核、任务边界复核和发布前检查。

- [ ] T081 [P] Verify every public DTO returns `DxE_id` as string and never JSON number in `docs/rbac/dxe-id-string-verification.md`
- [ ] T082 [P] Verify no service reads raw project directly after `CurrentRbacContext` middleware in `docs/rbac/project-context-verification.md`
- [ ] T083 [P] Verify Redis `permset` write paths only originate from MySQL/Casbin-derived builders in `docs/rbac/permset-truth-verification.md`
- [ ] T084 [P] Verify ES full reindex and Outbox incremental sync are both documented and wired in `docs/rbac/es-sync-verification.md`
- [ ] T085 [P] Verify FusionCache is not used for high-frequency `SISMEMBER`, Redis increments, locks, or Pub/Sub in `docs/rbac/fusioncache-boundary-verification.md`
- [ ] T086 Review all API authorization behavior for deny-by-default, project-scoped super, and audit logging in `docs/rbac/security-review-checklist.md`
- [ ] T087 Update operational runbook for Redis, ES7, FusionCache, NetCasbin, Outbox, and worker recovery in `docs/rbac/operations-runbook.md`
- [ ] T088 Produce final compatibility sign-off checklist for existing intranet frontend systems in `docs/rbac/frontend-signoff-checklist.md`

## Implementation Blocking Corrections

**目标**: 修正实现前阻塞项，补齐 `DxE_id` 生成、Casbin Enforcer reload、permset 重建冲突、Pub/Sub 订阅、allowlist、ES bootstrap 和审计接线。

### DxE_id 生成器任务

- [ ] T089 [P] [US2] [MVP-Blocker] Define `IRbacDxEIdGenerator` interface for centralized `DxE_id` generation in `src/Rbac.Application/Identity/IRbacDxEIdGenerator.cs`
- [ ] T090 [P] [US2] [MVP-Blocker] Define snowflake or distributed ID implementation boundary that always returns string `DxE_id` in `src/Rbac.Infrastructure.MySql/Identity/RbacDxEIdGenerationOptions.cs`
- [ ] T091 [US2] [MVP-Blocker] Define migration import rule allowing legacy `DxE_id` preservation with conflict reporting in `src/Rbac.Application/Identity/RbacDxEIdImportPolicy.cs`
- [ ] T092 [US2] [MVP-Blocker] Define uniqueness validation for global or `project + entityType + DxE_id` scope in `src/Rbac.Domain/Validation/RbacDxEIdUniquenessRules.cs`

### Casbin policy store / adapter 任务

- [ ] T093 [P] [US3] [MVP-Blocker] Define MySQL-backed Casbin policy reader for loading `g` policies from userid-group-project truth tables in `src/Rbac.Application/Policies/ICasbinGroupingPolicyReader.cs`
- [ ] T094 [P] [US3] [MVP-Blocker] Define MySQL-backed Casbin policy reader for loading `p` policies from group-permission-action truth tables in `src/Rbac.Application/Policies/ICasbinPermissionPolicyReader.cs`
- [ ] T095 [US3] [MVP-Blocker] Document that Casbin adapter is a loading mechanism and never the business truth source in `docs/rbac/casbin-policy-store-boundary.md`

### ICasbinPolicySyncService 实现任务

- [ ] T096 [US3] [MVP-Blocker] Define `ICasbinPolicySyncService` reload contract that reloads policy from MySQL truth tables in `src/Rbac.Application/Policies/ICasbinPolicySyncService.cs`
- [ ] T097 [US3] [MVP-Blocker] Define new-Enforcer build flow that does not mutate the shared Enforcer instance in `src/Rbac.Infrastructure.Casbin/CasbinEnforcerFactory.cs`
- [ ] T098 [US3] [MVP-Blocker] Define atomic Enforcer reference replacement and fallback-to-old-Enforcer behavior in `src/Rbac.Infrastructure.Casbin/CasbinEnforcerProvider.cs`
- [ ] T099 [US3] [MVP-Blocker] Define Casbin reload success/failure audit logging contract in `src/Rbac.Application/Auditing/CasbinReloadAuditEvents.cs`

### permset 重建协调任务

- [ ] T100 [US3] [MVP-Blocker] Define request-path lazy permset rebuild flow for cache miss and version stale cases in `src/Rbac.Application/Snapshots/RbacPermsetLazyRebuildCoordinator.cs`
- [ ] T101 [US6] [Deferred-Outbox] Define Worker-triggered permset invalidation and optional hot-key prewarm flow after permission changes in `src/Rbac.Worker/Cache/RbacPermsetInvalidationWorker.cs`
- [ ] T102 [US3] [MVP-Blocker] Define compare-before-write version check for rebuilt permset in `src/Rbac.Infrastructure.Redis/RbacPermsetVersionedWriter.cs`
- [ ] T103 [US3] [MVP-Blocker] Define stale rebuild discard rule when version changes during permset generation in `src/Rbac.Application/Snapshots/RbacPermsetConflictPolicy.cs`

### Redis Pub/Sub 订阅端任务

- [ ] T104 [US6] [Deferred-Outbox] Define Redis Pub/Sub subscriber for `rbac.cache.invalidate` events in `src/Rbac.Infrastructure.Redis/RbacCacheInvalidationSubscriber.cs`
- [ ] T105 [US6] [Deferred-Outbox] Define FusionCache L1 eviction behavior when subscriber receives user-level or project-level invalidation events in `src/Rbac.Infrastructure.Redis/RbacFusionCacheEvictionHandler.cs`
- [ ] T106 [US6] [Deferred-Outbox] Document fallback behavior for lost Pub/Sub events using short L1 TTL and version checks in `docs/rbac/redis-pubsub-invalidation.md`

### 匿名/白名单路由注册任务

- [ ] T107 [P] [US3] [MVP-Blocker] Define centralized anonymous and allowlist route options for login, health, swagger, and static resources in `src/Rbac.Api/Options/RbacAllowlistOptions.cs`
- [ ] T108 [US3] [MVP-Blocker] Define deny-by-default behavior for APIs without permissionCode unless matched by centralized allowlist in `src/Rbac.Api/Filters/RbacAllowlistAuthorizationPolicy.cs`

### ES index template / alias bootstrap 任务

- [ ] T109 [P] [US5] [Deferred-ES] Define ES index template bootstrap service for mappings and analyzers in `src/Rbac.Infrastructure.Elasticsearch/Bootstrap/RbacEsIndexTemplateBootstrapper.cs`
- [ ] T110 [US5] [Deferred-ES] Define ES alias initialization service for query aliases before first indexing in `src/Rbac.Infrastructure.Elasticsearch/Bootstrap/RbacEsAliasBootstrapper.cs`
- [ ] T111 [US6] [Deferred-ES/Outbox] Define pre-reindex alias validation that checks alias existence and single-current-index target in `src/Rbac.Infrastructure.Elasticsearch/Reindex/RbacEsAliasPreflightChecker.cs`

### 审计日志写入接线任务

- [ ] T112 [US3] [MVP-Blocker] Define non-blocking audit event emission from `RbacAuthorizationFilter` for allow, deny, and error results in `src/Rbac.Api/Filters/RbacAuthorizationAuditEmitter.cs`
- [ ] T113 [US3] [MVP-Blocker] Define non-blocking audit event emission from `RbacPermissionChecker` for Redis, Casbin, and fallback decisions in `src/Rbac.Application/Auditing/RbacPermissionAuditEmitter.cs`
- [ ] T114 [US6] [Deferred-Outbox] Define asynchronous audit sink using in-memory queue or Outbox without blocking hot authorization requests in `src/Rbac.Worker/Auditing/RbacAuditEventWorker.cs`

## Dependencies

### Phase Dependencies

- Phase 1 Setup must complete before Phase 2.
- Phase 2 Foundational blocks all user stories.
- US1 and US2 are both P1 and should complete before US3 and US4.
- US3 depends on US1 project context and US2 policy/domain contracts.
- US4 depends on US1 context and US2 menu/rule contracts, but can proceed in parallel with US3 after shared contracts are stable.
- US5 depends on US2 domain contracts and can proceed after MySQL truth DTOs are stable.
- US6 depends on US2, US3, and US5 contracts.
- US7 depends on US3, US4, and US6 for meaningful metrics and scale validation.
- Implementation Blocking Corrections marked `[MVP-Blocker]` must complete before coding MVP runtime authorization.
- Tasks marked `[Deferred-ES]`, `[Deferred-Outbox]`, or `[Deferred-ES/Outbox]` may be scheduled with US5/US6 instead of MVP.

### Story Dependency Graph

```text
Setup -> Foundation -> US1
Setup -> Foundation -> US2
US1 + US2 -> US3
US1 + US2 -> US4
US2 -> US5
US2 + US3 + US5 -> US6
US3 + US4 + US6 -> US7
```

## Parallel Execution Examples

### Setup / Foundation

```text
T002, T003, T004, T005, T006 can run in parallel after T001.
T007, T008, T009, T010, T011, T012, T013, T014 can run in parallel after project skeleton exists.
```

### US1

```text
T017 and T018 can run in parallel.
T019 and T020 can run in parallel after T017/T018.
T023 and T024 can run after T021 defines resolver behavior.
```

### US2

```text
T026, T027, T028, T029, T030 can run in parallel.
T032 and T033 can run after domain aggregates are defined.
```

### US3

```text
T036, T037, T038, T039 can run in parallel.
T040, T041, T042 can run in parallel after contracts are stable.
T044 waits for T040, T041, and T042.
```

### US4

```text
T047 and T048 can run in parallel.
T049 and T051 can run in parallel.
T050 waits for T049 and T051.
```

### US5

```text
T055, T056, T057, T058, T059 can run in parallel.
T060 waits for index contracts.
T061 waits for T060.
```

### US6

```text
T064 and T065 can run in parallel.
T067, T068, T069 can run in parallel after T064.
T070 and T071 can run as a focused ES rebuild slice.
```

### US7

```text
T073 and T074 can run in parallel.
T075, T076, T077, T078 can run in parallel after core runtime services exist.
```

## Implementation Strategy

### MVP First

MVP 建议只包含：

1. Phase 1 Setup
2. Phase 2 Foundational
3. US1 JWT + project context
4. US2 MySQL truth model contracts
5. US3 runtime authorization skeleton
6. US4 frontend `menus` compatibility skeleton
7. Implementation Blocking Corrections marked `[MVP-Blocker]`: T089-T100, T102-T103, T107-T108, T112-T113

MVP 验收重点：

- 能解析 JWT 得到 `userid`。
- 能前置校验 `project` 并生成 `CurrentRbacContext`。
- 能以 `permissionCode:action` 判断接口权限。
- 能返回前端可构建 `auth()` / `v-auth` 的 `menus`。
- `DxE_id` 始终按 string 输出。
- `DxE_id` 由 RBAC 中心统一生成或迁移导入保留，并完成唯一性校验。
- Casbin reload 不阻塞 Enforce，请求期间旧 Enforcer 可继续服务。
- `permset` 重建采用 version compare-before-write，版本冲突时丢弃旧结果。
- 匿名/白名单路由集中配置，未映射 `permissionCode` 的接口默认拒绝。
- 鉴权 allow/deny/error 审计事件异步写入，不阻塞热路径。

### Incremental Delivery

1. 先交付兼容 API 与服务端鉴权，不引入 ES 管理查询作为阻塞项。
2. 再交付 ES7 管理查询和审计检索。
3. 再交付 ES index template / alias bootstrap、Outbox、全量重建、缓存失效和 Casbin policy refresh。
4. 最后交付 10W 用户规模优化、灰度迁移和运维观测。

### Blocking vs Deferred Additions

MVP 阻塞项：

- T089-T092: `DxE_id` 生成、迁移保留和唯一性规则。
- T093-T099: MySQL policy store、Casbin reload、新 Enforcer 原子替换和 reload 审计。
- T100, T102-T103: 请求链路懒重建、version compare-before-write、冲突丢弃。
- T107-T108: 集中 allowlist 与默认拒绝。
- T112-T113: 鉴权 filter / checker 审计事件接线。

可延后到 ES/Outbox 阶段：

- T101: Worker 事件触发的 permset 预热/失效。
- T104-T106: Redis Pub/Sub 订阅端和 L1 驱逐。
- T109-T111: ES template、alias bootstrap、reindex alias preflight。
- T114: 审计事件异步 Worker。

### Validation Checklist

- 所有任务均使用 `- [ ] Txxx` 格式。
- User story 阶段任务均带 `[USx]` 标签。
- 可并行任务均带 `[P]` 标签。
- 每个任务都包含明确文件路径。
- 本任务清单不要求立即写代码、不生成数据库迁移、不修改业务实现文件。
