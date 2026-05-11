# Redis + ES7 + FusionCache + NetCasbin 完整增强设计

## 0. 设计前提

本设计基于以下文档继续展开：

- `rbac-contract-readonly-audit.md`
- `rbac-frontend-compatibility-contract.md`
- `rbac-intranet-compatibility-boundary-analysis.md`
- `ASP.NET Core RBAC 重构实施计划.md`

目标系统是独立 ASP.NET Core RBAC 权限中心。它不复刻 PHP，不继续使用 ABP，不分析会员 RBAC，不生成数据库迁移，不开始编码实现。

已确定技术边界：

- MySQL 是 RBAC 配置数据、用户授权关系、菜单规则、策略关系的真相库。
- Redis 是运行态缓存层，存权限快照、权限码 Set、菜单快照、版本号、project 授权缓存。
- FusionCache 负责 L1 本地内存缓存 + L2 Redis 分布式缓存的统一访问和防击穿能力。
- NetCasbin 是运行态权限判断引擎，判断维度为 `userid + project + permissionCode + action`。
- Elasticsearch 7.x 仅用于权限管理后台查询和审计查询，不参与实时接口鉴权。
- ES .NET 客户端使用 NEST 7.17.x。
- Redis 客户端使用 StackExchange.Redis。
- Casbin 策略持久化优先设计为 MySQL 真相库；Redis 只做策略缓存和版本缓存。
- 前端继续使用 `menus -> authNode -> auth() / v-auth`，不感知 Casbin。
- `project` 由前端请求携带，但后端必须校验 `userid` 是否授权访问该 project。
- `DxE_id` 是历史兼容业务 ID，不作为长期权限判断依据。
- 权限判断长期依赖 `permissionCode` / `ruleCode`。
- 10W 用户规模下禁止单个大 key 存储所有用户权限。

### 0.1 实施硬性约束

以下约束属于实施门禁，后续设计、编码、评审、验收均不得违反：

1. Redis `permset` 必须由 MySQL 真相库中的 RBAC 配置和 Casbin policy 计算生成，不能在 Redis 中维护另一套权限真相。
2. `project` 校验必须前置为统一上下文，例如 `ProjectResolver` / `CurrentRbacContext`，业务 Service 只能读取已校验上下文，不能各自散落校验。
3. ES7 必须同时具备全量重建索引与 Outbox 增量同步机制，不能只定义查询 mapping。
4. FusionCache 不包办所有 Redis 操作；`permset` 高频 `SISMEMBER` 判断必须直接走 StackExchange.Redis。
5. `DxE_id` 对前端 API 一律按 string 返回，对 ES 一律按 `keyword` 存储，不能以 JSON number 返回。

### 0.2 实现前补充设计决策

#### DxE_id 生成规则

- 内部数据库主键统一使用 `Guid`。
- 对外兼容 ID 统一使用 `DxE_id`，API 永远返回 string。
- 新建记录必须由 RBAC 权限中心统一生成 `DxE_id`，业务调用方不得自行传入新 ID。
- `DxE_id` 底层可使用雪花 long 或其他分布式 ID，但序列化到 JSON 时必须转为字符串。
- 迁移旧数据时允许保留原 `DxE_id`，但必须经过唯一性校验和冲突处理。
- `DxE_id` 唯一范围推荐全局唯一；如果历史数据无法满足全局唯一，则最低要求为 `project + entityType + DxE_id` 唯一。
- `DxE_id` 只用于前端兼容、编辑、删除、排序和迁移追踪，不作为长期权限判断依据。
- 长期权限判断必须使用 `permissionCode` / `ruleCode`。

#### Casbin Enforcer 生命周期与 reload 并发策略

- 不在共享 Enforcer 实例上直接热更新 policy。
- 当 `rbac:policy-version:{project}` 变化时，后台构建新的 Enforcer 实例。
- 新 Enforcer 从 MySQL 真相表重新加载 `g` / `p` policy。
- 新 Enforcer 加载成功后，原子替换当前 Enforcer 引用。
- 新 Enforcer 构建失败时继续使用旧 Enforcer，不能让实时鉴权进入无策略状态。
- `Enforce` 请求不阻塞 reload；reload 在后台进行。
- reload 成功、失败、耗时、policy version、project 必须写审计日志或结构化日志。

#### permset 重建触发路径与冲突处理

- 请求链路遇到 cache miss 或 version stale 时，可以懒重建 `permset`。
- Worker 在权限变更后负责递增 version、删除高风险 key，并可选预热热点用户 key。
- `permset` 写入前必须读取并校验当前 version。
- 如果重建过程中 version 已变化，本次 `permset` 写入必须丢弃。
- Redis `permset` 只能由 MySQL/Casbin-derived builder 生成。
- version 新者生效，不允许 Redis 形成第二套权限真相。

## 1. 总体架构

### 1.1 组件职责边界

| 组件 | 职责 | 明确不做 |
|---|---|---|
| ASP.NET Core API | 接收 JWT 请求、解析 userid、解析 project、执行服务端鉴权、返回兼容 DTO | 不把前端 `auth()` 当作安全边界 |
| MySQL | RBAC 配置真相库：用户、project 授权、权限组、菜单规则、permissionCode、Casbin policy | 不承担高频运行态权限快照读取 |
| Redis | 运行态缓存：用户权限快照、权限码 Set、菜单快照、版本号、project 授权、API 映射、policy version | 不作为真相库，不承载全量用户大 key |
| FusionCache | 统一封装 L1 本地缓存 + L2 Redis；提供防击穿、后台刷新、故障降级 | 不替代 Redis key 模型，不包办 `permset` 高频 `SISMEMBER` |
| NetCasbin | 运行态权限判断兜底和统一策略引擎 | 不构建前端菜单树，不直接返回 menus |
| Elasticsearch 7.x | 管理端搜索、模糊查询、精确过滤、审计检索；支持全量重建和 Outbox 增量同步 | 不参与实时接口鉴权，不作为编辑真相 |
| Worker | MySQL 变更后的异步同步、ES 索引重建、Redis 失效、Casbin policy 刷新 | 不直接绕过业务服务修改权限数据 |

### 1.2 数据流分层

```text
请求鉴权链路:
API -> JWT -> Project 校验 -> API 映射 permissionCode -> FusionCache -> Redis permset -> NetCasbin -> 允许/拒绝

菜单加载链路:
API -> JWT -> Project 校验 -> FusionCache 用户菜单快照 -> Redis menus -> MySQL 重建 -> 返回 menus

管理查询链路:
Admin API -> ES 查询列表/检索 -> 返回管理页面

管理写入链路:
Admin API -> 服务端鉴权 -> MySQL 写入 -> Outbox -> Redis 失效 -> ES 重建 -> Casbin policy 刷新
```

