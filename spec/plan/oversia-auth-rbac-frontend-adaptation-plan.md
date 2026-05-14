# oversia 权限管理前端重写计划

## 1. 目标

本计划用于交付 Claude 开发新的权限管理界面。开发依据以本文件和 `README.md` 为准。

后端项目：`E:\router\router`  
前端项目：`E:\Web\oversia`  
重点前端目录：`src/views/backend/auth`

本次目标不是把旧 BuildAdmin 权限页简单换接口，而是重写权限中心前端接入层和页面业务模型：

- 管理员账号管理：`/api/admin`
- 权限组管理：`/api/group`
- 菜单/按钮规则管理：`/api/rule`
- API 地址鉴权映射管理：`/api/api-map`
- Project 授权和超管切换：`/api/project-grant`
- 登录初始化和菜单加载：`/api/auth/login`、`/api/admin/index`

重要背景：DxEId 已移除。前端不得再使用 `dxeId` 作为编辑、删除、排序、授权参数。

## 2. 当前前端核实结论

当前 `E:\Web\oversia\src\views\backend\auth` 仍是 BuildAdmin 旧权限页：

- `auth/admin/index.vue` 使用 `new baTableApi('/admin/auth.Admin/')`
- `auth/group/index.vue` 使用 `new baTableApi('/admin/auth.Group/')`
- `auth/rule/index.vue` 使用 `new baTableApi('/admin/auth.Rule/')`
- `src/api/backend/auth/group.ts` 的 `getAdminRules()` 仍请求 `/admin/auth.Rule/index`
- 表单字段仍是旧模型：`id`、`pid`、`rules`、`group_arr`、`nickname`、`avatar`、`email`、`mobile`、`password`、`motto` 等

当前 `src/utils/axios.ts` 也不适合直接调用 RBAC API：

- 旧封装默认认为 `code === 1` 是成功。
- RBAC API 的成功码是 `code === 0`。
- 旧封装自动发送 `batoken` / `ba-user-token`。
- RBAC API 要求 `Authorization: Bearer <jwt>` 和 `X-Project: oversia`。

结论：不能直接复用 `baTableApi` 对接 `/api/*`。必须新增 RBAC 专用 API client 或在调用层显式适配响应码、headers 和 `data` 解包。

## 3. 总体设计

建议新增专用目录：

```text
src/api/backend/rbac/client.ts
src/api/backend/rbac/types.ts
src/api/backend/rbac/admin.ts
src/api/backend/rbac/group.ts
src/api/backend/rbac/rule.ts
src/api/backend/rbac/apiMap.ts
src/api/backend/rbac/projectGrant.ts
src/api/backend/rbac/adapters.ts
```

建议新增或重写页面：

```text
src/views/backend/auth/admin/index.vue
src/views/backend/auth/admin/popupForm.vue
src/views/backend/auth/group/index.vue
src/views/backend/auth/group/popupForm.vue
src/views/backend/auth/rule/index.vue
src/views/backend/auth/rule/popupForm.vue
src/views/backend/auth/apiMap/index.vue
src/views/backend/auth/apiMap/popupForm.vue
src/views/backend/auth/projectGrant/index.vue
```

如需保留原页面入口名称，可以在原 `auth/admin`、`auth/group`、`auth/rule` 文件内重写实现；新增 `auth/apiMap` 和 `auth/projectGrant` 作为权限中心新页面。

## 4. RBAC API Client 约束

### 4.1 请求头

所有 `/api/*` 业务请求必须带：

```http
Authorization: Bearer <jwt>
X-Project: oversia
```

项目 `oversia` 可以先做常量，也可以从环境变量或登录态读取：

```ts
const RBAC_PROJECT = 'oversia'
```

### 4.2 响应解包

RBAC 统一响应：

```ts
type RbacResponse<T> = {
  code: number
  msg: string
  data: T
  time: number
}
```

成功条件是 `code === 0`。失败时抛出 `msg`，不要沿用旧 `code === 1` 判断。

分页响应：

```ts
type RbacPaged<T> = {
  list: T[]
  total: number
}
```

`time` 和 `total` 都是 JSON number，不是 string。

### 4.3 登录态接入

