# oversia 权限中心前端接入改造计划

## 1. 分析范围

- 前端项目：`E:\Web\oversia`
- 重点目录：`src\views\backend\auth`
- 本次不分析：`src\views\backend\auth\adminLog`
- 当前权限中心后端项目：`E:\router\router`
- 目标：判断 `auth/admin`、`auth/group`、`auth/rule` 是否能直接适配现有 RBAC API，以及是否需要重写页面。

## 2. 总体结论

`src\views\backend\auth` 当前仍是 BuildAdmin 后台权限页形态，核心依赖：

- `baTableApi('/admin/auth.Admin/')`
- `baTableApi('/admin/auth.Group/')`
- `baTableApi('/admin/auth.Rule/')`
- `getAdminRules()` 调用 `/admin/auth.Rule/index`
- 表单字段使用旧模型：`id`、`group_arr`、`rules`、`pid`、`menu_type`、`menu_dir`、`nickname`、`avatar`、`email`、`mobile` 等。

现有权限中心 API 已经转为：

- 用户：`/api/admin`
- 权限组：`/api/group`
- 路由/按钮规则：`/api/rule`
- 权限树：`/api/rule/tree`
- 认证上下文：`Authorization: Bearer <jwt>` + `X-Project: oversia`
- 主要业务标识：`dxeId`、`userid`、`groupCode`、`ruleCode`、`permissionCode`

因此，auth 目录不能只替换请求路径直接复用。建议采用“业务适配重写”，而不是全量视觉重写：保留后台页面位置、Element Plus 交互习惯和部分表格/抽屉外观，重写 API 适配层、表单字段、树选择逻辑、状态/类型映射。

## 3. claudetable 组件是否可依赖

`E:\Web\oversia\src\components\claudetable` 下已有：

- `Commontable.vue`：通用表格、分页、列显示、操作列插槽。
- `Commonsearch.vue`：通用搜索表单。
- `EditorDrawer.vue`：通用抽屉表单，适合简单字段 schema。
- `Treeutils.js`：树工具。
- `Attachment*`：附件相关，与权限页关系弱。

判断：

- 可以依赖 `Commontable.vue` 和 `Commonsearch.vue` 承接用户、权限组、规则列表。
- 可以部分依赖 `EditorDrawer.vue` 承接用户新增/编辑这类简单表单。
- 不建议直接用 `EditorDrawer.vue` 承接权限组规则树和菜单规则编辑的全部表单，因为权限树存在半选节点、`ruleCode` 提交、父子关系、按钮节点等特殊逻辑。
- `claudetable` 组件本身不提供 RBAC API 适配能力，仍需要新增专用 service/composable 做字段转换。

结论：如果重写页面，可以参考并复用 `/claudetable` 的表格/搜索/抽屉模式，但不能把它视为完整替代方案。

## 4. 后端 API 当前契约

### 4.1 用户

- `GET /api/admin/list`
- `POST /api/admin`
- `PUT /api/admin/{dxeId}`
- `PUT /api/admin/{dxeId}/status`
- `PUT /api/admin/{dxeId}/username`
- `DELETE /api/admin/{dxeId}`

新增用户当前请求重点字段：

```json
{
  "userid": "196045",
  "username": "张三",
  "groupCode": ["group_xxx"]
}
```

完整编辑当前请求重点字段：

```json
{
  "username": "张三",
  "status": "Active",
  "groupArr": ["group_xxx"]
}
```

### 4.2 权限组

- `GET /api/group/index`
- `GET /api/group/list`
- `POST /api/group`
- `PUT /api/group/{dxeId}`
- `PUT /api/group/{dxeId}/rules`
- `PUT /api/group/{dxeId}/status`
- `DELETE /api/group/{dxeId}`

新增权限组当前后端 DTO 已有默认 `GroupCode = group_<guid>`，前端可只传：

```json
{
  "groupName": "测试巡察组",
  "status": "1",
  "ruleCodes": ["dashboard", "dashboard/index"]
}
```

编辑权限组建议传：

```json
{
  "groupName": "测试巡察组",
  "status": "1",
  "ruleCodes": ["dashboard", "dashboard/index"]
}
```

注意：后端会基于 `ruleCodes` 推导 `permissionCodes`，前端不应提交 `permissionCodes`。

### 4.3 路由/按钮规则

- `GET /api/rule/tree`
- `GET /api/rule/list`
- `POST /api/rule`
- `PUT /api/rule/{dxeId}`
- `PUT /api/rule/{dxeId}/status`
- `PUT /api/rule/{dxeId}/weigh`
- `DELETE /api/rule/{dxeId}`

创建规则当前核心字段：

```json
{
  "ruleCode": "dashboard/index",
  "permissionCode": "menu:dashboard/index",
  "title": "工作台",
  "type": "Menu",
  "name": "dashboard/index",
  "path": "/dashboard/index",
  "parentRuleCode": "dashboard",
  "menuType": "Tab",
  "component": "/src/views/backend/dashboard.vue",
  "icon": "fa fa-dashboard",
  "remark": "工作台入口",
  "keepalive": false,
  "weigh": 10
}
```

