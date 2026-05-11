# 内网版 RBAC 前端兼容与重构边界分析报告

## 1. 当前结论摘要

本报告基于当前工作区源码核实，包括 `rbac-frontend-compatibility-contract.md`、`web/` 前端源码，以及当前 PHP 后端 RBAC 相关代码。需要特别说明：当前工作区源码未体现用户描述的内网版改造结果，仍更接近原始 BuildAdmin 前后端实现。

当前源码核实结论如下：

- 未发现 `DxE_id`、`DXE_id`、`dxe_id` 在 `web/src`、`app/admin`、`app/common`、`extend/ba` 中的实际引用；当前源码仍大量依赖 `id`。
- 未发现后台 RBAC 前端请求携带 `project` 的实现；当前后端 RBAC 权限判断也未按 `project` 隔离。
- 后台管理员信息仍依赖 `nickname`，未完成 `userid` 替代。
- `refreshToken` 仍在前端 store、axios 刷新逻辑、登录/退出接口、后端 Auth 中存在。
- `siteConfig` 仍在后台布局、站点配置、模块安装等流程中存在。
- `terminal` 终端相关 store、组件、API、布局入口仍有残留。
- 未发现 `allmenu` / `allMenu` 的实际依赖。
- 当前实际使用字段为 `super`，未发现 `supre`。
- `auth()` / `v-auth` 的真实权限判断依赖菜单规则节点生成的 `authNode`，判断格式为：当前路由 path + `/` + 按钮权限名。
- 菜单树构建依赖 `menus` 返回的 `type`、`menu_type`、`path`、`name`、`component`、`url`、`extend`、`keepalive`、`children` 等字段。

因此，当前 `rbac-frontend-compatibility-contract.md` 不能直接作为内网最终版本的唯一依据。它对当前工作区源码仍然基本成立，但与用户描述的内网目标状态存在明显差异。后续 ASP.NET Core RBAC 重构应以“当前真实内网前端源码”为最终兼容合同；如果当前工作区不是内网最终分支，需要在内网实际分支重新执行同样核实。

## 2. 与 rbac-frontend-compatibility-contract.md 的差异修正

`rbac-frontend-compatibility-contract.md` 的主要结论仍符合当前工作区源码，但不符合用户描述的内网目标状态。差异修正如下：

| 差异项 | 用户描述的内网状态 | 当前工作区源码核实 | 修正结论 |
|---|---|---|---|
| `nickname` | 已改为 `userid` | 后台管理员仍使用 `nickname` | 当前源码未完成替换 |
| `refreshToken` | 不再使用 | 前端和后端均仍存在 | 当前源码仍依赖 |
| `siteConfig` | 不再使用 | 后台布局和配置仍使用 | 当前源码仍依赖 |
| `terminal` | 已移除 | 前端 store、组件、API、布局入口仍存在 | 当前源码仍有残留 |
| `id` -> `DxE_id` | 表格、树、编辑、删除、排序已替换 | 未发现 `DxE_id`，仍使用 `id` | 当前源码未完成替换 |
| `project` | 前端请求携带，后端用于多系统隔离 | 未发现前端/后端 RBAC 使用 | 当前源码未体现 |
| `allmenu` | 可能废弃 | 当前源码未发现依赖 | 可以作为废弃候选 |
| `super/supre` | 统一为 `super` | 当前源码只有 `super` | 建议统一保留 `super` |
| `DXE_id` 作为兼容 ID | 计划改为雪花 long | 当前源码无该字段 | 方案可行，但需前端改造或兼容别名 |

结论：当前工作区不能证明“内网版已经完全完成字段替换”。ASP.NET Core 重构前，应以实际部署中的内网前端分支再次核实，避免用目标描述覆盖源码事实。

## 3. 当前前端真实依赖字段

### 3.1 通用响应结构

当前前端请求层真实依赖以下通用字段：

| 字段 | 用途 | 兼容要求 |
|---|---|---|
| `code` | 判断业务状态码 | 必须兼容 |
| `msg` | 错误提示 / 成功提示 | 必须兼容 |
| `data` | 业务数据载体 | 必须兼容 |
| `time` | 响应时间戳 | 建议兼容 |