当前 `adminInfo.token` 保存的是旧后台 token。若门户已通过 URL 或外部登录态传入 JWT，则 RBAC client 应读取同一 token 并发送为 `Authorization: Bearer ${token}`。

如果现有 token 不是 JWT，需要在登录接入层补一层真实 JWT 获取/注入逻辑；不要把 `batoken` 当成 RBAC Bearer token 的替代品。

## 5. 标识字段迁移规则

| 模块 | 操作标识 | 说明 |
| --- | --- | --- |
| 管理员 | `userid` | 编辑、删除、状态、用户名更新全部使用 `userid` |
| 权限组 | `groupCode` | 编辑、删除、规则授权、成员管理全部使用 `groupCode` |
| 规则 | `ruleCode` | 编辑、删除、状态、排序全部使用 `ruleCode` |
| API 映射 | `id` | 后端返回 Guid，只用于 `/api/api-map/{id}` |
| Project 授权 | `userid` | 授权、撤销、超管切换使用用户业务 ID |

禁止事项：

- 禁止新增 `dxeId` 字段。
- 禁止把 `id` 当作通用业务操作键。
- 禁止把规则树提交为旧数字 ID 数组。
- 权限组授权只提交 `ruleCodes[]`。

## 6. 页面一：管理员管理

### 6.1 API

```text
GET    /api/admin/list
POST   /api/admin
PUT    /api/admin/{userid}
PUT    /api/admin/{userid}/status
PUT    /api/admin/{userid}/username
DELETE /api/admin/{userid}
```

列表查询参数：

- `userid`
- `groupCode`
- `keyword`
- `status`
- `page`
- `pageSize`

列表字段：

- `userid`
- `username`
- `status`
- `projectCodes`
- `groupCodes`
- `groupNames`

### 6.2 UI 字段

列表保留：

- 用户 ID：`userid`
- 显示名称：`username`
- 权限组：`groupNames` / `groupCodes`
- 状态：`Active` / `Disabled`
- Project：`projectCodes`

移除旧字段：

- `nickname`
- `avatar`
- `email`
- `mobile`
- `password`
- `motto`
- `last_login_time`

新增表单：

```ts
type AdminCreateForm = {
  userid: string
  username: string
  groupCode: string[]
}
```

编辑表单：

```ts
type AdminUpdateForm = {
  username?: string
  status?: 'Active' | 'Disabled'
  groupArr?: string[]
}
```

### 6.3 Project 授权入口

管理员列表行上建议新增操作：

- 授权到当前 Project
- 设为超管
- 取消超管
- 撤销当前 Project 授权

这些操作不属于 `/api/admin`，必须调用 `/api/project-grant`。

## 7. 页面二：Project 授权与超管管理

这是旧计划缺失的重点。RBAC 的“超管”不是管理员表字段，而是当前 project 下的 `rbac_project_grant.is_super`。

### 7.1 API

```text
POST   /api/project-grant
DELETE /api/project-grant/{userid}
PUT    /api/project-grant/{userid}/super
```

请求体：

```json
{
  "userid": "196045",
  "isSuper": true
}
```

超管切换：

```json
{
  "isSuper": false
}
```

### 7.2 设计原则

- “授权到 Project”和“设为超管”必须拆成可理解的两个动作。
- 用户没有当前 Project 授权时，直接调用 `PUT /api/project-grant/{userid}/super` 会失败，必须先 `POST /api/project-grant`。
- `POST /api/project-grant` 如果授权已存在，会更新 super 标志。
- `DELETE /api/project-grant/{userid}` 会撤销该用户当前 project 的授权，属于高风险操作。

### 7.3 UI 建议

可以做成独立页面 `auth/projectGrant`，也可以先作为管理员列表的侧边抽屉。

推荐独立页面字段：

- `userid`
- `username`
- `projectCodes`
- `isSuper`
- `status`
- 操作：授权、撤销、设为超管、取消超管

当前后端 README 未提供 Project 授权分页列表接口。前端可先复用 `GET /api/admin/list` 展示用户列表，并根据 `adminInfo.super` 或 `adminInfo.project` 显示当前操作者身份；如果需要准确展示每个用户当前 project 的 `isSuper`，需要后端补充只读查询接口。开发时不要用猜测字段伪造 super 状态。

