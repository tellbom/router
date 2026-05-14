# DxEId 移除改动边界文档

## 背景

`DxEId` 是为兼容旧前端数字 ID 而引入的雪花 ID 字符串字段。  
前端代码全新重构后，该字段失去存在意义。  
路由参数改用天然业务键：`userid`（管理员）、`groupCode`（权限组）、`ruleCode`（规则）。

---

## 改动原则

- **只删除** DxEId 相关代码，不重构其他逻辑
- **不改** ES 文档的 `_id`（仍用 `Guid.ToString()`，已有实测验证）
- **不改** `rbac_api_permission_map` 的路由（该表已用 `Guid id` 作路由参数，与 DxEId 无关）
- 每处改动标注「删除」或「替换」，不引入新抽象

---

## 一、彻底删除的文件（整文件删除）

| 文件 | 原因 |
|------|------|
| `Rbac.Application/Identity/IRbacDxEIdGenerator.cs` | DxEId 生成器接口，整体无用 |
| `Rbac.Application/Identity/RbacDxEIdImportPolicy.cs` | 旧数据迁移导入策略，整体无用 |
| `Rbac.Domain/Validation/RbacDxEIdUniquenessRules.cs` | DxEId 格式和唯一性验证，整体无用 |
| `Rbac.Infrastructure.MySql/Identity/RbacDxEIdGenerationOptions.cs` | 雪花 ID 生成器配置 + `SnowflakeDxEIdGenerator` 实现，整体无用 |

---

## 二、Domain 层

### `Rbac.Domain/ValueObjects/RbacValueObjects.cs`

- **删除** `DxEId` record 定义（约 15 行）
- 其余 ValueObject 不动

### `Rbac.Domain/Users/RbacAdministrator.cs`

- **删除** `public DxEId DxEId` 属性
- **删除** `Create(...)` 方法中的 `DxEId dxeId` 参数及赋值行
- 其余字段和方法不动

### `Rbac.Domain/Groups/RbacGroup.cs`

- **删除** `public DxEId DxEId` 属性
- **删除** `Create(...)` 方法中的 `DxEId dxeId` 参数及赋值行

### `Rbac.Domain/Rules/RbacRule.cs`

- **删除** `public DxEId DxEId` 属性
- **删除** `CreateMenu(...)` 和 `CreateButton(...)` 中的 `DxEId dxeId` 参数及赋值行

### `Rbac.Domain/Validation/RbacIdentityValidationRules.cs`

- **删除** `ValidateDxEId(...)` 方法及相关注释
- 其余验证方法不动

---

## 三、Application 层

### `Rbac.Application/Management/RbacManagementWriteGuard.cs`

- **删除** `LoadAdminByDxEIdAsync` 方法
- **删除** `LoadGroupByDxEIdAsync` 方法
- **删除** `LoadRuleByDxEIdAsync` 方法
- **新增**（替换）：
  - `LoadAdminByUseridAsync(string userid)`
  - `LoadGroupByCodeAsync(string groupCode, string project)`
  - `LoadRuleByCodeAsync(string ruleCode, string project)`
- 删除 `IRbacDxEIdGenerator` 的注入和使用（WriteGuard 本身不生成 ID，但若有引用一并移除）

### `Rbac.Application/Repositories/RbacRepositoryContracts.cs`

- `IAdministratorRepository`：**删除** `FindByDxEIdAsync`
- `IGroupRepository`：**删除** `FindByDxEIdAsync`
- `IRuleRepository`：**删除** `FindByDxEIdAsync`

### `Rbac.Application/Contracts/Menus/RbacMenuDtos.cs`

- `MenuNodeDto`：**删除** `DxEId` 字段和注释中对 DxEId 的说明

### `Rbac.Application/Contracts/Compatibility/BackendIndexDtos.cs`

- **删除** 所有 DTO 中的 `DxEId` 字段

### `Rbac.Application/Contracts/Compatibility/FrontendCompatibilityContracts.cs`

- **删除** 所有 DTO 中的 `DxEId` 字段

### `Rbac.Application/Mapping/RbacCompatibilityMappers.cs`

- **删除** 所有 `DxEId = xxx.DxEId.Value` 映射行及相关注释

### `Rbac.Application/Menus/RbacMenuBuilder.cs`

- **删除** `DxEId = node.DxEId` 赋值行

### `Rbac.Application/Backend/RbacBackendIndexService.cs`

- **删除** `DxEId = admin.DxEId.Value` 及 `DxEId = string.Empty` 赋值行