表格类接口依赖：

| 字段 | 用途 | 兼容要求 |
|---|---|---|
| `data.list` | 表格数据列表 | 必须兼容 |
| `data.total` | 分页总数 | 必须兼容 |
| `data.remark` | 表格备注 / 提示 | 建议兼容 |
| `data.options` | 远程下拉选项 | 远程选择组件使用时必须兼容 |

### 3.2 登录与后台首页

当前前端登录和后台初始化依赖：

| 字段 | 当前用途 | 兼容要求 |
|---|---|---|
| `userInfo` | 登录后写入管理员信息 store | 必须兼容，或在 JWT 方案中提供等价结构 |
| `routePath` | 登录后跳转路径 | 当前前端依赖 |
| `adminInfo` | 后台首页初始化管理员信息 | 必须兼容 |
| `menus` | 后台菜单树和按钮权限来源 | 必须兼容 |
| `siteConfig` | 当前源码后台布局仍使用 | 当前源码必须兼容；目标内网版若已移除可废弃 |
| `terminal` | 当前源码终端入口仍使用 | 当前源码必须兼容；目标内网版若已移除可废弃 |

### 3.3 管理员信息字段

当前 `adminInfo` store 真实字段：

| 字段 | 当前用途 | 兼容要求 |
|---|---|---|
| `id` | 当前管理员主键、判断是否本人、接口参数 | 当前源码必须兼容 |
| `username` | 登录账号展示 | 必须兼容 |
| `nickname` | 昵称展示、管理员表单必填 | 当前源码必须兼容 |
| `avatar` | 头像展示 | 建议兼容 |
| `lastlogintime` | 后台展示 | 建议兼容 |
| `token` | 请求认证令牌 | JWT 重构后应兼容字段名或改造请求层 |
| `refreshToken` | 当前刷新 token 逻辑 | 当前源码仍依赖；目标可移除 |
| `super` | 超级管理员 UI 判断 | 必须统一为 `super` |

### 3.4 管理员列表 / 编辑

当前后台管理员页面依赖：

| 字段 | 用途 | 兼容要求 |
|---|---|---|
| `id` | 表格主键、编辑、删除、本人判断 | 当前源码必须兼容 |
| `username` | 表格和表单 | 必须兼容 |
| `nickname` | 表格和表单 | 当前源码必须兼容 |
| `group_arr` | 编辑表单权限组选择值 | 必须兼容 |
| `group_name_arr` | 列表展示所属权限组 | 必须兼容 |
| `avatar` | 头像 | 建议兼容 |
| `email` | 表格 / 表单 | 建议兼容 |
| `mobile` | 表格 / 表单 | 建议兼容 |
| `motto` | 表单 | 可兼容 |
| `password` | 新增 / 修改 | 必须按现有语义兼容 |
| `status` | 状态开关 | 必须兼容 |
| `lastlogintime` | 表格展示 | 建议兼容 |
| `createtime` | 表格展示 | 建议兼容 |

### 3.5 权限组

当前权限组页面依赖：

| 字段 | 用途 | 兼容要求 |
|---|---|---|
| `id` | 权限组主键、树节点、禁止编辑自身组 | 当前源码必须兼容 |
| `pid` | 父级权限组 | 必须兼容 |
| `name` | 权限组名称、远程下拉显示 | 必须兼容 |
| `rules` | 权限规则 ID 集合，支持 `*` | 必须兼容 |
| `status` | 状态 | 必须兼容 |
| `createtime` | 展示 | 建议兼容 |
| `updatetime` | 展示 | 建议兼容 |
| `group` | `admin/group/index` 额外返回的当前管理员组 | 当前页面依赖 |

### 3.6 菜单 / 规则

当前菜单规则页面和路由构建依赖：