### 1.3 核心设计判断

- 实时鉴权只依赖 Redis / FusionCache / NetCasbin / MySQL 兜底，不依赖 ES。
- 菜单树裁剪由 RBAC 业务服务完成，NetCasbin 只参与权限判断。
- 权限变更后优先通过版本号懒失效，避免批量删除 10W 用户缓存。
- 对外兼容 `DxE_id`，但前端 API 只能返回字符串；内部授权判断使用 `permissionCode`。
- 所有缓存 key 都必须包含 `project` 或能明确归属 project。
- Redis `permset` 是 MySQL/Casbin 策略的运行态派生物，不允许手工写成另一套权限来源。

## 2. 运行态鉴权链路

### 2.1 请求进入后的标准流程

1. API 接收请求。
2. JWT 中间件验证签名、过期时间、issuer、audience。
3. 从 JWT claims 解析 `userid`，例如 `sub` 或公司门户约定字段。
4. 从请求中解析 `project`，来源可以是 header、query、body 或路由，但推荐统一 header：`X-Project`。
5. 调用 `IRbacProjectResolver` 校验 `userid-project` 授权关系，并生成 `CurrentRbacContext`。
6. 后续业务 Service、鉴权器、菜单构建器只读取 `CurrentRbacContext`，不得重复散落解析或信任原始 project 入参。
7. 根据当前 API endpoint、HTTP method、业务 action 映射 `permissionCode`。
8. 读取用户权限快照版本，判断本地缓存是否仍有效。
9. 优先通过 FusionCache 获取用户权限快照、API 映射、project 授权关系等中等粒度对象。
10. 使用 StackExchange.Redis 直接对 `rbac:permset:{project}:{userid}` 执行 `SISMEMBER`，判断用户是否拥有 `permissionCode:action`。
11. Redis 缓存未命中或版本不一致时，调用 NetCasbin `Enforce(userid, project, permissionCode, action)` 兜底。
12. Casbin 仍无法判断时，可按配置选择 MySQL 重建快照后再次判断。
13. 返回允许或拒绝。

### 2.2 推荐判定顺序

```text
JWT valid?
  no -> 401
  yes

project exists?
  no -> 400
  yes

userid authorized to project?
  no -> 403
  yes

api mapped to permissionCode?
  no -> deny by default, except explicit anonymous/allowlist
  yes

super in project?
  yes -> allow, still write audit
  no

Redis permset contains permissionCode?
  yes -> allow
  no or cache stale

NetCasbin Enforce(userid/groupCode, project, permissionCode, action)?
  yes -> allow and optionally rebuild permset
  no -> 403
```

### 2.3 project 来源与校验

`project` 不来自 JWT，因为公司门户 JWT 不颁发 project。后端允许前端请求携带 `project`，但必须服务端校验。

推荐规则：

- `project` 必须是显式参数，不允许后端默认猜测跨系统权限。
- `IRbacProjectResolver` 是唯一入口，负责解析、校验并输出 `CurrentRbacContext`。
- `CurrentRbacContext` 至少包含 `userid`、`project`、`isProjectAuthorized`、`isProjectSuper`、`traceId`。
- `userid-project` 授权关系优先从 FusionCache 获取。
- FusionCache miss 后查 Redis `rbac:user-projects:{userid}`。
- Redis miss 后查 MySQL 授权表并重建缓存。
- 若 project 不在授权集合内，直接拒绝。
- 审计日志记录 `requestedProject`、`resolvedProject`、`userid`、`clientIp`、`result`。
- 业务 Service 禁止自行读取 header/body 中的 `project` 作为权限边界。

### 2.4 API 到 permissionCode 的映射

推荐建立 API 映射表：

| 字段 | 说明 |
|---|---|
| `project` | 所属系统 |
| `httpMethod` | GET/POST/PUT/DELETE |
| `routePattern` | 规范化路由模板 |
| `permissionCode` | 权限码 |
| `action` | `read/create/update/delete/execute/access` |
| `status` | 启用状态 |

路由匹配算法必须与 ASP.NET Core 框架保持一致：

- `routePattern` 使用 ASP.NET Core route template 语法，例如 `/api/users/{id}`。
- 匹配时推荐直接使用 `RouteTemplate.TryParse` + `TemplateMatcher`。
- 不允许自行用简单字符串前缀、手写正则或大小写不一致规则替代框架匹配。
- 同一 `project + httpMethod + routePattern` 下只能映射一个 `permissionCode + action`。

运行时缓存：

- Redis key：`rbac:api-map:{project}`
- FusionCache key：`rbac:api-map:{project}`
- 内容：路由模板到 `permissionCode + action` 的映射。
- 变更后递增 `rbac:policy-version:{project}` 或专用 API map version。

### 2.5 Redis Set 与 Casbin 的分工

| 判断方式 | 使用场景 | 特点 |
|---|---|---|
| Redis Set `SISMEMBER` | 高频 API 鉴权、已构建用户权限快照 | 极快，适合热路径；必须直接使用 StackExchange.Redis |
| NetCasbin Enforce | Redis miss、版本不一致、策略复杂、兜底判断 | 更完整，适合策略引擎 |
| MySQL 重建 | 缓存缺失且 Casbin policy 需要刷新 | 最慢，只做兜底和重建 |

推荐热路径优先使用 Redis Set，Casbin 做策略兜底和一致性来源之一。

`permset` 生成规则：

- 输入只能来自 MySQL 真相库中的用户-组关系、组-权限关系、菜单/按钮/API 权限配置，以及由这些数据加载出的 Casbin policy。
- 输出写入 `rbac:permset:{project}:{userid}`。
- 任何管理接口不得直接向 `permset` 添加或移除权限，以免形成第二套权限真相。
- 若 Casbin policy 与 MySQL 真相库不一致，以 MySQL 为最终修复来源，并通过 policy sync 重新生成。

### 2.6 匿名与白名单路由

实时鉴权必须默认拒绝未配置 `permissionCode` 的接口，除非接口位于集中 allowlist。

允许进入 allowlist 的典型路由：

- 登录接口。
- 健康检查。
- Swagger / OpenAPI 文档。
- 静态资源。
- 明确标记为匿名的诊断接口。

allowlist 规则：

- 必须集中配置，不允许散落在 Controller 或业务 Service 中。
- allowlist 命中也应记录基础访问日志。
- allowlist 之外的接口如果没有 `permissionCode` 映射，默认返回拒绝。
- allowlist 不得绕过 `project` 相关管理接口。

### 2.7 permset 懒重建协调

请求链路发现 `permset` miss 或 version stale 时：

