# RBAC 前端兼容合同分析报告

**分析日期**: 2026-05-08  
**输入依据**: `spec/main/rbac-contract-readonly-audit.md`、`web/` 前端源码目录  
**范围**: 仅后台管理员 RBAC；不分析会员 RBAC；不复刻 PHP；不设计数据库；不生成重构代码；不接入 Casbin。  
**目标**: 提炼当前前端真实依赖，作为 ASP.NET Core 重构后台 RBAC 的前端兼容合同。

## 1. 总结结论

当前前端真正依赖的 RBAC 合同不是 PHP 表结构，也不是 `代码核实.md` 中的 ABP 风格 DTO，而是：

1. 登录接口返回 `userInfo` 与 `routePath`。
2. 后台初始化接口返回 `adminInfo`、`menus`、`siteConfig`、`terminal`。
3. `menus` 菜单树字段驱动动态路由、左侧菜单、按钮权限。
4. 按钮权限由菜单树中的非 `menu/menu_dir` 节点生成，不依赖用户组、角色名、`super`、`project` 或 `allmenu`。
5. 前端默认以 `id` 作为表格主键、树节点主键、远程下拉 value、编辑/删除/排序参数，但该 `id` 不要求一定是数据库自增 int；它必须能稳定转字符串并能被 `parseInt` 的旧校验点兼容。

因此，后端可使用 `Guid` 作为数据库主键，另设 `DXE_id` 雪花 long 作为业务兼容 ID；但若不改前端，接口输出层仍必须提供一个兼容字段 `id`。推荐让前端兼容层中的 `id` 映射到雪花 long `DXE_id`，数据库 Guid 仅内部使用。

## 2. 前端真实依赖字段清单

### 2.1 响应壳

前端 Axios 封装默认要求：

| 字段 | 是否必须 | 用途 |
|---|---:|---|
| `code` | 是 | `code === 1` 视为成功；`409` 触发刷新 token；`302` 触发路由跳转 |
| `msg` | 是 | 错误/成功提示 |
| `data` | 是 | 业务数据 |
| `time` | 低 | 前端未见强依赖，但建议保留 |

### 2.2 登录返回

后台登录页 `web/src/views/backend/login.vue` 真实依赖：

| 字段 | 是否必须 | 用途 |
|---|---:|---|
| `data.userInfo` | 是 | 写入 `adminInfo` store |
| `data.userInfo.id` | 是 | 当前管理员 ID，用于禁止删除自己、权限组选择参数 |
| `data.userInfo.username` | 中 | 用户资料/显示 |
| `data.userInfo.nickname` | 是 | 顶栏、欢迎语显示 |
| `data.userInfo.avatar` | 是 | 顶栏头像 |
| `data.userInfo.lastlogintime` | 中 | 顶栏/资料显示 |
| `data.userInfo.token` | 是 | 后续请求认证 token；后续可换成 JWT 字符串 |
| `data.userInfo.refreshToken` | 可改 | 当前刷新 token 使用；若后续不做刷新 token 可调整前端 |
| `data.userInfo.super` | 中 | 只控制终端、清缓存入口显示 |
| `data.routePath` | 是 | 登录成功后 `router.push({ path: routePath })` |

### 2.3 后台初始化返回

后台布局 `web/src/layouts/backend/index.vue` 真实依赖：

| 字段 | 是否必须 | 用途 |
|---|---:|---|
| `data.adminInfo` | 是 | 刷新页面后重新填充管理员信息 |
| `data.menus` | 是 | 构建动态路由、菜单、按钮权限 |
| `data.siteConfig` | 中 | 站点名、CDN、上传配置等，不是 RBAC 但布局依赖 |
| `data.terminal.installServicePort` | 低 | 终端功能依赖；仅 `super` 用户可见 |
| `data.terminal.npmPackageManager` | 低 | 终端功能依赖 |

### 2.4 菜单树节点

`web/src/utils/router.ts`、菜单组件、表单组件真实依赖：

| 字段 | 是否必须 | 用途 |
|---|---:|---|
| `id` | 高 | 表格主键、树节点 key、编辑/删除/排序、远程下拉 value |
| `pid` | 高 | 菜单规则父级、拖拽排序限制、树结构管理 |
| `type` | 高 | `menu_dir`、`menu` 进入菜单；其他节点作为按钮权限 |
| `title` | 高 | 菜单标题、权限树 label |
| `name` | 高 | 路由 name；按钮权限码核心 |
| `path` | 高 | 前端路由 path 拼接 |
| `icon` | 中 | 菜单图标 |
| `menu_type` | 高 | `tab/link/iframe` 决定路由和点击行为 |
| `url` | 高 | `link/iframe` 菜单真实地址 |
| `component` | 高 | `tab` 菜单动态加载的 Vue 组件路径 |
| `keepalive` | 中 | 页面缓存组件名/标记 |
| `extend` | 高 | 控制“只加路由”或“只加菜单” |
| `children` | 高 | 菜单树和权限树递归 |
| `status` | 中 | 菜单管理列表显示/编辑 |
| `weigh` | 中 | 排序 |
| `remark` | 低 | 表单备注 |
| `createtime/updatetime` | 中 | 管理列表显示和搜索 |

