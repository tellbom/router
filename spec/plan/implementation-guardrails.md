# RBAC 权限中心 — 实施门禁

本文档列出开发、评审、验收阶段均不得违反的硬性约束。任何 PR 或设计变更与以下规则冲突时，必须先修改规则并经过 review，而不是绕过规则。

---

## 1. 框架与依赖约束

| 约束 | 说明 |
|---|---|
| **禁止使用 ABP Framework** | 本系统从零实现 RBAC，不引入 ABP 任何模块或基类 |
| **禁止生成 EF Core 迁移** | 数据库 schema 由 DBA 或独立脚本管理，`dotnet ef migrations add` 不允许执行 |
| **NEST 必须锁定在 7.17.x** | 不允许升级到 `Elastic.Clients.Elasticsearch`（8.x 客户端），ES 服务端是 7.x |
| **Casbin.Net 必须支持 RBAC with domains** | model.conf 使用 `g = _, _, _` 三元组，选包时必须验证 |

---

## 2. 认证与 Token 约束

| 约束 | 说明 |
|---|---|
| **禁止实现 refreshToken** | 公司门户统一负责 token 生命周期，本系统只消费 JWT，不颁发也不刷新 |
| **禁止读取 PHP batoken** | 旧系统 batoken 不被信任，不在任何中间件或过滤器中解析 |
| **禁止实现 siteConfig / terminal 字段** | 登录响应 DTO 不包含这些字段，前端从其他接口获取 |

---

## 3. 权限模型约束

| 约束 | 说明 |
|---|---|
| **禁止分析或实现会员 RBAC** | 本系统仅管理后台管理员权限，会员权限由其他系统负责 |
| **禁止复刻 PHP 权限逻辑** | 以新设计文档为准，不参照 PHP 代码实现 |
| **permset 只能由 MySQL/Casbin 派生** | 任何直接向 Redis permset 写入权限的代码路径均不允许合并 |
| **project 校验必须集中在 ProjectResolver** | 业务 Service 只能读 `CurrentRbacContext`，不允许直接读 Header/Query 中的 project |
| **DxE_id 必须以 string 序列化** | 所有对外 DTO 和 ES mapping 中 DxE_id 字段不得为 number/long 类型 |
| **全局 super 不允许存在** | super 权限必须 project 级别，不得实现绕过所有 project 的全局 super |

---

## 4. ES 与缓存约束

| 约束 | 说明 |
|---|---|
| **ES 不参与实时鉴权** | 鉴权链路只走 Redis / FusionCache / NetCasbin / MySQL，禁止在鉴权 filter 中查询 ES |
| **ES 必须有全量重建能力** | 不允许只定义 mapping 没有 reindex + alias 切换机制 |
| **FusionCache 不包办 SISMEMBER** | permset 的高频判断必须直接用 `StackExchange.Redis`，不经过 FusionCache |
| **禁止单 key 存储所有用户权限** | key 必须按 `{project}:{userid}` 拆分 |

---

## 5. 违规处理

- 代码审查发现违反上述约束 → PR 不允许合并，需修正后重新提交
- 架构决策需要调整上述约束 → 先更新 `docs/rbac/adr-001-rbac-runtime-architecture.md` 并经过 review，再修改代码