1. 读取当前 `rbac:version:{project}`、`rbac:version:{project}:{userid}`、`rbac:policy-version:{project}`。
2. 使用 MySQL/Casbin-derived builder 计算候选 `permset`。
3. 写入前再次读取当前 version。
4. 若 version 未变化，写入 `rbac:permset:{project}:{userid}` 并附带快照 version。
5. 若 version 已变化，丢弃本次重建结果，由下一次请求或 Worker 重新生成。

该流程确保旧版本重建结果不会覆盖新权限。

## 3. 权限管理页面查询链路

### 3.1 查询走 ES，写入走 MySQL

管理后台查询列表、搜索、过滤优先走 ES7：

- 用户搜索。
- 权限组搜索。
- 菜单搜索。
- `permissionCode` 搜索。
- `project` 精确过滤。
- 状态过滤。
- 创建时间过滤。
- 全字段模糊搜索。
- 审计日志检索。

但以下操作必须写 MySQL：

- 新增。
- 编辑。
- 删除。
- 启用 / 禁用。
- 排序。
- 分配权限组。
- 修改 rules。
- 修改 API 到 permissionCode 映射。
- 修改 Casbin policy。

ES 只提供查询视图，不作为编辑真相。任何从 ES 返回的数据在写入前仍应按 `project + DxE_id` 或内部 `Guid` 从 MySQL 校验。

### 3.2 管理查询流程

```text
Admin API -> 服务端鉴权 -> 构造 ES bool query -> ES 返回分页结果 -> 返回前端 data.list/data.total
```

推荐过滤条件：

- `project.keyword` 精确过滤。
- `status.keyword` 精确过滤。
- `userid.keyword` 精确过滤。
- `permissionCode.keyword` 精确过滤。
- `createdAt` range 过滤。
- `allText` match / match_phrase / query_string 模糊搜索。

### 3.3 管理写入流程

```text
Admin API -> 服务端鉴权 -> MySQL transaction -> 写 Outbox -> commit
Worker -> 消费 Outbox -> Redis 失效/版本递增 -> ES 更新 -> Casbin policy 刷新
```

写入接口不能直接更新 ES 后就返回“最终成功”。ES 更新失败时，不影响 MySQL 真相库，但必须进入补偿队列并在管理端显示同步状态或告警。

### 3.4 ES 全量重建与增量同步要求

ES7 不能只设计 mapping，必须同时提供两条同步链路：

| 链路 | 触发场景 | 数据来源 | 结果 |
|---|---|---|---|
| Outbox 增量同步 | 管理端新增、编辑、删除、启停、排序、授权变更 | MySQL 变更事件 | 更新单条或小批量 ES 文档 |
| 全量重建索引 | 新索引上线、mapping 变更、数据修复、ES 同步异常恢复 | MySQL 当前全量真相数据 | 重建完整索引并切换 alias |

全量重建要求：

- 使用新索引名，例如 `rbac_user_index_v20260508_001`。
- 重建完成后通过 alias 切换，例如 `rbac_user_index` 指向新索引。
- 重建期间查询仍走旧 alias，避免管理端不可用。
- 重建结果需要校验 MySQL 记录数、ES 文档数、关键 project 分布。
- 失败时不切换 alias，保留旧索引。

Outbox 增量同步要求：

- 写 MySQL 与写 Outbox 必须同事务。
- Worker 消费 Outbox 更新 ES。
- 同一业务对象事件必须幂等。
- 增量失败进入重试和告警。
- 周期性全量校验用于发现漏同步。

## 4. Redis Key 模型

### 4.1 Key 设计原则

- 按 `project` 和 `userid` 拆分，避免单个大 key。
- 权限快照和菜单快照分离，避免菜单变化影响所有鉴权 Set。
- 高频鉴权使用 Set。
- 高频 `permset` 判断直接使用 StackExchange.Redis `SISMEMBER`，不经过 FusionCache 包装。
- 大对象使用 JSON 字符串或 MessagePack，视运维可观测性决定。
- 所有 key 都要有 TTL 或版本号校验。
- 权限变更优先递增版本号，懒失效旧快照。
- Redis key 中的权限数据都是 MySQL/Casbin 策略的派生缓存，不是权限真相。

### 4.2 Key 清单

| Key | 类型 | 内容 | TTL | 失效条件 | FusionCache | 主动删除 | 版本校验 |
|---|---|---|---|---|---|---|---|
| `rbac:snapshot:{project}:{userid}` | String/JSON | 用户完整权限快照：groups、super、permissionCodes、menuVersion、policyVersion | 30-60 min | 用户组变更、project 授权变更、规则变更、用户状态变更 | 适合 | 允许 | 必须 |
| `rbac:menus:{project}:{userid}` | String/JSON | 裁剪后的前端 `menus` 树 | 30-60 min | 菜单变更、用户权限变更、project 变更 | 适合 | 允许 | 必须 |
| `rbac:permset:{project}:{userid}` | Set | 由 MySQL/Casbin 策略生成的用户 `permissionCode:action` 集合 | 30-60 min | 权限组变更、规则变更、policy version 变化 | 不建议；高频判断直接走 StackExchange.Redis | 允许 | 必须 |
| `rbac:user-projects:{userid}` | Set | 用户可访问 project 列表 | 10-30 min | 用户 project 授权变更、用户禁用 | 适合 | 允许 | 建议 |
| `rbac:project-users:{project}` | Set | project 下授权用户列表，仅管理/统计使用 | 10-30 min | project 授权变更 | 不建议热路径使用 | 允许 | 建议 |
| `rbac:version:{project}` | String/Number | project 全局权限版本 | 不过期或长 TTL | 菜单、规则、组、policy 变更时递增 | 可 L1 短缓存 | 不建议删除，递增 | 是 |
| `rbac:version:{project}:{userid}` | String/Number | 用户级权限版本 | 不过期或长 TTL | 用户组、用户状态、用户授权变更 | 可 L1 短缓存 | 不建议删除，递增 | 是 |
| `rbac:version:{project}:group:{groupCode}` | String/Number | 权限组版本 | 不过期或长 TTL | 组规则变更、组状态变更 | 可 L1 短缓存 | 不建议删除，递增 | 是 |
| `rbac:api-map:{project}` | Hash/String | API route/method 到 permissionCode/action 映射 | 60 min | API 权限映射变更 | 适合 | 允许 | 建议 |
| `rbac:menu-tree:{project}` | String/JSON | project 全量启用菜单规则树，不含用户裁剪 | 60 min | 菜单新增、编辑、删除、排序、状态变更 | 适合 | 允许 | 必须 |
| `rbac:policy-version:{project}` | String/Number | Casbin policy 版本 | 不过期或长 TTL | policy 变更、权限组关系变更 | 可 L1 短缓存 | 不建议删除，递增 | 是 |

### 4.3 具体内容建议