| 字段 | 用途 | 兼容要求 |
|---|---|---|
| `id` | 菜单规则主键、树节点 key、排序、权限组 rules | 当前源码必须兼容 |
| `pid` | 父级菜单 / 规则 | 必须兼容 |
| `title` | 菜单标题、树展示 | 必须兼容 |
| `name` | 路由 name、按钮权限节点名 | 必须兼容 |
| `path` | 路由 path、authNode key | 必须兼容 |
| `type` | `menu_dir`、`menu`、`button` 等类型 | 必须兼容 |
| `menu_type` | `tab`、`link`、`iframe` | 必须兼容 |
| `url` | 外链 / iframe 地址 | 对外链菜单必须兼容 |
| `component` | 前端组件路径 | tab 菜单必须兼容 |
| `extend` | `none`、`add_menu_only` 等扩展行为 | 必须兼容 |
| `icon` | 菜单图标 | 建议兼容 |
| `keepalive` | 路由缓存 | 必须兼容 |
| `weigh` | 排序 | 必须兼容 |
| `status` | 启停 | 必须兼容 |
| `remark` | 备注 | 可兼容 |
| `children` | 菜单树 / 规则树 | 必须兼容 |

## 4. 必须兼容字段清单

按当前工作区源码，以下字段属于前端兼容关键字段：

| 类别 | 必须兼容字段 |
|---|---|
| 通用响应 | `code`、`msg`、`data`、`data.list`、`data.total` |
| 登录 / 初始化 | `userInfo`、`routePath`、`adminInfo`、`menus` |
| 管理员 | `id`、`username`、`nickname`、`group_arr`、`group_name_arr`、`status`、`token`、`super` |
| 权限组 | `id`、`pid`、`name`、`rules`、`status`、`group` |
| 菜单规则 | `id`、`pid`、`title`、`name`、`path`、`type`、`menu_type`、`url`、`component`、`extend`、`keepalive`、`weigh`、`status`、`children` |
| 按钮权限 | `path`、`name`、父子关系、按钮节点类型 |
| 表格 / 树通用主键 | 当前源码为 `id`；目标内网版若已改造，应统一为 `DxE_id` |

如果以用户描述的内网目标作为重构合同，则建议必须兼容字段调整为：

| 类别 | 目标兼容字段 |
|---|---|
| 对外业务兼容 ID | `DxE_id` |
| 登录用户展示 / 识别 | `userid`、`username` |
| 多系统隔离 | `project` |
| 超级管理员 | `super` |
| 菜单 / 按钮 | `menus`、`name`、`path`、`type`、`menu_type`、`extend`、`keepalive`、`children` |

但该目标清单需要以内网真实分支代码再次确认。

## 5. 可废弃字段清单

| 字段 / 能力 | 当前工作区状态 | 废弃建议 |
|---|---|---|
| `allmenu` / `allMenu` | 未发现真实依赖 | 可废弃；不建议向普通管理员返回全量菜单 |
| `supre` | 未发现真实依赖 | 废弃，统一为 `super` |
| `refreshToken` | 当前源码仍依赖 | JWT 方案下可废弃，但需先移除前端 axios 刷新逻辑和后端 refresh 接口依赖 |
| `siteConfig` | 当前源码仍依赖 | 内网版若确认已移除，可废弃；当前工作区不能直接移除 |
| `terminal` | 当前源码仍有残留 | 内网版若确认已移除，可废弃；当前工作区不能直接移除 |
| `nickname` | 当前源码仍依赖 | 目标可改为 `userid`，但当前工作区不能直接废弃 |
| `id` 对外主键 | 当前源码强依赖 | 目标可由 `DxE_id` 替代，但需完整前端改造或提供兼容别名 |

## 6. DxE_id 使用范围分析

### 6.1 当前源码核实结果

当前工作区未发现 `DxE_id`、`DXE_id`、`dxe_id` 的真实引用。因此不能得出“当前内网版已经完全用 `DxE_id` 替代 `id`”的结论。

当前源码中仍依赖 `id` 的关键位置包括：

- `baTable` 默认主键 `table.pk = 'id'`。
- 表格编辑、删除、字段快速编辑、拖拽排序均通过 `table.pk` 取值，默认仍为 `id`。
- 远程下拉组件默认 `pk = 'id'`。
- 权限组规则树使用 `node-key="id"`。
- 菜单父级选择、权限组父级选择仍使用 `id`。
- 管理员列表使用 `row.id` 判断是否当前用户。
- 权限组页面使用 `row.id` 判断是否当前管理员所属组。
- 后端 Auth 规则加载、角色规则、菜单树均基于 `id` / `pid`。