### `Rbac.Application/Outbox/RbacOutboxEvents.cs`

- `MenuChangedPayload`：**删除** `DxEId` 字段及注释中的说明

### `Rbac.Application/Search/IRbacManagementSearchService.cs`（含 RbacSearchQueries.cs）

- `UserSearchResult`：**删除** `DxEId` 字段
- `GroupSearchResult`：**删除** `DxEId` 字段
- `RuleSearchResult`：**删除** `DxEId` 字段

### `Rbac.Application/Serialization/RbacSerializationRules.cs`

- **删除** `LongToStringConverter` 及 `NullableLongToStringConverter`（专为 DxEId long→string 而设）
- **删除** 相关注释

---

## 四、Infrastructure.MySql 层

### `Rbac.Infrastructure.MySql/Mapping/RbacEntityMappings.cs`

- `AdministratorMapping`：**删除** `dxe_id` 列配置和 `ux_admin_dxe_id` 索引配置
- `GroupMapping`：**删除** `dxe_id` 列配置和 `ux_group_dxe_id` 索引配置
- `RuleMapping`：**删除** `dxe_id` 列配置和 `ux_rule_dxe_id` 索引配置

### `Rbac.Infrastructure.MySql/Repositories/RbacRepositories.cs`

- `AdministratorRepository`：**删除** `FindByDxEIdAsync` 方法
- `GroupRepository`：**删除** `FindByDxEIdAsync` 方法
- `RuleRepository`：**删除** `FindByDxEIdAsync` 方法

### `Rbac.Infrastructure.MySql/Management/RbacManagementWriteService.cs`

- 所有 `Create(...)` 调用处：**删除** `new DxEId(_idGen.Generate()),` 参数
- **删除** `IOutboxWriter _outbox` 以外注入的 `IRbacDxEIdGenerator _idGen` 字段和构造参数

---

## 五、Infrastructure.Elasticsearch 层

### `Rbac.Infrastructure.Elasticsearch/Documents/RbacEsDocuments.cs`

- `UserDocument`：**删除** `DxEId` 属性（`[Keyword(Name = "dxe_id")]`）
- `GroupDocument`：**删除** `DxEId` 属性
- `RuleDocument`：**删除** `DxEId` 属性

### `Rbac.Infrastructure.Elasticsearch/Indexes/RbacUserIndexMapping.cs`

- **删除** `dxe_id` 字段映射及 `allText` copy_to 中对 DxEId 的引用

### `Rbac.Infrastructure.Elasticsearch/Indexes/RbacGroupIndexMapping.cs`

- **删除** `dxe_id` 字段映射

### `Rbac.Infrastructure.Elasticsearch/Indexes/RbacRuleIndexMapping.cs`

- **删除** `dxe_id` 字段映射

### `Rbac.Infrastructure.Elasticsearch/Reindex/RbacEsFullReindexService.cs`

- `ReindexUsersAsync`：**删除** `DxEId = a.DxEId.Value` 赋值行
- `ReindexGroupsAsync`：**删除** `DxEId = g.DxEId.Value` 赋值行
- `ReindexRulesAsync`：**删除** `DxEId = r.DxEId.Value` 赋值行

### `Rbac.Infrastructure.Elasticsearch/Services/RbacManagementSearchService.cs`

- 查询结果映射处：**删除** `DxEId` 字段赋值行

### `Rbac.Worker/Outbox/RbacElasticsearchOutboxProcessor.cs`

- 三处文档构造（User/Group/Rule）：**删除** `DxEId = xxx.DxEId.Value` 赋值行
- 注释中对 DxEId 的说明一并删除

---

## 六、API 层（Controllers + Program.cs）

### `Rbac.Api/Controllers/AdminController.cs`

- 路由 `{dxeId}` **替换为** `{userid}`
- `_guard.LoadAdminByDxEIdAsync(dxeId)` **替换为** `_guard.LoadAdminByUseridAsync(userid)`
- `Create` 方法响应体**删除** `new { dxeId = admin.DxEId.Value }` 改为 `new { userid = admin.Userid.Value }`
- **删除** `IRbacDxEIdGenerator _idGen` 字段和构造注入

### `Rbac.Api/Controllers/GroupController.cs`