按钮规则必须传 `parentRuleCode`。

## 5. 页面逐项适配判断

### 5.1 `auth/admin`

现状：

- 列表字段包含 `nickname`、`avatar`、`email`、`mobile`、`last_login_time` 等。
- 新增/编辑表单包含 `username`、`nickname`、`group_arr`、`password`、`motto` 等。
- 远程权限组选择指向 `/admin/auth.Group/index`。
- 弹窗里有模拟组织/人员选择数据，但没有和当前 RBAC 用户新增接口形成真实映射。

与当前 API 差异：

- 当前后端核心字段是 `userid`、`username`、`groupCode/groupArr`、`status`。
- 后端不管理密码、头像、邮箱、手机、座右铭、最后登录时间。
- `group_arr` 需要转换为 `groupCode[]` 或 `groupArr[]`。

建议：

- 需要重写业务表单，但不必重写整页视觉。
- 用户列表只保留当前 RBAC 有真实来源的列：`userid`、`username`、`groupNames/groupCodes`、`status`。
- 新增表单只保留：`userid`、`username`、`groupCode[]`。
- 编辑表单只保留：`username`、`status`、`groupArr[]`。
- 如果后续要接入通讯录选人，选人结果必须显式映射：人员编号 -> `userid`，人员名称 -> `username`。

### 5.2 `auth/group`

现状：

- 列表接口使用 `/admin/auth.Group/`。
- 权限树接口使用 `/admin/auth.Rule/index`。
- 表单树 `node-key="id"`，提交 `rules`，当前逻辑会把选中节点的 `id` 传回后端。

与当前 API 差异：

- 当前后端要求提交 `ruleCodes[]`，不是旧的数字规则 ID，也不是 `dxeId[]`。
- `/api/rule/tree` 返回节点同时包含 `id` 和 `ruleCode`，其中 `id` 是操作用 `dxeId`，不是权限授权依据。
- 当前权限组接口会由 `ruleCodes` 推导 `permissionCodes`。

建议：

- 权限组页面需要改造树选择逻辑。
- 权限树节点 key 建议直接改为 `ruleCode`。
- 如果 UI 仍需用 `id` 做编辑/删除，则维护 `id -> ruleCode` 映射，但提交时必须转换为 `ruleCode[]`。
- 半选父级是否提交应由前端产品策略决定；当前后端只按前端传入的 `ruleCodes` 裁剪，不自动补父级。
- 新增/编辑权限组统一提交 `groupName`、`status`、`ruleCodes`。

### 5.3 `auth/rule`

现状：

- 列表接口使用 `/admin/auth.Rule/`。
- 表单字段使用 `pid`、`type=menu_dir/menu/button`、`menu_type=tab/link/iframe`、`name`、`path`、`component`、`extend`、`remark`、`keepalive`。
- 父节点选择使用旧 `id/pid` 模型。

与当前 API 差异：

- 当前后端创建需要 `ruleCode` 和 `permissionCode`。
- 当前后端父节点字段是 `parentRuleCode`。
- 当前后端 `CreateRuleRequest.Type` 使用枚举解析，`menu_dir` 不能直接作为 `MenuDir` 解析。
- `/api/rule/tree` 当前 `type` 从后端转换为小写枚举名，`MenuDir` 会输出 `menudir`，和旧前端 `menu_dir` 不完全一致。
- `status` 后端使用 `Active/Disabled`，旧前端使用 `1/0`。
- `keepalive` 后端是 bool，旧前端使用 `1/0`。

建议：

- 规则页是三块里改造最重的页面。
- 建议新增前端适配函数：
  - `menu_dir` <-> `MenuDir`
  - `menu` <-> `Menu`
  - `button` <-> `Button`
  - `tab/link/iframe` <-> `Tab/Link/Iframe`
  - `1/0` <-> `Active/Disabled`
  - `1/0` <-> `true/false`
- 父节点提交必须使用 `parentRuleCode`。
- 前端应新增或明确 `ruleCode` 字段。若沿用旧 `name`，则需要固定规则：`ruleCode = name`。
- `permissionCode` 建议由前端适配层自动派生：
  - 菜单/目录：`menu:${ruleCode}`
  - 按钮：`button:${ruleCode}`
- 删除、编辑、排序继续使用 `dxeId`。

## 6. 推荐改造方案

### 6.1 新增 RBAC API 适配层

建议在 oversia 中新增：

```text
src/api/backend/rbac/admin.ts
src/api/backend/rbac/group.ts
src/api/backend/rbac/rule.ts
src/api/backend/rbac/adapter.ts
```

职责：

- 统一调用 `/api/admin`、`/api/group`、`/api/rule`。
- 注入或沿用全局请求中的 `Authorization`、`X-Project`。
- 将后端响应转换成页面可用模型。
- 将页面表单转换成后端请求模型。

不建议直接改全局 `baTableApi`，避免影响 oversia 其他仍使用 BuildAdmin 老接口的页面。

### 6.2 页面保留与重写边界

建议：