### 7.4 风险提示

- 超管用户在 `RbacMenuBuilder` 中会返回完整菜单树。
- 超管变更会触发缓存失效和权限快照变化。
- 前端必须二次确认“设为超管”和“撤销授权”。
- 不允许普通管理员看到或操作超管切换按钮，除非后端权限已通过 `button:grant.super` 授权。

## 8. 页面三：权限组管理

### 8.1 API

```text
GET    /api/group/index
GET    /api/group/list
POST   /api/group
PUT    /api/group/{groupCode}
PUT    /api/group/{groupCode}/rules
PUT    /api/group/{groupCode}/status
POST   /api/group/{groupCode}/members
DELETE /api/group/{groupCode}/members/{userid}
DELETE /api/group/{groupCode}
```

### 8.2 UI 字段

列表字段：

- `groupCode`
- `groupName`
- `project`
- `status`
- `permissionCodes`

表单字段：

```ts
type GroupForm = {
  groupCode?: string
  groupName: string
  parentGroupCode?: string
  status: 'Active' | 'Disabled'
  ruleCodes: string[]
}
```

### 8.3 权限树

权限组表单里的权限树读取：

```text
GET /api/rule/tree
```

树节点 key 必须使用 `ruleCode`：

```vue
<el-tree node-key="ruleCode" />
```

提交：

```json
{
  "ruleCodes": ["dashboard", "system.user"]
}
```

不要提交：

- `id[]`
- `dxeId[]`
- 旧 `rules[]`
- `permissionCodes[]`

半选节点策略：建议提交 checked + halfChecked 的 `ruleCode`，保持旧页面“父级可见”的体验；如产品要求只提交叶子节点，则必须在 UI 上明确提示。

## 9. 页面四：菜单/按钮规则管理

### 9.1 API

```text
GET    /api/rule/tree
GET    /api/rule/list
POST   /api/rule
PUT    /api/rule/{ruleCode}
PUT    /api/rule/{ruleCode}/status
PUT    /api/rule/{ruleCode}/weigh
DELETE /api/rule/{ruleCode}
```

### 9.2 类型映射

后端枚举：

- `MenuDir`
- `Menu`
- `Button`

旧前端值：

- `menu_dir`
- `menu`
- `button`

必须建立双向映射：

```ts
menu_dir -> MenuDir
menu     -> Menu
button   -> Button

MenuDir  -> menu_dir
Menu     -> menu
Button   -> button
```

菜单类型映射：

```ts
tab    -> Tab
link   -> Link
iframe -> Iframe
```

状态映射：

```ts
1 / true  -> Active
0 / false -> Disabled
```

缓存映射：

```ts
1 -> true
0 -> false
```

### 9.3 表单字段

创建规则：

```ts
type RuleCreateForm = {
  ruleCode: string
  permissionCode: string
  title: string
  type: 'MenuDir' | 'Menu' | 'Button'
  name?: string
  path?: string
  parentRuleCode?: string
  menuType?: 'Tab' | 'Link' | 'Iframe'
  url?: string
  component?: string
  extend?: string
  remark?: string
  keepalive?: boolean
  weigh?: number
}
```

建议自动生成 `permissionCode`：

- 目录/菜单：`menu:${ruleCode}`
- 按钮：`button:${ruleCode}`

允许高级用户手动覆盖，但必须提示“API 映射和权限组授权依赖 permissionCode”。

### 9.4 父子关系

旧 `pid` 改为 `parentRuleCode`。父级选择器读取 `/api/rule/tree`，选中后提交父节点 `ruleCode`。

按钮规则必须有 `parentRuleCode`。

### 9.5 排序

排序调用：

```text
PUT /api/rule/{ruleCode}/weigh
```

不要再调用旧 `sortable` 接口。

## 10. 页面五：API 地址鉴权映射管理

这是本次新增页面，核心用于维护“某个 API 地址需要哪个权限码才能访问”。

### 10.1 API

```text
GET    /api/api-map/list
POST   /api/api-map
PUT    /api/api-map/{id}
DELETE /api/api-map/{id}
```

查询参数：