### 6.2 是否所有表格、树、下拉、编辑、删除、排序都已依赖 DxE_id

以当前工作区源码为准：否。

当前默认机制仍是 `id`。若内网版目标要求全部改为 `DxE_id`，至少需要确认以下位置已经完成改造：

| 场景 | 当前默认字段 | 目标字段 |
|---|---|---|
| 表格主键 | `id` | `DxE_id` |
| 编辑参数 | `id` | `DxE_id` |
| 删除参数 | `id` | `DxE_id` |
| 快速编辑 | `id` | `DxE_id` |
| 拖拽排序 | `id` | `DxE_id` |
| 树节点 key | `id` | `DxE_id` |
| 远程下拉值 | `id` | `DxE_id` |
| 权限组 rules | 菜单规则 `id` | 菜单规则 `DxE_id` 或稳定 `permissionCode` |

### 6.3 Guid + DxE_id(long) 是否可行

可行，但必须分清内部主键和外部兼容 ID：

| 层次 | 建议 |
|---|---|
| 数据库内部主键 | 使用 `Guid`，仅用于内部关联和数据一致性 |
| 对外兼容字段 | 返回 `DxE_id`，用于前端表格、树、编辑、删除、排序兼容 |
| `DxE_id` 类型 | 可使用雪花 long，但建议 JSON 中以字符串返回 |
| 权限判断长期方向 | 从 `DxE_id` 逐步迁移到稳定的 `permissionCode` / `ruleCode` |

注意：JavaScript `Number` 对超过 `2^53 - 1` 的整数存在精度风险。雪花 long ID 很容易超过安全整数范围。因此 `DxE_id` 即使后端是 long，对前端也建议序列化为字符串。否则树节点、下拉值、删除 ID、排序 ID 可能出现精度丢失。

### 6.4 是否可以不再把 DxE_id 作为内部数据库主键

可以，而且建议这样做。`DxE_id` 应作为历史兼容业务 ID，不应继续绑死为数据库主键。ASP.NET Core 后端可以：

- 内部使用 `Guid Id`。
- 对外返回 `DxE_id`。
- 请求入参接受 `DxE_id`。
- 服务层通过 `Project + DxE_id` 映射到内部 `Guid`。
- 新权限判断逐步使用 `permissionCode` / `ruleCode`。

## 7. userid / nickname 差异分析

### 7.1 当前源码核实结果

当前后台管理员 RBAC 仍使用 `nickname`，未发现后台管理员侧 `userid` 已完全替代 `nickname`。

`nickname` 当前用途包括：

- 管理员 store 字段。
- 顶部用户菜单展示。
- Dashboard 展示。
- 管理员列表列字段。
- 管理员新增 / 编辑表单字段。
- 管理员信息页面字段。
- 后端 Auth 允许返回字段。
- 后端管理员验证器。
- 数据库管理员表字段。

`userid` / `user_id` 在当前源码中主要出现在会员相关或业务日志中，不属于后台管理员 RBAC 主链路。

### 7.2 重构建议

如果内网目标确认为“不再使用 nickname，统一使用 userid”，ASP.NET Core 兼容层应明确：

| 字段 | 建议 |
|---|---|
| `userid` | 作为前端展示和业务识别字段保留 |
| `username` | 可继续作为登录账号 / 账号名 |
| `nickname` | 仅在确认所有前端不再引用后废弃 |
| JWT Claim | 建议使用 `sub` 作为稳定用户唯一标识，`userid` 作为业务账号 |

迁移期可返回：

```json
{
  "userid": "zhangsan",
  "username": "zhangsan",
  "nickname": "zhangsan"
}
```

待所有内网前端确认不再使用 `nickname` 后，再移除 `nickname`。

## 8. refreshToken / siteConfig / terminal 残留分析

### 8.1 refreshToken

当前工作区仍有残留，并且属于运行链路：