#### rbac:snapshot:{project}:{userid}

```json
{
  "project": "news",
  "userid": "EMP001",
  "groups": ["GROUP_ADMIN"],
  "super": false,
  "permissionCodes": ["api:system.user.create", "button:system.user.add"],
  "ruleCodes": ["menu:system.user", "button:system.user.add"],
  "versions": {
    "project": 102,
    "user": 17,
    "policy": 51,
    "menu": 88
  },
  "createdAt": "2026-05-08T10:00:00+08:00"
}
```

#### rbac:permset:{project}:{userid}

Set member 推荐：

```text
api:system.user.create:execute
button:system.user.add:access
menu:system.user:access
```

也可以只存 `permissionCode`，但 action 维度会弱化。若长期要支持 `read/create/update/delete/execute`，建议 member 包含 action。

写入来源限制：

- 只能由 `IRbacSnapshotService` / policy sync 流程根据 MySQL 和 Casbin policy 重建。
- 不能由管理端保存接口直接写入单个权限项。
- 不能由 ES 查询结果反向生成。
- 不能作为权限编辑时的真相来源。

#### rbac:menus:{project}:{userid}

存储裁剪后的前端兼容 `menus`，必须保留：

- `DxE_id`
- `pid`
- `title`
- `name`
- `path`
- `type`
- `menu_type`
- `url`
- `component`
- `extend`
- `keepalive`
- `children`
- `permissionCode`
- `ruleCode`

`DxE_id` 在 JSON 中必须是字符串，即使底层生成算法是雪花 long。

## 5. FusionCache 缓存策略

### 5.1 使用 FusionCache 的数据

| 数据 | L1 TTL | L2 Redis TTL | 防击穿 | 后台刷新 | 失效方式 |
|---|---:|---:|---|---|---|
| project 菜单树 `rbac:menu-tree:{project}` | 30-60s | 30-60min | 开启 | 开启 | 菜单变更事件 |
| 用户权限快照 `rbac:snapshot:{project}:{userid}` | 15-60s | 30-60min | 开启 | 热点用户开启 | 用户/组/权限版本变化 |
| 用户菜单快照 `rbac:menus:{project}:{userid}` | 15-60s | 30-60min | 开启 | 热点用户开启 | 菜单版本或用户权限版本变化 |
| API permissionCode 映射 | 60-180s | 60min | 开启 | 开启 | API 映射变更事件 |
| 用户 project 授权关系 | 30-120s | 10-30min | 开启 | 可选 | 用户 project 授权变更 |
| policy version | 5-15s | 长 TTL | 不必复杂 | 不需要 | policy 递增 |
| Casbin Enforcer | 进程级单例 | 不适用 | 不适用 | policy version 变化时 reload | policy 变更事件 |

明确不走 FusionCache 包办的操作：

- `rbac:permset:{project}:{userid}` 的高频 `SISMEMBER`。
- Redis version 的原子递增。
- Redis 分布式锁。
- 发布订阅缓存失效事件。
- 大批量 Set 扫描或管理类统计。

### 5.2 L1 与 L2 的定位

- L1 本地内存用于降低 Redis 压力，TTL 必须短。
- L2 Redis 用于跨实例共享和冷启动恢复。
- 权限类缓存必须带版本号，不允许只依赖 TTL。
- 关键权限变更后通过发布事件通知本地 L1 失效。
- FusionCache 包装的是“可序列化对象读取”，不是所有 Redis 命令的统一替代层。

### 5.3 防击穿策略

FusionCache 应开启：

- 单飞请求合并，避免同一 key 多线程同时回源。
- 软超时，避免慢 MySQL 查询拖垮接口。
- 失败时短暂返回 stale 数据，但仅限读取菜单和查询类数据。
- 鉴权场景返回 stale 数据要谨慎：高风险写接口建议版本不一致时强制重建或走 Casbin 兜底。

### 5.4 后台刷新策略

适合后台刷新的数据：

- project 菜单树。
- API permissionCode 映射。
- 热点用户权限快照。
- 热点用户菜单快照。

不建议后台刷新的数据：

- 已禁用用户的权限快照。
- 已移除 project 授权的用户快照。
- 版本明确过期且涉及写权限的鉴权结果。

### 5.5 本地缓存失效通知

推荐事件主题：

```text
rbac.cache.invalidate
```

事件内容：

| 字段 | 说明 |
|---|---|
| `eventId` | 幂等 ID |
| `project` | 权限域 |
| `userid` | 可选，用户级失效 |
| `groupCode` | 可选，组级失效 |
| `resourceType` | `menu/apiMap/policy/userProject/snapshot` |
| `version` | 新版本 |
| `occurredAt` | 事件时间 |

服务实例收到事件后清理对应 L1 key，并让下一次请求从 Redis 或 MySQL 重建。

### 5.6 Redis Pub/Sub 订阅端

发布端负责发出缓存失效事件，订阅端必须在每个 API 实例中运行。

订阅端职责：

- 订阅 `rbac.cache.invalidate`。
- 收到事件后驱逐对应 FusionCache L1 本地缓存。
- 对用户级事件驱逐 `snapshot`、`menus`、project 授权等本地对象。
- 对 project 级事件驱逐 `menu-tree`、`api-map`、policy version 等本地对象。
- 订阅端处理失败时写日志，不阻塞主请求。
- 事件丢失时仍依赖短 L1 TTL + Redis version 校验兜底。

## 6. NetCasbin 模型

### 6.1 模型定位

NetCasbin 用于判断：

```text
sub = userid 或 groupCode
dom = project
obj = permissionCode
act = action
```

它不负责：

- 生成前端菜单树。
- 裁剪 `menus`。
- 替代 Redis permset。
- 暴露给前端。
- 维护独立于 MySQL 的权限真相。

### 6.2 model.conf 设计

```ini
[request_definition]
r = sub, dom, obj, act

[policy_definition]
p = sub, dom, obj, act

[role_definition]
g = _, _, _

[policy_effect]
e = some(where (p.eft == allow))

[matchers]
m = g(r.sub, p.sub, r.dom) && r.dom == p.dom && r.obj == p.obj && r.act == p.act
```

说明：

- `g = _, _, _` 表示 RBAC with domains。
- 用户和权限组关系按 project 隔离。
- `obj` 使用 `permissionCode`，不使用 `DxE_id`。
- `act` 使用 `execute/access/read/create/update/delete` 等动作。

### 6.3 示例策略

```text
g, EMP001, GROUP_ADMIN, news
p, GROUP_ADMIN, news, api:system.user.create, execute
p, GROUP_ADMIN, news, button:system.user.add, access
p, GROUP_ADMIN, news, menu:system.user, access
```

### 6.4 MySQL 真相存储方案

建议 MySQL 中至少表达两类关系：

