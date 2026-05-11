以下为《ASP.NET Core RBAC 重构实施计划.md》内容。本次按你的限制未写入文件、未修改代码。

# ASP.NET Core RBAC 重构实施计划

## 1. 重构目标

- 在兼容现有内网后台管理前端的前提下，重构 RBAC 权限体系。
- 使用 ASP.NET Core 自研 RBAC，不继续使用 ABP，不完全复刻 PHP。
- 登录统一使用 JWT，可接入公司门户 JWT 或 Keycloak JWT。
- 提升权限加载速度，减少首页权限初始化冗余。
- 建立清晰的 `project` 多系统权限域。
- 内部主键使用 `Guid`，对外保留 `DxE_id` 作为历史兼容业务 ID。
- 权限判断逐步从 `DxE_id` 迁移到稳定的 `permissionCode` / `ruleCode`。
- 为后续 Casbin 接入预留授权判断边界，但本期不接入 Casbin。

## 2. 非目标

- 不重构会员 RBAC。
- 不兼容 PHP `batoken`。
- 不保留 PHP `refreshToken` 机制。
- 不继续暴露 `siteConfig`、`terminal` 作为 RBAC 初始化依赖。
- 不返回 `allmenu` 全量菜单。
- 不生成数据库迁移代码。
- 不设计 Casbin 具体策略表。
- 不改变现有内网前端的真实使用习惯。

## 3. 兼容字段最终合同

以人工确认的内网差异为准，最终兼容合同如下：

| 类别 | 必须保留字段 |
|---|---|
| 通用响应 | `code`、`msg`、`data`、`time` |
| 分页列表 | `data.list`、`data.total`、必要时 `data.remark` |
| 远程下拉 | `data.options` 或兼容 `data.list` |
| 登录返回 | `userInfo`、`routePath` |
| 用户信息 | `userid`、`username`、`avatar`、`lastlogintime`、`token`、`super` |
| 管理员 | `DxE_id`、`userid`、`username`、`group_arr`、`group_name_arr`、`status` |
| 权限组 | `DxE_id`、`pid`、`name`、`rules`、`status`、`children` |
| 菜单规则 | `DxE_id`、`pid`、`title`、`name`、`path`、`type`、`menu_type`、`url`、`component`、`extend`、`keepalive`、`weigh`、`status`、`children` |
| 多系统域 | `project` |
| 权限码 | `permissionCode`、`ruleCode`，新增稳定字段 |

兼容期可临时返回 `id = DxE_id`，但目标前端合同应以大小写明确的 `DxE_id` 为准。

## 4. Guid / DxE_id / permissionCode 的关系

| 字段 | 定位 | 是否对前端暴露 |
|---|---|---|
| `Id: Guid` | 数据库内部主键 | 不作为旧前端主键暴露 |
| `DxE_id` | 历史兼容业务 ID，用于表格、树、编辑、删除、排序 | 必须暴露 |
| `permissionCode` | 稳定权限判断码 | 建议暴露或至少后端内部使用 |
| `ruleCode` | 菜单 / 按钮规则稳定编码 | 建议保留 |

原则：

- `Guid` 负责内部数据一致性。
- `DxE_id` 负责前端兼容。
- `permissionCode` / `ruleCode` 负责长期权限判断。
- `DxE_id` 若使用雪花 long，JSON 建议以字符串返回，避免 JavaScript 大整数精度丢失。
- 服务层通过 `project + DxE_id` 定位内部 `Guid`。

## 5. project 权限域设计原则

- `project` 是多系统权限域，不是普通筛选条件。
- 前端可以携带 `project`，但后端不能直接信任。
- 后端必须从 JWT claims、应用授权关系、服务端配置中校验当前用户是否允许访问该 `project`。
- 所有 RBAC 查询必须带 `project`：管理员、权限组、菜单、按钮、权限快照。
- `super` 也必须受 `project` 域约束，避免全局超管越权。
- Redis 权限缓存 key 必须包含 `project`。
- 审计日志记录 `requestedProject` 与后端确认后的 `resolvedProject`。

## 6. 登录与 JWT 边界

- 统一使用 `Authorization: Bearer <JWT>`。
- JWT 来源可以是公司门户或 Keycloak。
- 后端从 JWT 中解析用户唯一标识，不信任前端传入的用户 ID。
- `userInfo.token` 可继续返回 JWT 字符串，用于兼容前端存储习惯。
- 不再返回 `refreshToken`。
- 不再兼容 PHP `batoken`。
- `userid` 作为业务用户标识，JWT `sub` 作为稳定身份标识。

## 7. menus / auth() / v-auth 兼容方案

前端权限链路保持不变：

1. 登录后获取 `userInfo` 和 `routePath`。
2. 后台初始化接口返回当前 `project` 下的 `menus`。
3. 前端根据 `menus` 构建动态路由。
4. 按钮权限节点进入 `authNode`。
5. `auth('add')`、`v-auth="'edit'"` 按当前路由 path 拼接按钮名判断。

必须保留菜单字段：

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

按钮权限建议继续使用：

- `add`
- `edit`
- `del`
- `sortable`

后端不能只返回可见菜单，还要返回当前页面下的按钮权限节点。

## 8. Redis 权限快照方案

建议引入用户权限快照，减少每次首页加载时重复计算菜单和按钮权限。

推荐 key：

```text
rbac:snapshot:{project}:{userGuid}:{version}
```

快照内容：