- `web/src/stores/adminInfo.ts` 保存 `refreshToken`。
- `web/src/utils/axios.ts` 存在 409 后刷新 token 的逻辑。
- `web/src/api/common.ts` 存在 `refreshToken()` 接口。
- `web/src/api/backend/index.ts` 退出登录时提交 `refresh_token`。
- `app/admin/library/Auth.php` 仍生成和维护 refresh token。
- `app/admin/controller/Index.php` logout 仍读取 `refresh_token`。

JWT 重构后，如果统一使用门户或 Keycloak JWT，可以废弃 PHP 风格 `refreshToken`，但前端请求层必须同步移除或替换刷新机制。否则接口返回缺失会导致登录态异常。

### 8.2 siteConfig

当前工作区仍有残留：

- 后台布局初始化读取 `siteConfig`。
- `siteConfig` store 仍存在。
- logo、站点标题、模块接口等仍读取配置。
- 后端首页仍返回 `siteConfig`。

如果内网版已确认移除，则 ASP.NET Core 不需要复刻该字段。但以当前工作区为准，不能直接移除。

### 8.3 terminal

当前工作区仍有残留：

- `terminal` store 仍存在。
- 终端组件仍存在。
- 后台布局仍读取终端配置。
- API 中仍有终端配置、构建终端等接口。
- 模块安装流程仍引用终端状态。

如果内网版已确认移除，则该能力不应进入新 RBAC 边界；它不是 RBAC 必要字段。

## 9. project 权限隔离风险分析

### 9.1 当前源码核实结果

当前工作区未发现后台 RBAC 前端请求携带 `project`，也未发现后端 RBAC 按 `project` 过滤管理员、权限组、菜单规则或权限判断。

### 9.2 project 被前端伪造导致越权的风险

如果后续前端请求会携带 `project`，则存在明确越权风险：客户端传入的 `project` 不能作为可信权限边界。

高风险场景：

- 用户修改请求参数，把 `project=A` 改成 `project=B`。
- 后端仅按请求里的 `project` 查询菜单和按钮权限。
- 同一 `DxE_id` 在不同 project 下重复或语义不同。
- Redis 权限快照 key 未包含 project，导致跨系统污染。
- 超级管理员 `super` 未限定 project 域，导致全局越权。

### 9.3 project 最佳重构方案

建议在 ASP.NET Core 中将 `project` 作为“服务端确认的权限域”，而不是普通前端参数：

- JWT 中携带允许访问的 project 列表，或通过用户所属应用授权表查询。
- 前端可以继续传 `project` 用于选择当前系统，但后端必须校验该 project 是否在用户允许范围内。
- 所有 RBAC 查询必须包含 `Project` 条件：用户权限组、菜单、规则、按钮、权限快照。
- Redis key 必须包含 project，例如 `rbac:snapshot:{project}:{userGuid}:{version}`。
- 后端审计日志记录 `requestedProject` 与 `resolvedProject`。
- 不允许仅凭 body/query/header 中的 `project` 直接授权。

## 10. menus / auth() / v-auth 权限链路分析

### 10.1 菜单树生成机制

当前前端从后台首页接口获取 `menus`，然后调用路由处理逻辑生成动态路由和菜单。

菜单树核心字段：

| 字段 | 真实用途 |
|---|---|
| `type` | 判断是目录、菜单还是按钮权限节点 |
| `menu_type` | `tab` 注册内部页面，`iframe` 注册 iframe 页面，`link` 作为外链 |
| `path` | 路由 path，也是 authNode 的 key |
| `name` | 路由 name，也是按钮权限节点名称的一部分 |
| `component` | 动态导入页面组件 |
| `url` | link / iframe 地址 |
| `extend` | `add_menu_only` 时只加菜单不加路由 |
| `keepalive` | 路由缓存配置 |
| `title` | 菜单展示标题 |
| `icon` | 菜单图标 |
| `children` | 递归生成菜单和按钮权限 |

### 10.2 auth() 实际工作机制

当前 `auth(name)` 不是直接请求后端校验，而是读取前端运行时生成的 `authNode`：