- `permissionCode`
- `action`
- `resourceType`
- `keyword`
- `status`
- `page`
- `pageSize`

创建：

```json
{
  "httpMethod": "GET",
  "routePattern": "/api/admin/list",
  "permissionCode": "menu:admin.list",
  "action": "read"
}
```

更新：

```json
{
  "permissionCode": "button:admin.edit",
  "action": "update"
}
```

### 10.2 UI 字段

列表列：

- HTTP 方法
- API 路由模板
- 权限码
- 动作
- 资源类型
- 标题
- 状态
- 操作

如果 `GET /api/api-map/list` 返回的是权限视图聚合结果而缺少 `id/httpMethod/routePattern`，则说明当前 ES permission view 不足以支撑编辑/删除。此时前端只能做只读“权限视图”，编辑页面需要后端补充 MySQL 源数据分页查询，或在 `PermissionViewSearchResult` 中补充 `id/httpMethod/routePattern`。

### 10.3 表单控件

HTTP 方法使用下拉：

- `GET`
- `POST`
- `PUT`
- `DELETE`
- `PATCH`

`routePattern` 输入规则：

- 必须以 `/api/` 开头。
- 路由参数使用 `{name}`，例如 `/api/group/{groupCode}`。
- 不要填写域名。
- 不要填写 query string。

`permissionCode` 建议从 `/api/rule/tree` 中选择，也允许手工输入。

`action` 使用下拉：

- `read`
- `create`
- `update`
- `delete`
- `execute`
- `access`

### 10.4 鉴权语义

运行时鉴权链路：

1. 前端请求业务 API。
2. 后端根据 `project + httpMethod + routePattern` 查 `rbac_api_permission_map`。
3. 查到 `permissionCode + action`。
4. 后端用当前用户的 Project 授权、超管状态、组权限、Casbin policy 判断是否放行。

页面文案应明确：API 映射不是“给用户授权”，而是“定义访问某个 API 需要哪一个权限码”。用户是否能访问，仍由权限组、Project 授权和超管状态决定。

### 10.5 高风险限制

- 禁止删除当前页面自身依赖的 API 映射，除非二次确认。
- 删除或修改映射会触发 api-map 缓存失效，但前端仍应提示“权限变化可能需要刷新页面或等待缓存更新”。
- 如果创建了新的业务 API 映射，还需要在规则管理里创建对应 `permissionCode`，并在权限组里授权该规则。

## 11. 登录初始化与菜单适配

当前后端：

```text
POST /api/auth/login
GET  /api/admin/index
```

返回：

- `token`
- `routePath`
- `adminInfo`
- `menus`

前端需要适配：

- RBAC 成功码 `0`
- `adminInfo.userid` 映射到前端可用的 `adminInfo.id` 或新增 `userid` 字段
- `adminInfo.username` 可映射到旧 `nickname`，避免顶部用户名为空
- `adminInfo.super` 继续写入 `adminInfo.super`
- 菜单节点中的 `ruleCode` 保留到 meta，供按钮权限判断使用

建议修改 `src/stores/interface/index.ts` 和 `src/stores/adminInfo.ts`：

```ts
type AdminInfo = {
  id: string | number
  userid?: string
  username: string
  nickname: string
  token: string
  refresh_token?: string
  super: boolean
  project?: string
}
```

## 12. 权限按钮控制

旧前端按钮控制依赖 `navTabs` 的 auth node。新 RBAC 下建议：

- 菜单显示来自 `/api/admin/index` 的 `menus`。
- 按钮权限来自菜单树里的 `Button` 节点。
- 页面按钮使用 `permissionCode` 或 `ruleCode` 判断，不再使用旧 route name 拼接。

如果短期不改全局按钮鉴权，可先按页面内显式控制：

- 管理员新增：`button:admin.create`
- 管理员编辑：`button:admin.edit`
- 管理员删除：`button:admin.delete`
- 权限组授权：`button:group.rules`
- 规则新增：`button:rule.create`
- API 映射新增：`button:apimap.create`
- Project 超管切换：`button:grant.super`

## 13. 实施顺序

### 阶段 1：RBAC client 和适配器