| 类型 | 示例 | 说明 |
|---|---|---|
| 用户-组关系 | `userid, groupCode, project` | 对应 Casbin `g` |
| 组-权限策略 | `groupCode, project, permissionCode, action` | 对应 Casbin `p` |

可评估 Casbin EFCore Adapter，但建议业务真相表仍由 RBAC 领域模型掌控。Casbin adapter 可作为策略加载视图，而不是唯一业务模型。

### 6.5 Redis policy cache

推荐 key：

| Key | 类型 | 内容 |
|---|---|---|
| `rbac:policy-version:{project}` | String/Number | project policy 版本 |
| `rbac:casbin:policy:{project}` | String/JSON 或 Hash | 当前 project 策略缓存，可选 |
| `rbac:casbin:groups:{project}:{userid}` | Set | 用户在 project 下的 groupCode，可选 |

policy 变更后：

1. MySQL 写入成功。
2. 递增 `rbac:policy-version:{project}`。
3. 发布 policy invalidation 事件。
4. 各 API 实例发现 policy version 变化后 reload Enforcer。
5. 对应用户快照和 permset 通过版本懒失效。

### 6.6 MySQL policy store / adapter 边界

Casbin policy 的数据来源必须是 MySQL 真相表。

要求：

- `g` policy 从用户-权限组关系表加载。
- `p` policy 从权限组-权限码关系表加载。
- Casbin adapter 可作为加载机制，但不能成为业务真相库。
- 管理端写入权限时必须写 RBAC 领域模型和 MySQL 真相表。
- policy reload 的数据来源只能是 MySQL 真相库，不允许从 Redis `permset` 或 ES 反向生成。

### 6.7 super 处理

`super` 必须限定在 project 下：

```text
userid EMP001 is super only in project news
```

处理方式：

- Redis snapshot 中包含 `super: true/false`。
- MySQL 中保存 project 级 super 授权。
- API 鉴权时，先校验 userid-project 授权，再判断 project 下 super。
- super 仍需写审计日志。
- 不建议用全局 super 绕过所有 project。

### 6.8 Casbin 与 Redis permset 的关系

| 层 | 定位 |
|---|---|
| Redis permset | 高频热路径，直接判断权限码；内容由 MySQL/Casbin 策略生成 |
| NetCasbin | 策略兜底、复杂策略判断、重建 permset 的来源之一 |
| MySQL | 策略真相库 |

直接 Redis 判断的场景：

- 用户快照版本有效。
- `permset` 存在。
- API 映射明确。
- 非敏感或常规 API。

走 Casbin Enforce 的场景：

- `permset` miss。
- 版本不一致。
- 用户刚变更权限。
- 权限码是新增或高风险接口。
- 需要确保策略引擎结果与缓存一致。

### 6.9 Enforcer reload 并发策略

推荐使用不可变 Enforcer 引用策略：

- 当前可用 Enforcer 保存在进程内原子引用中。
- reload 时后台创建新 Enforcer。
- 新 Enforcer 从 MySQL policy store 加载完整 policy。
- 新 Enforcer 自检通过后，使用原子交换替换当前引用。
- reload 失败时保留旧引用。
- `Enforce` 请求始终读取当前引用，不等待 reload 完成。
- reload 过程必须记录 `project`、旧 version、新 version、结果、耗时、失败原因。

## 7. ES7 索引设计

### 7.1 通用 mapping 原则

- `project`、`userid`、`permissionCode`、`ruleCode`、`groupCode`、`status` 必须为 `keyword`。
- `DxE_id` 必须为 `keyword`，即使底层是雪花 long，也避免精度和序列化问题。
- 标题、名称、路径、用户名等需要 multi-field：`text` 用于模糊搜索，`.keyword` 用于精确过滤和排序。
- 中文模糊搜索预留 IK 分词器：`ik_max_word` / `ik_smart`。
- 全字段模糊搜索使用 `copy_to: allText`。
- 时间字段使用 `date`，统一 ISO 8601。

### 7.2 rbac_user_index

用途：管理员用户查询。

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | keyword | 内部 Guid 字符串 |
| `DxE_id` | keyword | 兼容业务 ID |
| `userid` | keyword | 用户业务标识 |
| `username` | text + keyword | 用户名 |
| `projectCodes` | keyword | 用户可访问 project |
| `groupCodes` | keyword | 所属权限组 |
| `groupNames` | text + keyword | 权限组名称 |
| `status` | keyword | 状态 |
| `superProjects` | keyword | 具备 super 的 project |
| `createdAt` | date | 创建时间 |
| `updatedAt` | date | 更新时间 |
| `allText` | text | 全字段搜索；copy_to 来源：`userid`, `username`, `groupNames`, `projectCodes`, `groupCodes`, `status`, `DxE_id` |

精确过滤：

- `userid.keyword`
- `projectCodes`
- `groupCodes`
- `status`
- `DxE_id`

模糊搜索：

- `username`
- `groupNames`
- `allText`

### 7.3 rbac_group_index

用途：权限组查询。

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | keyword | 内部 Guid |
| `DxE_id` | keyword | 兼容业务 ID |
| `project` | keyword | 权限域 |
| `groupCode` | keyword | 组编码 |
| `groupName` | text + keyword | 组名称 |
| `parentGroupCode` | keyword | 父级组 |
| `ruleCodes` | keyword | 规则码集合 |
| `permissionCodes` | keyword | 权限码集合 |
| `status` | keyword | 状态 |
| `createdAt` | date | 创建时间 |
| `updatedAt` | date | 更新时间 |
| `allText` | text | 全字段搜索；copy_to 来源：`groupCode`, `groupName`, `parentGroupCode`, `ruleCodes`, `permissionCodes`, `project`, `status`, `DxE_id` |

精确过滤：

- `project`
- `groupCode`
- `status`
- `permissionCodes`

模糊搜索：

- `groupName`
- `allText`

### 7.4 rbac_rule_index

用途：菜单规则和按钮规则查询。

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | keyword | 内部 Guid |
| `DxE_id` | keyword | 兼容业务 ID |
| `project` | keyword | 权限域 |
| `ruleCode` | keyword | 规则码 |
| `permissionCode` | keyword | 权限码 |
| `parentRuleCode` | keyword | 父级规则 |
| `title` | text + keyword | 菜单标题 |
| `name` | text + keyword | 前端路由/权限节点名 |
| `path` | text + keyword | 前端 path |
| `type` | keyword | `menu_dir/menu/button` |
| `menu_type` | keyword | `tab/link/iframe` |
| `component` | keyword | 组件路径 |
| `url` | keyword | 外链/iframe URL |
| `extend` | keyword | 扩展行为 |
| `keepalive` | keyword | 缓存标记 |
| `status` | keyword | 状态 |
| `weigh` | integer | 排序 |
| `createdAt` | date | 创建时间 |
| `updatedAt` | date | 更新时间 |
| `allText` | text | 全字段搜索；copy_to 来源：`ruleCode`, `permissionCode`, `parentRuleCode`, `title`, `name`, `path`, `type`, `menu_type`, `component`, `url`, `project`, `status`, `DxE_id` |