### 2.5 管理员与角色管理

后台管理员页面真实依赖：

| 字段 | 是否必须 | 用途 |
|---|---:|---|
| `id` | 高 | 当前用户判断、编辑、删除 |
| `username` | 高 | 登录名、列表、表单 |
| `nickname` | 高 | 昵称、顶部显示 |
| `group_arr` | 高 | 管理员所属角色组远程多选回显/提交 |
| `group_name_arr` | 中 | 管理员列表标签展示 |
| `avatar/email/mobile/motto/status` | 中 | 管理员资料和表单 |
| `lastlogintime/createtime` | 中 | 列表时间显示/查询 |

权限组页面真实依赖：

| 字段 | 是否必须 | 用途 |
|---|---:|---|
| `data.group` | 高 | 当前管理员所属组，禁止修改/删除自己的组 |
| `list[].id` | 高 | 树、编辑、删除、自身组判断 |
| `list[].pid` | 高 | 父子组 |
| `list[].name` | 高 | 组名 |
| `list[].rules` | 高 | 列表展示为文本；编辑回显时为规则 ID 数组或 `*` |
| `list[].children` | 高 | 树形表格 |

## 3. 高风险兼容字段

这些字段改名、缺失或改变语义会直接破坏当前后台前端：

- `code/msg/data`
- `data.userInfo.token`
- `data.userInfo.id`
- `data.userInfo.nickname`
- `data.routePath`
- `data.adminInfo`
- `data.menus`
- 菜单节点：`id/pid/type/title/name/path/menu_type/url/component/extend/children`
- 按钮节点：`name`
- 管理员：`group_arr/group_name_arr`
- 权限组：`data.group`、`rules`
- 表格通用：`data.list/data.total/data.remark`
- 下拉通用：`data.options` 或 `data.list`

## 4. 可废弃字段

基于 `web/src` 检索，本项目后台前端未真实依赖：

| 字段 | 结论 | 说明 |
|---|---|---|
| `DXE_id` | 可不直接暴露给当前前端 | `web/src` 未发现引用 |
| `allmenu` | 可移除 | `web/src` 未发现引用；当前前端使用 `menus` |
| `project` | 不参与当前前端权限判断 | `web/src` 未发现引用 |
| `supre` | 应废弃 | `web/src` 未发现引用；真实字段是 `super` |
| `UserIdentifier` | 可废弃 | 未发现前端依赖 |
| `infoMessage` | 可废弃 | 未发现后台前端依赖 |
| `isLogout` | 可废弃 | 未发现后台前端依赖 |

注意：这是基于当前 `web/` 源码的结论。若其他内网系统有二次开发前端，应再用同样方法扫描那些仓库。

## 5. 前端权限链路分析

后台权限链路如下：

1. 登录页调用 `/admin/index/login`。
2. 登录成功后将 `res.data.userInfo` 写入 `adminInfo` store。
3. 登录成功后按 `res.data.routePath` 跳转，通常是 `/admin`。
4. 后台布局加载时调用 `/admin/index/index`。
5. 返回 `res.data.menus` 后执行 `handleAdminRoute(res.data.menus)`。
6. `handleAdminRoute()` 动态注册后台路由，并生成菜单树和按钮权限 Map。
7. 菜单展示读取 `navTabs.state.tabsViewRoutes`。
8. 页面按钮通过 `auth('add')` 或 `v-auth="'edit'"` 判断。

这里的关键点是：**权限判断最终只依赖菜单树里的按钮节点 name 与当前 route.path 的拼接关系**。

## 6. auth() 实际工作机制

`web/src/utils/common.ts` 中：

```ts
auth(name) => navTabs.state.authNode[currentRoute.path]
    contains currentRoute.path + '/' + name
```

实际规则：

- 当前页面路由 path 例如 `/admin/auth/menu`。
- 按钮权限节点应生成 `/admin/auth/menu/add`、`/admin/auth/menu/edit`、`/admin/auth/menu/del`、`/admin/auth/menu/sortable`。
- `auth('add')` 检查是否存在 `/admin/auth/menu/add`。
- `v-auth="'edit'"` 检查是否存在 `/admin/当前页面/edit`。

不参与 `auth()` 的字段：

- `super`
- `group_arr`
- `group_name_arr`
- `project`
- `DXE_id`
- `allmenu`
- JWT claims

这意味着后端即使使用更复杂的权限模型，也必须输出能让前端生成上述按钮节点的 `menus` 树。

