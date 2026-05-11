# Outbox 写入端闭环 — 计划报告

**日期**: 2026-05-10  
**结论**: 评估报告**无疑问，完全接受**。

---

## 一、核实结论

| 核查项 | 发现 |
|--------|------|
| `IOutboxWriter.Append` 调用点 | 全项目搜索为零，写入端闭环确实不存在 |
| 管理写操作入口 | `RbacManagementWriteGuard` 只做"读取 + 校验"，无写入逻辑；`Repository.SaveAsync` 只保存聚合根，无 Outbox 调用 |
| Payload 契约 | 6 类 Payload 全部已在 `RbacOutboxEvents.cs` 定义完整 |
| 消费侧 | `RbacRedisOutboxProcessor` / `RbacElasticsearchOutboxProcessor` / `RbacCasbinOutboxProcessor` 均已实现，等待事件投递 |
| 事务要求 | `OutboxReaderWriter.Append` 是 void，不单独 `SaveChanges`，设计已支持同一事务提交 |

**缺失的唯一环节**：**写入端** —— 业务变更发生时，没有任何代码调用 `IOutboxWriter.Append(evt)`。

---

## 二、驳回项

无。评估报告描述准确，约束合理，不需要驳回任何条款。

---

## 三、实现计划

### 3.1 新增两个文件

**文件 A**：`Rbac.Application/Management/IRbacManagementWriteService.cs`  
Application 层接口，定义 7 个方法签名，声明 `operatorUserid` 为必传参数（Payload 需要，不能省略）。

**文件 B**：`Rbac.Infrastructure.MySql/Management/RbacManagementWriteService.cs`  
Infrastructure 实现，持有 `RbacDbContext` + `IOutboxWriter`，每个方法内完成：
1. 聚合根写入（`Add` 或修改字段）  
2. `IOutboxWriter.Append(evt)`（不单独 SaveChanges）  
3. `await _db.SaveChangesAsync(ct)` — 业务实体 + Outbox 行同一事务提交

---

### 3.2 七个方法与 Outbox 事件映射

| 方法 | 涉及聚合根 | 产生事件 | Payload 类 |
|------|-----------|---------|-----------|
| `SaveAdministratorAsync` | `RbacAdministrator` | `UserChanged` | `UserChangedPayload` |
| `SaveGroupAsync` | `RbacGroup` | `GroupChanged` | `GroupChangedPayload` |
| `SaveRuleAsync` | `RbacRule` | `MenuChanged` | `MenuChangedPayload` |
| `SaveProjectGrantAsync` | `RbacProjectGrant` | `ProjectGrantChanged` | `ProjectGrantChangedPayload` |
| `SaveApiPermissionMapAsync` | `RbacApiPermissionMap` | `ApiMapChanged` | `ApiMapChangedPayload` |
| `SaveGroupMemberAsync` | `RbacGroupMember` | `PolicyChanged` + `GroupChanged` | `PolicyChangedPayload` + `GroupChangedPayload` |
| `DeleteGroupMemberAsync` | `RbacGroupMember` | `PolicyChanged` + `GroupChanged` | `PolicyChangedPayload` + `GroupChangedPayload` |

**说明**：
- `SaveGroupAsync` 当 permissionCodes 变更时**额外**追加一个 `PolicyChanged` 事件（因为 Casbin p policy 需要 reload）。
- `SaveGroupMemberAsync` / `DeleteGroupMemberAsync` 同时产生两个事件：`PolicyChanged`（触发 Casbin reload）+ `GroupChanged`（触发受影响用户的 permset 失效）。`GroupChangedPayload.AffectedUserids` 由调用方传入，不在服务内反查（保持调用方掌握上下文）。

---

### 3.3 关键约束落地

**同一事务**：`IOutboxWriter.Append` 仅追加到 `DbContext` 的变更跟踪，`SaveChangesAsync` 一次提交，不拆分。

**Payload 不猜字段**：方法签名要求调用方显式传入 `changedFields`、`affectedUserids`、`affectedGroupCodes` 等。服务不从聚合根反推，不读 Redis/ES。

**operatorUserid 必传**：接口方法强制要求，不可为 null，防止审计空洞。

**WriteGuard 先行**：编辑/删除场景调用方应先通过 `RbacManagementWriteGuard` 从 MySQL 加载聚合根，再传入写服务。写服务不重复加载，不重复校验 project。

**禁止笛卡尔积**：`SaveGroupMemberAsync` / `DeleteGroupMemberAsync` 直接操作 `RbacGroupMember`，不从 `rbac_project_grant × rbac_group` 推导。

---

### 3.4 需要同步注册到 DI

`IRbacManagementWriteService` → `RbacManagementWriteService`（Scoped），在 Api 和 Worker 的 `Program.cs` 中各加一行。

---

## 四、修改文件清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `Rbac.Application/Management/IRbacManagementWriteService.cs` | **新建** | 7 个方法的接口定义 |
| `Rbac.Infrastructure.MySql/Management/RbacManagementWriteService.cs` | **新建** | 实现：事务 + Outbox.Append + SaveChangesAsync |
| `Rbac.Api/Program.cs` | **追加 1 行** | 注册 `IRbacManagementWriteService` |
| `Rbac.Worker/Program.cs` | **追加 1 行** | 注册 `IRbacManagementWriteService` |

其余文件（`RbacOutboxEvents.cs`、`OutboxReaderWriter.cs`、三个 Processor、`RbacManagementWriteGuard`）**不修改**。

---

## 五、验收检查点

1. `rg "Append("` 在 `RbacManagementWriteService.cs` 中可见 ≥ 7 处调用。  
2. 每处 `Append` 后紧跟 `SaveChangesAsync`，无单独的独立 `SaveChanges`。  
3. `dotnet build Rbac.sln` 0 errors（允许已有 warnings）。  
4. 不出现 `rbac_project_grant × rbac_group` 查询。  
5. 不出现从 Redis/ES 读取后写入 Outbox 的代码路径。