精确过滤：

- `project`
- `permissionCode`
- `ruleCode`
- `type`
- `menu_type`
- `status`
- `DxE_id`

模糊搜索：

- `title`
- `name`
- `path`
- `allText`

### 7.5 rbac_permission_view_index

用途：权限管理视图，聚合用户、组、规则、权限码，方便管理端排查。

| 字段 | 类型 | 说明 |
|---|---|---|
| `project` | keyword | 权限域 |
| `permissionCode` | keyword | 权限码 |
| `ruleCode` | keyword | 规则码 |
| `action` | keyword | 动作 |
| `resourceType` | keyword | `api/menu/button` |
| `title` | text + keyword | 展示标题 |
| `path` | text + keyword | 路由或接口路径 |
| `groupCodes` | keyword | 关联权限组 |
| `groupNames` | text + keyword | 关联权限组名称 |
| `status` | keyword | 状态 |
| `updatedAt` | date | 更新时间 |
| `allText` | text | 全字段搜索；copy_to 来源：`permissionCode`, `ruleCode`, `action`, `resourceType`, `title`, `path`, `groupCodes`, `groupNames`, `project`, `status` |

精确过滤：

- `project`
- `permissionCode`
- `ruleCode`
- `action`
- `resourceType`
- `status`

模糊搜索：

- `title`
- `path`
- `groupNames`
- `allText`

### 7.6 rbac_audit_log_index

用途：权限鉴权、管理操作、同步失败等审计检索。

| 字段 | 类型 | 说明 |
|---|---|---|
| `auditId` | keyword | 审计 ID |
| `traceId` | keyword | 请求链路 ID |
| `userid` | keyword | 用户 |
| `project` | keyword | 确认后的 project |
| `requestedProject` | keyword | 前端请求 project |
| `permissionCode` | keyword | 权限码 |
| `action` | keyword | 动作 |
| `result` | keyword | `allow/deny/error` |
| `reason` | keyword | 拒绝原因 |
| `apiPath` | text + keyword | API 路径 |
| `httpMethod` | keyword | HTTP 方法 |
| `clientIp` | ip | 客户端 IP |
| `userAgent` | text | UA |
| `createdAt` | date | 审计时间 |
| `allText` | text | 全字段搜索；copy_to 来源：`auditId`, `traceId`, `userid`, `project`, `requestedProject`, `permissionCode`, `action`, `result`, `reason`, `apiPath`, `httpMethod`, `clientIp`, `userAgent` |

精确过滤：

- `userid`
- `project`
- `permissionCode`
- `result`
- `httpMethod`
- `createdAt` range

模糊搜索：

- `apiPath`
- `userAgent`
- `allText`

### 7.7 NEST 7.17.x 使用约束

- 索引模板和 mapping 由基础设施层统一管理。
- 写 ES 使用后台 Worker，不在 MySQL 事务内同步等待 ES 成功。
- 查询 API 只读取 ES，不把 ES 文档直接作为保存 DTO。
- ES 查询结果中的 `DxE_id` 作为字符串返回。
- 所有索引中的 `DxE_id` mapping 固定为 `keyword`，不使用 `long`。
- ES 必须支持 alias 方式全量重建索引，并与 Outbox 增量同步并存。

### 7.8 Index template 与 alias bootstrap

ES 阶段开始前必须补充 bootstrap 能力：

- 创建或更新 index template / mapping。
- 初始化查询 alias，例如 `rbac_user_index`。
- 全量重建前检查 alias 是否存在、是否指向唯一当前索引。
- 重建新索引时使用版本化物理索引名。
- 重建完成后进行文档数和关键字段校验。
- 校验通过后原子切换 alias。
- alias 检查失败时停止重建并告警。

## 8. MySQL 与 Redis / ES / Casbin 同步策略

### 8.1 权限配置变更标准链路

```text
1. API 服务端鉴权
2. MySQL transaction 写入 RBAC 真相表
3. 同事务写入 Outbox 事件
4. commit
5. Worker 拉取 Outbox
6. 递增 Redis version
7. 删除或标记 Redis key
8. 发布缓存失效事件
9. 更新或重建 ES 索引
10. 刷新 Casbin policy version
11. 标记 Outbox 已完成
```

同步链路中的权限生成原则：

- MySQL 写入成功后，Redis `permset` 只能通过快照重建或 policy sync 生成。
- Casbin policy 从 MySQL 真相库加载。
- ES 索引从 MySQL 真相库或 Outbox payload 更新。
- 任何同步失败都不能通过直接手改 Redis 权限来“修复”。

### 8.2 Outbox 是否需要

需要。原因：

- MySQL 成功但 Redis 删除失败，会造成旧权限继续生效。
- MySQL 成功但 ES 更新失败，会造成管理端查询不一致。
- MySQL 成功但 Casbin 未 reload，会造成鉴权兜底不一致。

Outbox 表建议保存：

| 字段 | 说明 |
|---|---|
| `eventId` | 幂等 ID |
| `eventType` | `UserChanged/GroupChanged/MenuChanged/PolicyChanged/ProjectGrantChanged` |
| `project` | 权限域 |
| `userid` | 可选 |
| `groupCode` | 可选 |
| `payload` | 事件内容 |
| `status` | `Pending/Processing/Succeeded/Failed` |
| `retryCount` | 重试次数 |
| `nextRetryAt` | 下次重试时间 |
| `createdAt` | 创建时间 |

Outbox `payload` 字段结构必须按 `eventType` 固定，避免 Redis、ES、Casbin 三类处理器各自猜测。

| eventType | payload 字段 |
|---|---|
| `UserChanged` | `userid`, `userGuid`, `project`, `changedFields`, `oldStatus`, `newStatus`, `affectedGroupCodes`, `reason`, `operatorUserid`, `occurredAt` |
| `GroupChanged` | `groupCode`, `groupGuid`, `project`, `changedFields`, `oldRuleCodes`, `newRuleCodes`, `oldPermissionCodes`, `newPermissionCodes`, `affectedUserids`, `operatorUserid`, `occurredAt` |
| `MenuChanged` | `ruleCode`, `ruleGuid`, `DxE_id`, `project`, `changeKind`, `parentRuleCode`, `permissionCode`, `routePath`, `menuType`, `affectedPermissionCodes`, `operatorUserid`, `occurredAt` |
| `PolicyChanged` | `project`, `policyVersion`, `changeKind`, `subjectType`, `userid`, `groupCode`, `permissionCode`, `action`, `affectedUserids`, `operatorUserid`, `occurredAt` |
| `ProjectGrantChanged` | `project`, `userid`, `grantKind`, `oldProjects`, `newProjects`, `oldSuper`, `newSuper`, `operatorUserid`, `occurredAt` |
| `ApiMapChanged` | `project`, `httpMethod`, `routePattern`, `oldPermissionCode`, `newPermissionCode`, `oldAction`, `newAction`, `changeKind`, `operatorUserid`, `occurredAt` |