1. 后端返回 `menus`。
2. 前端递归处理菜单树。
3. 非菜单 / 按钮类节点被转换为按钮权限节点。
4. 权限节点格式为：父级路由 `path` + `/` + 按钮节点 `name`。
5. `auth(name)` 判断当前路由 path 下是否存在该按钮节点。

例如当前页面 path 为 `/auth/admin`，按钮权限名为 `edit`，则判断：

```text
/auth/admin/edit
```

是否存在于当前页面的 `authNode` 中。

### 10.3 v-auth 实际工作机制

`v-auth` 与 `auth()` 机制一致，只是以指令方式控制 DOM：

- 若当前路由没有对应按钮权限节点，直接移除元素。
- 常见用法包括 `v-auth="'edit'"`、`v-auth="'del'"`、`v-auth="'sortable'"`。

注意：`auth()` / `v-auth` 只控制前端显示，不是安全边界。ASP.NET Core 后端仍必须在每个写接口做服务端授权。

### 10.4 按钮权限生成规则

按钮权限来自菜单树中挂在页面菜单下面的权限节点。当前前端常见按钮权限名：

| 权限名 | 用途 |
|---|---|
| `add` | 新增 |
| `edit` | 编辑 |
| `del` | 删除 |
| `sortable` | 拖拽排序 |

因此后端返回菜单树时，不能只返回可见菜单，还必须返回当前页面下的按钮权限节点。否则页面按钮会被隐藏。

## 11. 当前 RBAC 设计问题清单

| 问题 | 风险等级 | 说明 |
|---|---|---|
| 前端按钮权限仅隐藏 DOM | 高 | 不能作为后端安全边界，所有接口必须服务端校验 |
| 当前源码未按 `project` 隔离 | 高 | 多系统场景下可能出现菜单、规则、用户组串权 |
| 若信任前端传入 `project` | 高 | 用户可伪造 project 请求跨系统权限 |
| 对外使用自增 `id` | 中高 | 暴露内部主键，且与 Guid 重构目标冲突 |
| 雪花 long 若以 Number 返回 | 中高 | 前端可能出现精度丢失 |
| 权限规则绑定数字 ID | 中高 | 菜单迁移、重建、跨环境同步容易失效 |
| `refreshToken` 残留 | 中 | 与 JWT 统一认证目标冲突 |
| `siteConfig` / `terminal` 与 RBAC 混在首页返回 | 中 | 增加 RBAC 初始化负担和非权限依赖 |
| `super` 只在前端控制部分 UI | 中 | 后端必须有同等授权逻辑 |
| `name` / `path` 同时参与权限判断 | 中 | 改菜单 path 可能影响按钮权限 |
| `allmenu` 若返回全量菜单 | 高 | 会暴露未授权菜单结构，建议废弃 |
| 当前未发现 `permissionCode` | 中 | 不利于后续 Casbin 或跨系统权限统一 |

## 12. 推荐的新 ASP.NET Core RBAC 边界

### 12.1 总体边界

新 ASP.NET Core RBAC 不应复刻 PHP 内部实现，但应保留前端兼容合同。建议分为三层：

| 层次 | 职责 |
|---|---|
| 兼容 API 层 | 保持当前前端需要的字段、响应结构、菜单树结构 |
| RBAC 服务层 | 负责用户、权限组、菜单、按钮、project 隔离、权限快照 |
| 授权判断层 | 统一封装权限校验，为后续 Casbin 留接口 |

### 12.2 主键与兼容 ID

建议模型：

| 字段 | 用途 |
|---|---|
| `Id: Guid` | 内部数据库主键 |
| `DxE_id: string` | 对外兼容 ID，底层可由雪花 long 生成 |
| `Project: string` | 权限域 |
| `PermissionCode` / `RuleCode` | 稳定权限码 |

兼容期建议：

- 对当前仍依赖 `id` 的前端，可临时返回 `id = DxE_id`。
- 对内网已改造前端，返回严格大小写的 `DxE_id`。
- 后端入参同时兼容 `DxE_id` 和旧 `id` 一段时间，但服务层统一映射到内部 `Guid`。

### 12.3 登录与 JWT

建议：

