# RBAC 权限中心 — 项目依赖规则

本文档定义各项目之间允许和禁止的依赖关系。违反依赖规则会破坏分层隔离，导致领域逻辑泄露到基础设施层或反向依赖。

---

## 依赖关系图

```
Rbac.Api
  └── Rbac.Application
  └── Rbac.Infrastructure.MySql
  └── Rbac.Infrastructure.Redis
  └── Rbac.Infrastructure.Elasticsearch
  └── Rbac.Infrastructure.Casbin

Rbac.Application
  └── Rbac.Domain

Rbac.Domain
  (无依赖)

Rbac.Infrastructure.MySql
  └── Rbac.Application

Rbac.Infrastructure.Redis
  └── Rbac.Application

Rbac.Infrastructure.Elasticsearch
  └── Rbac.Application

Rbac.Infrastructure.Casbin
  └── Rbac.Application

Rbac.Worker
  └── Rbac.Application
  └── Rbac.Infrastructure.MySql
  └── Rbac.Infrastructure.Redis
  └── Rbac.Infrastructure.Elasticsearch
  └── Rbac.Infrastructure.Casbin
```

---

## 各层职责与边界

### Rbac.Domain
- **职责**: 聚合根、值对象、领域规则、领域事件定义。
- **允许依赖**: 无。只使用 BCL（System.*）。
- **禁止依赖**: Application、任何 Infrastructure、任何第三方框架包（EF、Redis、Casbin 等）。
- **原则**: 可以在任何环境中单独编译和测试，不受外部依赖影响。

### Rbac.Application
- **职责**: 用例服务、DTO、接口契约定义、编排逻辑、缓存失效策略。
- **允许依赖**: Rbac.Domain，以及接口所需的抽象包（如 FusionCache 接口）。
- **禁止依赖**: 任何 Infrastructure 项目，不允许直接 `new` 基础设施实现类。
- **原则**: Application 只定义接口，由 Infrastructure 实现，由 Api/Worker 组装注入。

### Rbac.Infrastructure.*
- **职责**: 实现 Application 层定义的接口，封装第三方技术细节。
- **允许依赖**: Rbac.Application（以及传递依赖 Rbac.Domain）。
- **禁止依赖**: 其他 Infrastructure 项目互相依赖，禁止 Infrastructure.Redis 引用 Infrastructure.MySql 等。
- **禁止包含**: 业务决策逻辑，权限计算逻辑，菜单裁剪逻辑。

### Rbac.Api
- **职责**: Controller、中间件、过滤器、DI 组装、配置读取、响应格式。
- **允许依赖**: Application + 所有 Infrastructure（用于 DI 注册）。
- **禁止包含**: 业务逻辑，不允许直接调用 EF DbContext 或 Redis IDatabase。

### Rbac.Worker
- **职责**: Outbox 消费、ES 同步、Redis 失效、Casbin reload、预热任务。
- **允许依赖**: Application + 所有 Infrastructure（用于 DI 注册和任务执行）。
- **禁止包含**: 业务规则，不允许绕过 Application 服务直接修改权限数据。

---

## 禁止的依赖（硬性规则）

| 禁止的引用方向 | 原因 |
|---|---|
| `Rbac.Domain` → 任何其他项目 | Domain 必须零依赖 |
| `Rbac.Application` → `Rbac.Infrastructure.*` | Application 只定义接口不依赖实现 |
| `Rbac.Infrastructure.X` → `Rbac.Infrastructure.Y` | 基础设施层不互相依赖 |
| `Rbac.Infrastructure.*` → `Rbac.Api` | 基础设施不引用宿主层 |
| `Rbac.Api` 直接操作 `DbContext` / `IDatabase` | 必须通过 Application 服务接口 |

---

## 检查方式

可在 CI 中使用 `dotnet-depends` 或 `NDepend` 检查循环依赖。手动检查时查看各 `.csproj` 的 `<ProjectReference>` 是否符合上表规则。