处理器使用约定：

- Redis 处理器优先读取 `project`, `userid`, `groupCode`, `affectedUserids`, `policyVersion`。
- ES 处理器优先读取实体标识字段，如 `userid`, `groupCode`, `ruleCode`, `DxE_id`。
- Casbin 处理器优先读取 `project`, `policyVersion`, `userid`, `groupCode`, `permissionCode`, `action`。
- 缺失必须字段时事件进入 Failed，不允许处理器自行推断。

### 8.3 Worker 选择

可选方案：

| 方案 | 适合场景 |
|---|---|
| BackgroundService | 简单部署、轻量后台任务 |
| Hangfire | 需要可视化、重试、延迟任务、运维友好 |
| Quartz.NET | 复杂调度、定时全量重建、周期性补偿 |

建议：

- Outbox 消费用 Hangfire 或 BackgroundService。
- 周期性 ES 全量校验和缓存巡检可用 Quartz.NET。
- 早期可先用 BackgroundService，生产测试环境建议 Hangfire 更容易观察失败任务。

### 8.4 同步失败补偿

同步失败处理：

- Redis 失败：重试；若多次失败，记录告警；版本号必须最终递增。
- ES 失败：Outbox 保持 Pending/Failed，后台重试；管理端可显示索引延迟。
- Casbin reload 失败：递增 `policy-version` 后实例在下次请求检测版本并重试 reload。
- 缓存失效事件丢失：实例本地 L1 TTL 很短，且请求会检查版本号，最终一致。

### 8.5 避免部分同步失败导致权限错误

- 写 MySQL 与写 Outbox 必须同事务。
- 鉴权时必须校验版本号，不只看缓存存在。
- 权限收回场景优先递增用户级或 project 级 version。
- 高风险权限收回可主动删除 `rbac:permset:{project}:{userid}`。
- 对 super 变更、用户禁用、project 授权移除，应主动删除用户快照和 permset。

### 8.6 ES 全量重建补偿

除 Outbox 增量同步外，必须提供 ES 全量重建机制：

- 支持按索引全量重建。
- 支持按 project 局部重建。
- 支持按时间窗口补偿重放 Outbox。
- 支持重建完成后 alias 原子切换。
- 支持重建前后文档数量、关键字段分布、失败记录数量校验。
- 全量重建失败不得影响当前在线索引。

### 8.7 审计日志写入接线

鉴权链路必须产生 allow / deny / error 审计事件，但热路径不能同步阻塞主请求。

推荐方式：

- `RbacAuthorizationFilter` 和 `RbacPermissionChecker` 产生审计事件。
- 审计事件先写入内存异步队列或 Outbox。
- Worker 异步写入 MySQL 审计表和 ES 审计索引。
- 审计写入失败不能影响主请求授权结果。
- 对 deny、error、project forged、policy reload failed 等高风险事件必须保证最终可追踪。

## 9. 10W 用户规模下的性能设计

### 9.1 首页初始化减少 DB 查询

首页初始化只走：

1. JWT 解析 userid。
2. `IRbacProjectResolver` 统一解析并校验 project，生成 `CurrentRbacContext`。
3. FusionCache 获取 `rbac:menus:{project}:{userid}`。
4. 未命中时 Redis 获取。
5. Redis 未命中时使用 `rbac:menu-tree:{project}` + 用户 `permset` 裁剪。
6. 必要时 MySQL 重建用户快照。

常态下不应每次首页加载都查询权限组、菜单、规则多张表。

### 9.2 按用户拆分快照

禁止：

```text
rbac:all-user-permissions
```

推荐：

```text
rbac:snapshot:{project}:{userid}
rbac:permset:{project}:{userid}
rbac:menus:{project}:{userid}
```

这样可以：

- 单用户权限变更只影响该用户。
- 热点用户可被 L1 缓存吸收。
- Redis key 大小可控。
- 过期和重建粒度清晰。

### 9.3 热点用户优化

对于高频管理员或服务账号：

- L1 TTL 可设 30-60 秒。
- 开启 FusionCache 后台刷新。
- 预热 `snapshot`、`permset`、`menus`。
- 对热点 project 的 `menu-tree` 常驻 Redis。
- API 高频鉴权对 `permset` 使用 StackExchange.Redis 直接 `SISMEMBER`，减少 FusionCache 包装开销。

### 9.4 避免清理 10W 用户 key

权限组变更时，不建议扫描所有用户 key 删除。

推荐：

- 递增 `rbac:version:{project}:group:{groupCode}`。
- 递增 `rbac:version:{project}` 或 policy version。
- 用户请求时发现 snapshot 中 group version 旧，懒失效并重建。
- 对在线高风险用户可通过 project-users 索引做小批量主动删除，但不作为常规方案。

### 9.5 缓存击穿和雪崩

措施：

- FusionCache 单飞请求合并。
- Redis TTL 加随机抖动。
- 热点 key 后台刷新。
- `menu-tree:{project}` 与用户 `menus:{project}:{userid}` 分开。
- 权限快照重建限流。
- MySQL 查询加超时和熔断。
- 批量重建通过 Worker 排队，不在请求线程堆积。

### 9.6 批量预热

预热对象：

- 活跃 project 的 `menu-tree`。
- 活跃 project 的 `api-map`。
- 最近 7 天登录过的管理员用户 snapshot。
- 高频管理用户 menus。
- Casbin Enforcer policy。

预热时机：

- 应用启动后异步预热。
- 权限发布后 Worker 预热。
- 灰度切换前批量预热。

### 9.7 灰度迁移

灰度步骤：

1. 双写审计日志，不影响旧系统。
2. ASP.NET Core 权限中心只读对比 PHP 结果。
3. 对比 menus 和按钮权限差异。
4. 小 project 灰度切换。
5. 观察 Redis 命中率、Casbin Enforce QPS、ES 同步延迟。
6. 扩大 project 范围。
7. 关闭旧权限接口。

## 10. 模块划分建议

### 10.1 推荐项目结构

```text
Rbac.Api
Rbac.Application
Rbac.Domain
Rbac.Infrastructure.MySql
Rbac.Infrastructure.Redis
Rbac.Infrastructure.Elasticsearch
Rbac.Infrastructure.Casbin
Rbac.Worker
```