## 7. 菜单树生成机制

`handleMenuRule()` 负责把后端 `menus` 转成前端路由菜单：

- `extend == 'add_rules_only'`：跳过菜单生成，但仍可在动态路由阶段注册路由。
- `type == 'menu' || type == 'menu_dir'`：生成菜单项。
- `type == 'menu_dir'` 且 `children` 为空：不显示。
- `menu_type == 'link' || menu_type == 'iframe'`：菜单 path 使用 `url`。
- 其他情况：菜单 path 使用 `'/admin/' + path`。
- `title/icon/keepalive/menu_type` 被写入 route meta。
- 非 `menu/menu_dir` 节点进入按钮权限 `authNode`。

`addRouteAll()` 负责动态注册路由：

- `extend == 'add_menu_only'`：不注册路由。
- `type == 'menu' && menu_type == 'tab'`：要求 `component` 能匹配 `/src/views/backend/**/*.vue`。
- `type == 'menu' && menu_type == 'iframe'`：注册 iframe route。
- `link` 只用于菜单跳转外链，不注册 Vue 页面组件。

## 8. 按钮权限生成机制

按钮权限不是单独接口返回，而是来自 `menus` 的子节点：

```ts
if route.type is not menu/menu_dir:
    authNode.push('/admin/' + route.name)
```

例如菜单页：

- 页面菜单节点：`name = auth/menu`、`path = auth/menu`
- 按钮节点：`name = auth/menu/add`
- 当前路由：`/admin/auth/menu`
- `auth('add')` 要求按钮权限：`/admin/auth/menu/add`

默认按钮映射：

- 顶部新增：`auth('add')`
- 顶部编辑：`auth('edit')`
- 顶部删除：`auth('del')`
- 行编辑：`v-auth="'edit'`
- 行删除：`v-auth="'del'`
- 拖拽排序：`v-auth="'sortable'`

风险点：

- 文档或后端若输出 `delete`，前端不会等价识别为 `del`。
- 按钮权限 `name` 必须与页面路径同前缀，例如 `auth/group/del`。
- 当前 `v-auth` 指令只在 mounted 时移除元素，后续动态权限变化不会自动恢复。

## 9. DXE_id 是否必须为 int

当前后台前端不依赖 `DXE_id`。它依赖的是 `id`。

`id` 当前有几个“像 int”的使用点：

- Element Plus Tree `node-key="id"`。
- 远程下拉默认 `pk='id'`，并把 value 转成字符串。
- `baTable.table.pk` 默认是 `id`，编辑/删除/排序都发送 `id`。
- 菜单/组表单里有 `parseInt(val) == parseInt(form.items.id)` 的“父级不能等于自己”校验。

结论：

- `DXE_id` 不必为 int，因为前端未用它。
- 但如果不改前端，`id` 最好继续输出为可被 `parseInt` 正确处理的数值或数字字符串。
- 若 `id` 改成 Guid 字符串，树和表格多数能工作，但 `parseInt(Guid)` 会得到 `NaN`，父级不能选自己的校验会失效。

## 10. Guid + DXE_id(long) 是否可行

可行，但建议按“内部主键”和“前端兼容 ID”分层：

| 层 | 建议 |
|---|---|
| 数据库内部主键 | Guid |
| 业务兼容 ID | `DXE_id` 雪花 long |
| 当前前端接口 `id` | 输出 `DXE_id` 的 long/数字字符串 |
| 内部 API 或新系统 | 可返回 `guid` 或 `idGuid`，但不要替代旧前端 `id` |

这样可以满足：

- 后端数据库不依赖自增 int。
- 前端 `id/pid`、远程下拉、树节点、排序、编辑、删除仍正常。
- 后续新前端可逐步使用 Guid 或显式 `DXE_id`。

强烈不建议在旧前端兼容接口中直接把 `id` 改成 Guid，除非同步修改所有 `parseInt` 校验和可能依赖数字 ID 的 UI 逻辑。

## 11. allmenu 是否可移除

可以移除。

依据：

- `web/src` 未发现 `allmenu/allMenu` 引用。
- 当前后台权限菜单入口是 `data.menus`。
- 菜单树已经是当前管理员可访问菜单，而不是“所有菜单”。

安全建议：

- 后端不要向普通管理员返回全量菜单/全量权限。
- 只返回当前用户、当前 project 下可见的菜单和按钮规则。
- 菜单管理页面需要展示可管理范围时，应走管理接口并受后端权限限制，而不是登录态返回全量 `allmenu`。

## 12. project 的最佳重构方案

当前前端不传 `project`，也不在 `auth()` 中判断 `project`。但你计划保留 project 做多系统权限隔离，这是合理的。

建议兼容方案：