- `auth/admin/index.vue`：保留页面入口，重写数据源和列配置。
- `auth/admin/popupForm.vue`：建议简化重写，移除当前后端不支持字段。
- `auth/group/index.vue`：保留页面入口，重写接口和提交逻辑。
- `auth/group/popupForm.vue`：重点重写权限树，树 key 改 `ruleCode`。
- `auth/rule/index.vue`：保留页面入口，重写接口、字段映射、类型/状态展示。
- `auth/rule/popupForm.vue`：重写表单提交模型，父节点从 `pid` 改为 `parentRuleCode`。

### 6.3 是否使用 claudetable

推荐策略：

- 新页面如果追求快速稳定，可以直接用 `Commontable + Commonsearch` 重建列表。
- 用户页适合使用 `EditorDrawer`。
- 权限组和规则页建议使用自定义抽屉/弹窗，因为权限树、父节点、按钮规则字段联动比较强。
- 如果继续保留 BuildAdmin 的 `TableHeader/Table/PopupForm`，则必须额外实现一个兼容 `baTableClass` 的 API 包装层，复杂度不一定更低。

## 7. 分阶段实施计划

### 阶段 1：契约适配层

1. 新增 RBAC API service。
2. 新增状态、类型、菜单类型、keepalive 转换函数。
3. 新增树扁平化函数，保留 `dxeId`、`ruleCode`、`parentRuleCode` 映射。
4. 统一处理响应结构：`ApiResponse<T>.data`。

验收：

- 能在前端拿到 `/api/rule/tree` 并转换为页面树。
- 能在前端拿到 `/api/group/index` 并转换为权限组列表/选择项。
- 能在前端拿到 `/api/admin/list` 并转换为用户列表。

### 阶段 2：权限组页面

1. 改造权限组列表读取 `/api/group/index`。
2. 改造权限树读取 `/api/rule/tree`。
3. 权限树 `node-key` 改为 `ruleCode`。
4. 新增权限组提交 `groupName/status/ruleCodes`。
5. 编辑权限组提交 `groupName/status/ruleCodes`。
6. 删除权限组调用 `DELETE /api/group/{dxeId}`。

优先做权限组，因为用户授权依赖它。

### 阶段 3：用户页面

1. 用户列表改读 `/api/admin/list`。
2. 用户新增只提交 `userid/username/groupCode`。
3. 用户编辑提交 `username/status/groupArr`。
4. 删除用户调用 `DELETE /api/admin/{dxeId}`。
5. 移除或隐藏后端无真实字段的头像、邮箱、手机、密码、座右铭、最后登录时间。

### 阶段 4：规则页面

1. 规则树/列表改读 `/api/rule/tree` 或 `/api/rule/list`。
2. 新增规则时把旧表单字段转换为后端字段。
3. 编辑规则时使用 `PUT /api/rule/{dxeId}`。
4. 删除规则时使用 `DELETE /api/rule/{dxeId}`。
5. 排序时使用 `PUT /api/rule/{dxeId}/weigh`。
6. 状态切换使用 `PUT /api/rule/{dxeId}/status`。

规则页建议最后做，因为它对权限组树和前端菜单生成都有影响。

### 阶段 5：联调验证

1. 使用 `X-Project: oversia` 和有效 JWT。
2. 新建菜单目录、菜单、按钮。
3. 新建权限组，只选择部分 `ruleCode`，确认同级未越权。
4. 新建用户并绑定权限组。
5. 调用 `/api/auth/login` 或 `/api/admin/index` 验证登录态菜单。
6. 更新权限组后验证菜单缓存和 ES/Redis 是否同步。
7. 删除测试用户、测试权限组、测试规则。

## 8. 风险与注意事项

- 当前 BuildAdmin 页面直接依赖 `/admin/auth.*`，不能直接接入当前 `/api/*`。
- 前端不要提交 `permissionCodes`，只提交 `ruleCodes`。
- `id` 是 `dxeId`，用于编辑/删除；`ruleCode` 才是权限组授权依据。
- 规则类型 `menu_dir` 与后端 `MenuDir` 存在命名差异，必须转换。
- 当前后端树 DTO 注释期望 `menu_dir`，但实际映射里 `RuleType.ToString().ToLowerInvariant()` 会生成 `menudir`，前端适配层需要兼容两种值。
- 旧用户页的密码、头像、邮箱、手机等字段不是当前 RBAC 后端职责，不应强塞进本次权限中心接入。
- 权限组半选父级是否提交不是后端自动补齐逻辑，必须在前端产品层明确。
- 修改规则/权限组后可能存在缓存延迟，联调时必要时调用运维 reindex/cache-flush。

## 9. 最终建议

建议不要把 `src\views\backend\auth` 当作简单接口替换任务处理。正确边界是：

- 后端继续保持当前 RBAC 领域模型：`ruleCode` 输入、`permissionCode` 后端推导或规则创建时明确。
- 前端新增 RBAC 适配层，隔离 BuildAdmin 老字段。
- 页面视觉可以复用现有后台风格，列表/搜索优先复用 `claudetable`。
- 权限组树和规则表单必须按当前权限中心模型重写。

推荐实施顺序：权限组 -> 用户 -> 规则。这样可以先解决“给用户分组授权”的闭环，再处理菜单规则维护。
