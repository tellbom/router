# Global System Bootstrap — `__global__` 保留系统说明

**文档版本**: GA1  
**设计依据**: `unified-permission-center-plan-2.md`  
**适用范围**: 统一权限中心（Unified Permission Center）后端初始化

---

## 1. 模型说明

统一权限中心不引入第二套授权模型。它是一个**普通 RBAC project**，project code 为 `__global__`（见 `RbacGlobalConstants.ReservedProjectCode`），由现有授权管道全程处理：

| 现有组件 | 在 __global__ 下的行为 |
|---|---|
| `RbacProjectResolver` | 验证用户持有 `__global__` 下的 `RbacProjectGrant` |
| `RbacAuthorizationFilter` | 将 `/api/global/*` 路由映射到 `rbac.global.*` 权限码后做 Casbin 检查 |
| `IRbacManagementWriteService` | 接受任意 target project 参数，正常写入 MySQL + Outbox |
| Outbox Workers (Redis/ES/Casbin) | 消费事件时以 target project 为 scope，行为与业务 project 完全一致 |
| 审计管道 | 全局管理员的 `operatorUserid` 记录在每条 Outbox 事件中，无需新事件类型 |

---

## 2. 权限码与路由映射

四个保留权限码（定义于 `RbacGlobalConstants`）通过 `rbac_api_permission_map` 种子数据与路由绑定：

| 权限码 | 覆盖路由（GA2 实现）| action |
|---|---|---|
| `rbac.global.admin` | `GET api/global/project/list` | `access` |
| `rbac.global.user.manage` | `GET api/global/user/list` | `access` |
| `rbac.global.user.manage` | `PUT api/global/user/{userid}/status` | `write` |
| `rbac.global.group.manage` | `GET api/global/group/list` | `access` |
| `rbac.global.menu.manage` | `GET api/global/menu/list` | `access` |

---

## 3. Bootstrap 流程（鸡蛋问题解法）

初次部署时执行 `rbac-bootstrap-global.sql`，该脚本：

1. 在 `rbac_rule` 中创建 Global Console 菜单树（5 条规则，均属 `__global__`）。
2. 在 `rbac_group` 中创建 `global_admins` 权限组，持有全部 4 个 `rbac.global.*` 权限码。
3. 在 `rbac_api_permission_map` 中创建 5 条路由→权限码映射（`project = __global__`）。
4. 在 `rbac_administrator` 中创建第一个全局管理员账号。
5. 在 `rbac_project_grant` 中将其授权到 `__global__`，`is_super = true`（bootstrap 粗粒度）。

**bootstrap 完成后**，不再需要任何特殊工具。全局系统通过**现有接口**实现自管理：

- 新增全局管理员 → `POST /api/admin`（携带 `X-Project: __global__`）
- 将其加入细粒度权限组 → `POST /api/group/{groupCode}/members`（携带 `X-Project: __global__`）
- 降低 is_super 改用组权限 → `PUT /api/project-grant`（携带 `X-Project: __global__`）
- 撤销授权 → `DELETE /api/project-grant/{userid}`（携带 `X-Project: __global__`）

---

## 4. 项目隔离不变式

`RbacProjectGrant.IsSuper` 始终是 project 级别。

- `__global__` 下的 `IsSuper = true` 仅允许访问 `/api/global/*` 路由，**不赋予对任何业务 project 的运行时访问权**。
- 业务 project 管理员（如 `news` 的管理员）无 `__global__` 授权，无法调用任何全局 API。
- 全局管理员的跨 project 能力由 `rbac.global.*` 权限码显式授予，可独立撤销。

---

## 5. G005 排除规则（Compat-Blocker）

**规则**: 所有枚举跨 project 目标的操作必须排除 `__global__`，防止全局操作递归写入保留系统自身。

**执行位置**: GA2 的 `GlobalManagementService`（G008）。  
**执行方式**: 在枚举目标 project 列表时，对每个 project 调用 `RbacGlobalConstants.IsReservedProject(project)` 并跳过返回 `true` 的结果。

```csharp
// GA2 GlobalManagementService 中的目标解析逻辑（伪代码）
var targets = (await _grantRepo.GetDistinctProjectsAsync(ct))
    .Where(p => !RbacGlobalConstants.IsReservedProject(p))
    .ToList();
```

此规则在 GA2 合并前必须通过 Compat-Blocker 验证（G020）。

---

## 6. ES 全项目读取（A.8 #3 决议）

经代码审查确认（`RbacElasticQueryBuilder.Terms()` 跳过 null/空值），当 `query.Project = null` 时，ES 查询自动省略 project 过滤条件，实现全项目搜索。

**无需修改 ES 层**。GA2 的 Global*Controller 在跨项目搜索时只需将 `query.Project` 置为 `null`（而非 `"*"`）。

---

## 7. 非原子跨 project 写入（A.8 #4 决议）

跨 N 个 project 的操作由 `GlobalManagementService`（GA2）以 N 个独立事务执行。局部失败不回滚已成功的写入。设计约定：

- 操作结果以 `PerProjectResultReport` 格式返回，包含每个 project 的成功/失败状态。
- 失败的 project 可通过幂等重试（单独传入 target project）恢复。
- 与现有平台的最终一致性哲学一致，不引入分布式事务。

---

## 8. 审计粒度（A.8 #5 决议）

一次全局操作产生 N 条 per-project Outbox 事件，每条记录 `operatorUserid = 全局管理员 userid`。现阶段接受 per-project 粒度的审计记录，不新增 "一个全局动作 → N 个项目" 的合并记录。后续如有需求可通过现有 ES audit log 聚合查询实现。