- 不再兼容 PHP `batoken`。
- 使用标准 `Authorization: Bearer <JWT>`。
- 支持公司门户 JWT 或 Keycloak JWT。
- 后端从 JWT claims 解析用户身份，不信任前端传入的用户 ID。
- `project` 必须与 JWT claims 或服务端授权关系校验。

### 12.4 菜单与按钮权限

建议保留前端需要的 `menus` 结构，但内部权限判断从数字 ID 迁移到稳定权限码：

```text
Project + PermissionCode
```

菜单返回中可以同时包含：

| 字段 | 用途 |
|---|---|
| `DxE_id` | 前端树、编辑、删除、排序兼容 |
| `name` | 当前 auth/v-auth 兼容 |
| `path` | 当前 auth/v-auth 兼容 |
| `permissionCode` | 新后端授权判断 |
| `ruleCode` | 规则稳定标识 |

## 13. Redis 权限快照建议

为提高权限加载速度，建议 ASP.NET Core 生成用户权限快照并缓存到 Redis。

### 13.1 快照 key

建议 key 结构：

```text
rbac:snapshot:{project}:{userGuid}:{version}
```

或：

```text
rbac:snapshot:{project}:{userid}:{version}
```

其中 `project` 必须参与 key，避免多系统权限污染。

### 13.2 快照内容

建议快照包含：

| 字段 | 用途 |
|---|---|
| `project` | 当前权限域 |
| `userId` / `userGuid` | 用户标识 |
| `groups` | 用户所属权限组 |
| `super` | 是否超级管理员 |
| `menus` | 前端菜单树 |
| `buttonNodes` | 前端 auth/v-auth 使用的按钮节点 |
| `permissionCodes` | 后端授权判断使用 |
| `ruleCodes` | 规则稳定标识 |
| `DxE_ids` | 兼容旧前端或迁移期排查 |
| `version` | 权限版本号 |
| `expiresAt` | 过期时间 |

### 13.3 失效策略

以下事件必须使快照失效：

- 用户权限组变更。
- 权限组 rules 变更。
- 菜单 / 规则新增、编辑、删除、排序。
- project 授权关系变更。
- 用户禁用 / 启用。
- `super` 状态变更。

建议使用“权限版本号 + 短 TTL”组合。写入变更时递增 project 级或用户级权限版本，读取时发现版本不一致则重建快照。

## 14. 后续 Casbin 接入边界

本次不接入 Casbin，也不设计具体 Casbin 表结构。仅建议预留边界：

- 当前前端继续使用 `menus`、`auth()`、`v-auth`，不感知 Casbin。
- 后端所有接口授权统一调用 `IAuthorizationService` 或自定义 `IRbacPermissionChecker`。
- 当前 RBAC 服务先输出 `permissionCode` 判断结果。
- 后续如接入 Casbin，只替换授权判断层，不改变前端 DTO。
- Casbin domain 可对应 `project`。
- subject 可对应用户、角色或权限组。
- object 建议对应 `permissionCode`。
- action 可对应 `access`、`execute`、`read`、`write` 等后端动作。

Casbin 适合后续评估，但不适合在当前兼容字段尚未完全核实、`DxE_id` / `userid` / `project` 内网差异尚未落地确认时立即接入。

## 15. 不允许本次修改的内容

本次仅为重构前兼容性核实和边界分析，不应执行以下事项：

- 不修改 PHP 后端代码。
- 不修改 `web/` 前端代码。
- 不生成 ASP.NET Core 代码。
- 不生成数据库迁移。
- 不设计具体数据库表结构。
- 不接入 Casbin。
- 不分析会员 RBAC。
- 不分析无关业务模块。
- 不改变现有接口行为。

最终判断：当前工作区中的 `rbac-frontend-compatibility-contract.md` 可以作为“原始框架 / 当前工作区源码”的兼容分析参考，但不足以直接作为“内网版 ASP.NET Core RBAC 重构”的最终依据。重构前必须以真实内网前端分支再次核实 `DxE_id`、`userid`、`project`、`refreshToken`、`siteConfig`、`terminal` 的实际状态，并将前端真实依赖字段固化为 ASP.NET Core 兼容 API 合同。