- 路由 `{dxeId}` **替换为** `{groupCode}`
- `_guard.LoadGroupByDxEIdAsync(dxeId, ...)` **替换为** `_guard.LoadGroupByCodeAsync(groupCode, ...)`
- `Create` 方法响应体**删除** `new { dxeId = group.DxEId.Value }` 改为 `new { groupCode = group.GroupCode.Value }`
- **删除** `IRbacDxEIdGenerator _idGen` 字段和构造注入
- `RbacGroup.Create(...)` 调用处删除 `DxEId` 参数

### `Rbac.Api/Controllers/RuleController.cs`

- 路由 `{dxeId}` **替换为** `{ruleCode}`
- `_guard.LoadRuleByDxEIdAsync(dxeId, ...)` **替换为** `_guard.LoadRuleByCodeAsync(ruleCode, ...)`
- `Create` 方法响应体**删除** `new { dxeId = rule.DxEId.Value }` 改为 `new { ruleCode = rule.RuleCode.Value }`
- **删除** `IRbacDxEIdGenerator _idGen` 字段和构造注入

### `Rbac.Api/Controllers/ControllersAdditions.cs`（partial class 补丁）

- 所有 `{dxeId}` 路由参数同上，按实体类型替换为对应天然键
- `_guard.LoadXxxByDxEIdAsync` 调用全部替换

### `Rbac.Api/Controllers/ApiMapController.cs`

- **不动**，该 Controller 用 `{id:guid}` 与 DxEId 无关

### `Rbac.Api/Program.cs`

- **删除** `Configure<RbacDxEIdGenerationOptions>(...)` 注册行
- **删除** `AddSingleton<IRbacDxEIdGenerator>(...)` 注册块（约 4 行）
- **删除** `using Rbac.Infrastructure.MySql.Identity` 中与 DxEId 相关的引用（若 Identity 命名空间仅包含 DxEId 相关类则整行删除）

### `Rbac.Worker/Program.cs`

- 同 Api Program.cs，**删除** `Configure<RbacDxEIdGenerationOptions>` 和相关 using

---

## 七、SQL 脚本

### `rbac-init.sql` 和 `rbac-bootstrap.sql`

- `rbac_administrator`：**删除** `dxe_id` 列定义和 `UNIQUE KEY ux_admin_dxe_id`
- `rbac_group`：**删除** `dxe_id` 列定义和 `UNIQUE KEY ux_group_dxe_id`
- `rbac_rule`：**删除** `dxe_id` 列定义和 `UNIQUE KEY ux_rule_dxe_id`
- `rbac-bootstrap.sql` 中 INSERT 语句**删除** `dxe_id` 和 `CONCAT('bootstrap_', ...)` 值

> **注意**：如果数据库已初始化，需要额外提供 ALTER TABLE 迁移 SQL：
> ```sql
> ALTER TABLE rbac_administrator DROP INDEX ux_admin_dxe_id, DROP COLUMN dxe_id;
> ALTER TABLE rbac_group         DROP INDEX ux_group_dxe_id, DROP COLUMN dxe_id;
> ALTER TABLE rbac_rule          DROP INDEX ux_rule_dxe_id,  DROP COLUMN dxe_id;
> ```

---

## 八、appsettings.json

- **删除** `"DxEId"` 配置节（`WorkerId`、`DatacenterId` 等）

---

## 关键约束（执行 agent 必须遵守）

1. **ES 文档 `_id` 不变**：仍使用 `Guid.ToString()`，`DeleteDocumentAsync` 和 `IndexDocumentAsync` 的 id 参数不改
2. **`rbac_api_permission_map` 的 `Guid id` 路由不改**
3. **`Guid Id`（内部主键）不改**：只删 DxEId，Guid 主键保留
4. **不改鉴权链路**：permissionCode / Casbin / Redis permset 全部不动
5. **逐层编译验证**：建议按 Domain → Application → Infrastructure → Api/Worker 顺序改，每层改完确认编译通过再进行下一层

---

## 改动量估算

| 层 | 文件数 | 性质 |
|----|--------|------|
| 彻底删除文件 | 4 个 | 整文件删除 |
| Domain | 5 个 | 各删 1-3 行 |
| Application | 10 个 | 各删 1-5 行 |
| Infrastructure.MySql | 3 个 | 各删 3-8 行 |
| Infrastructure.Elasticsearch | 6 个 | 各删 1-3 行 |
| Api Controllers | 4 个 | 路由参数替换 + 删字段 |
| Program.cs × 2 | 2 个 | 各删 4-6 行 |
| SQL | 2 个 | 各删 3 列定义 |

**总体评估**：改动分散但逻辑清晰，每处改动独立，无跨层副作用，风险低。