### 10.2 模块职责

| 模块 | 职责 |
|---|---|
| `Rbac.Api` | Controller、JWT、project 解析、统一响应、鉴权过滤器 |
| `Rbac.Application` | 用例服务、DTO、菜单构建、权限快照、缓存失效编排 |
| `Rbac.Domain` | RBAC 聚合、权限组、菜单规则、project 授权、策略模型 |
| `Rbac.Infrastructure.MySql` | EF Core / Dapper、MySQL repository、Outbox |
| `Rbac.Infrastructure.Redis` | StackExchange.Redis key 操作、版本号、Set、分布式锁 |
| `Rbac.Infrastructure.Elasticsearch` | NEST 7.17.x 索引、查询、索引写入 |
| `Rbac.Infrastructure.Casbin` | NetCasbin Enforcer、policy 加载、policy version |
| `Rbac.Worker` | Outbox 消费、ES 同步、Redis 失效、Casbin reload、预热 |

### 10.3 核心接口

| 接口 | 职责 |
|---|---|
| `IRbacPermissionChecker` | 统一接口鉴权，封装 Redis + Casbin 判断 |
| `IRbacSnapshotService` | 构建和读取用户权限快照 |
| `IRbacProjectResolver` | 解析并校验 `userid-project` 授权 |
| `IRbacMenuBuilder` | 根据菜单树和权限码裁剪前端 `menus` |
| `IRbacPolicyStore` | 从 MySQL 读取和维护策略真相 |
| `IRbacSearchIndexer` | 写入和重建 ES 索引 |
| `IRbacCacheInvalidator` | 递增版本、删除 key、发布失效事件 |
| `ICasbinPolicySyncService` | 同步 MySQL policy 到 NetCasbin Enforcer |

新增上下文与同步接口：

| 接口 / 对象 | 职责 |
|---|---|
| `ICurrentRbacContextAccessor` | 在请求生命周期内提供已校验的 `CurrentRbacContext` |
| `CurrentRbacContext` | 保存 `userid`、`project`、`isProjectAuthorized`、`isProjectSuper`、`traceId`、`policyVersion` |
| `IRbacPermsetBuilder` | 根据 MySQL/Casbin 策略生成 Redis `permset`，禁止直接接收前端权限数据 |
| `IEsFullReindexService` | 执行 ES 全量重建、alias 切换、重建校验 |
| `IOutboxEventProcessor` | 消费 Outbox 并驱动 Redis/ES/Casbin 增量同步 |

### 10.4 接口调用边界

- `Rbac.Api` 不直接访问 Redis / ES / MySQL 表。
- `Rbac.Application` 通过接口编排流程。
- `Rbac.Domain` 不依赖基础设施。
- `Rbac.Infrastructure.*` 不包含业务决策。
- `Rbac.Worker` 只消费事件和执行同步，不绕过领域规则。
- 业务 Service 不解析原始 project，不直接相信前端 project，只使用 `CurrentRbacContext`。
- 高频 `permset` 判断封装在 `IRbacPermissionChecker` 内部，直接调用 StackExchange.Redis。

## 11. 明确反模式

以下设计明确不允许：

| 反模式 | 原因 |
|---|---|
| ES 参与实时鉴权 | ES 是查询视图，存在同步延迟，不适合安全边界 |
| 一个 Redis key 存所有用户权限 | 10W 用户规模下不可维护，更新和内存风险极高 |
| 前端传 project 后端直接相信 | 用户可伪造 project 导致跨系统越权 |
| `DxE_id` 作为长期权限判断依据 | 它是历史兼容 ID，不稳定，不适合跨系统权限语义 |
| Casbin 直接替代菜单树返回 | 前端仍依赖 `menus -> authNode`，菜单树由业务服务裁剪 |
| 前端直接感知 Casbin | 会破坏前端兼容合同，并泄露策略模型 |
| 只靠 `auth()` / `v-auth` 做权限安全 | 它只隐藏 DOM，不能阻止直接调用接口 |
| Redis 作为权限真相库 | Redis 可丢失、可过期，只能做运行态缓存 |
| ES 搜索结果直接作为编辑真相 | ES 有同步延迟，保存前必须回读/校验 MySQL |
| Redis `permset` 独立维护一套权限 | 会绕过 MySQL/Casbin 真相库，形成不可审计的第二套权限 |
| project 校验散落在各业务 Service | 容易漏校验或校验不一致，必须统一到 `ProjectResolver` / `CurrentRbacContext` |
| ES 只有 mapping 没有全量重建 | 索引损坏、mapping 变更、漏同步时无法生产恢复 |
| FusionCache 包装所有 Redis 操作 | 高频 `SISMEMBER`、版本递增、Pub/Sub 等命令不适合被对象缓存层包办 |
| `DxE_id` 以 JSON number 返回 | JavaScript 大整数精度丢失会破坏编辑、删除、排序和树节点 |
| 全局 super 绕过所有 project | 多系统隔离失效，必须是 project 级 super |
| 缓存只靠 TTL 不校验版本 | 权限收回可能延迟过久 |
| 雪花 long 以 JSON number 返回给前端 | JavaScript 可能精度丢失 |
| 权限变更后扫描删除 10W 用户 key | 高峰期会造成 Redis 和业务抖动 |
| API 未配置 permissionCode 默认放行 | 应默认拒绝，除非显式匿名或 allowlist |

## 12. 最终设计结论

推荐生产化路径是：

1. MySQL 作为 RBAC 真相库，保存用户、project、权限组、菜单规则、permissionCode、policy。
2. Redis 保存运行态快照和版本，不保存不可恢复的真相。
3. Redis `permset` 由 MySQL/Casbin 策略派生，作为高频鉴权热路径。
4. FusionCache 统一包装中等粒度对象读取，吸收热点和防击穿，但不包办 `SISMEMBER`。
5. `ProjectResolver` / `CurrentRbacContext` 统一完成 project 校验，业务服务只读上下文。
6. NetCasbin 作为策略引擎和兜底鉴权，不参与前端菜单树构建。
7. ES7 作为管理端搜索和审计检索，不参与实时鉴权，并必须具备全量重建与 Outbox 增量同步。
8. 权限变更通过 Outbox + Worker 实现 Redis、ES、Casbin 最终一致。
9. `DxE_id` 对前端 API 按 string 返回，对 ES 按 keyword 存储，不返回 JSON number。
10. 10W 用户规模下通过用户级 key、版本懒失效、热点预热避免全量 key 操作。
11. 前端兼容合同保持 `menus -> authNode -> auth() / v-auth`，后端安全边界由 ASP.NET Core 服务端鉴权保证。