1. 后端从 JWT claims、请求域名、应用配置或显式 header 中解析当前 `project`。
2. `/admin/index/index` 返回的 `menus` 已经是当前 `project` 过滤后的菜单树。
3. `authNode` 仍按当前前端机制生成，不要求前端传 `project`。
4. 菜单管理和角色管理接口可增加 `project` 过滤，但旧前端不感知。
5. 如未来同一前端切换多个系统，可再在前端 store 增加 `currentProject`，但不要阻塞当前兼容。

不建议：

- 不要把 `project` 拼进按钮权限 name，例如 `/admin/auth/menu/add@project`，会破坏 `auth('add')`。
- 不要要求当前前端每次 `auth()` 带 project 参数。

## 13. super/supre 真实依赖

真实依赖字段是 `super`，不是 `supre`。

用途：

- `adminInfo.super` 控制顶部终端按钮显示。
- `adminInfo.super` 控制清缓存入口显示。
- 注释明确说明：用于判定是否显示终端按钮等，不做任何权限判断。

结论：

- `supre` 可废弃。
- 后续统一为 `super`。
- `super` 不能替代菜单/按钮权限树；即使是超级管理员，也建议后端返回完整授权后的 `menus`，让前端机制保持一致。

## 14. routePath 真实用途

后台真实用途：

- 登录成功后，`login.vue` 调用 `router.push({ path: res.data.routePath })`。
- Axios 拦截器遇到 `code == 302` 时，如果 `data.routePath` 存在，也会跳转。

结论：

- `routePath` 应保留。
- 登录成功建议返回 `/admin`。
- 未登录/认证失效可返回 `/admin/login` 或直接让前端跳命名路由。
- `routePath` 不参与权限判断。

## 15. menu_type / extend / keepalive 真实用途

### menu_type

必须保留，枚举建议：

- `tab`: 加载 Vue 页面组件。
- `link`: 外链，点击时 `window.open`。
- `iframe`: 注册 iframe 页面。

### extend

必须保留，枚举建议：

- `none`: 正常菜单和规则。
- `add_rules_only`: 不显示为菜单，但可作为路由/规则。
- `add_menu_only`: 显示为菜单，但不注册动态路由。

### keepalive

建议保留。它会进入路由 meta，并被后台 router-view 用于缓存控制。当前代码判断 `typeof meta.keepalive == 'string'`，而菜单表单中使用 `0/1`。为减少兼容风险，建议后端继续返回与现有前端一致的 `0/1` 或可兼容字符串；如果要增强缓存，应单独梳理 keepalive 语义。

## 16. 前端是否依赖自增 int ID

前端不关心“自增”，但当前实现隐含依赖“数字型可比较 ID”。

必须保持：

- `id` 稳定唯一。
- `pid` 能与父节点 `id` 对应。
- `id/pid` 能被树组件、表格 row-key、远程下拉和后端接口识别。
- 排序接口 `id/targetId` 可定位两条记录。

建议：

- 旧兼容接口中 `id` 使用雪花 long。
- 内部 Guid 不直接暴露为旧前端 `id`。
- 如要暴露 Guid，新增字段如 `guid`，不要替换 `id`。

## 17. 后续 Casbin 是否适合接入

适合后续评估，但不应改变前端合同。

Casbin 若接入，应作为后端策略判断/权限计算引擎，最终仍输出当前前端需要的：

- 当前用户可见 `menus` 树。
- 菜单节点和按钮节点的 `name/path/type/menu_type/extend`。
- 管理端接口的后端鉴权结果。

不建议让当前前端直接感知 Casbin 的 `sub/dom/obj/act` 模型，也不建议让 `auth()` 直接请求后端实时判权。当前前端的优势是一次初始化后本地判断按钮显示；后端只需保证接口调用时再次鉴权。

## 18. 最终兼容合同建议

ASP.NET Core 重构时，面向当前后台前端的最小合同：

1. 认证使用 JWT，但登录返回仍填充 `data.userInfo.token`，值为 JWT。
2. 保留响应壳 `code/msg/data/time`。
3. 保留 `routePath`。
4. 保留初始化接口返回 `adminInfo/menus/siteConfig/terminal`。
5. `adminInfo` 使用 `id/username/nickname/avatar/lastlogintime/token/refreshToken/super`。
6. `menus` 只返回当前用户、当前 project 可访问菜单和按钮。
7. 菜单节点保留 `id/pid/type/title/name/path/icon/menu_type/url/component/keepalive/extend/children`。
8. 旧前端接口中的 `id/pid` 使用雪花 long 兼容 ID，不直接使用 Guid。
9. `allmenu/supre/DXE_id/project` 不作为当前前端必需字段；`project` 留在后端权限过滤维度。
10. Casbin 后续可作为后端权限引擎评估，但不能替代前端 `menus -> authNode -> auth()` 的兼容输出。