1. 新增 `src/api/backend/rbac/client.ts`。
2. 支持 `Authorization`、`X-Project`、`code === 0`。
3. 新增类型定义和字段转换函数。
4. 验证能调用 `/api/admin/index`、`/api/admin/list`、`/api/rule/tree`。

验收：

- 成功拿到菜单树。
- 成功拿到管理员分页。
- 失败响应能显示 `msg`。

### 阶段 2：权限组页面

1. 重写 `auth/group` 数据源。
2. 权限树改用 `/api/rule/tree`。
3. `node-key` 改为 `ruleCode`。
4. 提交 `ruleCodes[]`。
5. 所有写操作使用 `groupCode`。

验收：

- 新建权限组。
- 编辑权限组规则。
- 禁用/启用权限组。
- 删除空权限组。

### 阶段 3：管理员页面和 Project 授权

1. 重写 `auth/admin` 列表和表单。
2. 移除旧后台无支持字段。
3. 使用 `userid` 完成编辑、状态、删除。
4. 增加 Project 授权和超管切换入口。
5. 新增 `auth/projectGrant` 或侧边抽屉。

验收：

- 新建用户并加入权限组。
- 授权用户到 `oversia`。
- 切换超管后重新登录/刷新，菜单变化符合预期。
- 撤销授权后用户不能进入当前 project。

### 阶段 4：规则页面

1. 重写 `auth/rule` 列表和树。
2. 使用 `ruleCode` 作为行 key。
3. 表单增加/明确 `ruleCode` 和 `permissionCode`。
4. 类型、状态、菜单类型做双向映射。
5. 排序调用 `/api/rule/{ruleCode}/weigh`。

验收：

- 新建目录、菜单、按钮。
- 按钮必须挂到父规则。
- 权限组能选择新增规则。
- 菜单能通过 `/api/admin/index` 出现在前端。

### 阶段 5：API 映射管理页面

1. 新增 `auth/apiMap`。
2. 接入 `/api/api-map/list`。
3. 实现新增、编辑、删除。
4. `permissionCode` 支持从规则树选择。
5. 对删除和修改做二次确认。

验收：

- 新建 API 路由映射。
- 修改映射的 permissionCode/action。
- 删除测试映射。
- 使用不同权限组用户验证该 API 是否被放行/拒绝。

### 阶段 6：联调回归

1. 使用真实 JWT 和 `X-Project: oversia`。
2. 建规则。
3. 建权限组并授权规则。
4. 建用户并加入权限组。
5. 授权用户进入 Project。
6. 非超管验证只看到授权菜单。
7. 超管验证看到完整菜单。
8. 新增 API 映射后验证 API 鉴权。

## 14. Claude 开发注意事项

- 不要再参考旧计划里的 `{dxeId}`。
- 不要把 `README.md` 里的乱码当作字段名；以代码块、接口路径和本计划字段表为准。
- 不要复用 `baTableApi('/admin/auth.*')` 直接请求 RBAC API。
- 可以复用 Element Plus 表格、弹窗、树、抽屉样式。
- 如果继续用旧 Table 组件，需要确保其 `pk` 支持 `userid` / `groupCode` / `ruleCode`。
- `apiMap` 编辑/删除需要 Guid `id`；如果列表接口没有返回 `id`，先实现只读列表并标记后端缺口。
- 超管是 Project 维度，不是全局管理员字段。
- `permissionCode` 是 API 鉴权和权限组授权的核心，不是展示文案。
- `ruleCode` 是规则操作和权限树选择的核心。
- `groupCode` 是权限组操作和用户授权的核心。

## 15. 后端可能需要补充的接口

目前按 README 可完成大部分页面。但为了更完整的权限管理体验，建议后端后续补充：

1. Project 授权分页查询接口，例如 `GET /api/project-grant/list`，返回 `userid/project/isSuper/grantedBy/createdAt`。
2. API 映射 MySQL 源数据分页接口，返回 `id/httpMethod/routePattern/permissionCode/action/status`，避免 `GET /api/api-map/list` 只有权限视图而无法编辑。
3. 当前用户权限节点查询接口，方便前端统一控制按钮级权限。

这些不是第一阶段阻塞项，但 Claude 开发时需要识别并在实现说明里标记。