| 字段 | 用途 |
|---|---|
| `project` | 当前权限域 |
| `userGuid` / `userid` | 用户标识 |
| `groups` | 用户所属权限组 |
| `super` | 当前 project 下是否超管 |
| `menus` | 前端菜单树 |
| `buttonNodes` | auth / v-auth 使用 |
| `permissionCodes` | 后端接口鉴权 |
| `ruleCodes` | 规则稳定标识 |
| `DxE_ids` | 兼容排查 |
| `version` | 权限版本 |

失效条件：

- 用户权限组变更。
- 权限组 rules 变更。
- 菜单 / 按钮变更。
- project 授权变更。
- 用户禁用 / 启用。
- super 状态变更。

## 9. 接口鉴权方案

- 前端 `auth()` / `v-auth` 只负责显示控制，不作为安全边界。
- ASP.NET Core 每个 RBAC 写接口必须进行服务端鉴权。
- 推荐统一封装 `IRbacPermissionChecker`。
- 鉴权输入至少包含：`userGuid`、`project`、`permissionCode`、HTTP action。
- 查询类接口也应按 project 和数据范围过滤。
- 管理员、权限组、菜单、排序、删除接口均不得只依赖前端按钮隐藏。

## 10. 数据模型设计原则

本计划不生成迁移代码，仅定义原则：

- 所有核心表内部主键使用 `Guid`。
- 所有对前端兼容的实体保留 `DxE_id`。
- 菜单和按钮规则增加稳定 `permissionCode` / `ruleCode`。
- 所有 RBAC 表必须包含 `Project` 或可通过关联明确归属 project。
- `DxE_id` 在同一 project 内必须唯一。
- 权限组 rules 长期不建议只存数字 ID，应逐步迁移到 ruleCode / permissionCode。
- 用户与权限组关系必须按 project 隔离。
- 审计字段保留创建、更新、禁用状态。

## 11. 旧字段废弃清单

| 字段 / 能力 | 处理 |
|---|---|
| `nickname` | 内网目标改为 `userid`，迁移期可临时兼容 |
| `refreshToken` | JWT 统一后废弃 |
| `siteConfig` | 不进入新 RBAC 初始化合同 |
| `terminal` | 不进入新 RBAC 边界 |
| `allmenu` | 废弃，避免全量权限暴露 |
| `supre` | 废弃，统一为 `super` |
| PHP `batoken` | 废弃 |
| 自增 int 主键暴露 | 由 `DxE_id` 替代 |
| 纯数字规则 ID 鉴权 | 逐步迁移到 `permissionCode` |

## 12. 安全风险修复清单

- 修复前端传 `project` 可伪造导致越权的问题。
- 修复全量菜单 `allmenu` 暴露风险。
- 修复仅靠前端按钮隐藏控制权限的问题。
- 修复 `super` 不受 project 限制的问题。
- 修复权限缓存未按 project 隔离的问题。
- 修复雪花 long 作为 JSON number 导致前端精度丢失的问题。
- 修复规则绑定不稳定数字 ID 导致跨环境迁移失效的问题。
- 修复 RBAC 初始化接口混入非权限字段造成的冗余。
- 修复删除、排序、编辑接口缺少服务端权限校验的风险。

## 13. 分阶段实施计划

| 阶段 | 内容 |
|---|---|
| 阶段 0：冻结兼容合同 | 确认真实内网前端字段，固化 `DxE_id`、`userid`、`project`、`menus` 合同 |
| 阶段 1：搭建 ASP.NET Core RBAC 边界 | 建立 JWT、project 解析、统一响应、兼容 DTO、鉴权接口抽象 |
| 阶段 2：实现核心 RBAC 模型 | 管理员、权限组、菜单规则、用户组关系、Guid 与 DxE_id 映射 |
| 阶段 3：实现菜单与按钮权限 | 输出兼容 `menus`，生成前端可识别的按钮节点 |
| 阶段 4：实现接口鉴权 | 所有 RBAC 管理接口接入服务端权限判断 |
| 阶段 5：接入 Redis 权限快照 | 缓存 menus、buttonNodes、permissionCodes，提高加载速度 |
| 阶段 6：灰度迁移 | 与内网前端联调，保留必要兼容别名，逐步切换流量 |
| 阶段 7：废弃旧字段 | 移除 refreshToken、siteConfig、terminal、allmenu、supre 等非目标字段 |

## 14. 每阶段验收标准

| 阶段 | 验收标准 |
|---|---|
| 阶段 0 | 字段合同经前端和后端确认，无 `id/DxE_id`、`nickname/userid` 歧义 |
| 阶段 1 | JWT 登录可用，project 校验可用，统一响应结构稳定 |
| 阶段 2 | Guid 内部主键与 DxE_id 对外 ID 映射正确 |
| 阶段 3 | 菜单树正常渲染，`auth()` / `v-auth` 按钮权限表现一致 |
| 阶段 4 | 无权限用户即使直接调用接口也被拒绝 |
| 阶段 5 | 权限快照命中后首页权限加载明显减少数据库查询 |
| 阶段 6 | 现有内网前端不改或少改即可运行核心 RBAC 流程 |
| 阶段 7 | 旧字段移除后无前端残留依赖和运行错误 |

## 15. 后续 Casbin 接入预留点

本期不接入 Casbin，只预留边界：

- 所有权限判断通过 `IRbacPermissionChecker`。
- 当前实现可先用数据库 RBAC + Redis 快照。
- 后续 Casbin 接入时替换权限判断实现，不改变前端 DTO。
- Casbin domain 可映射为 `project`。
- subject 可映射为用户、权限组或角色。
- object 建议映射为 `permissionCode`。
- action 可映射为 `read`、`create`、`update`、`delete`、`execute`。
- 前端仍只感知 `menus`、`auth()`、`v-auth`，不直接感知 Casbin。