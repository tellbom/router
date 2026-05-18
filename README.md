# RBAC 权限中心 API 文档

本文档根据 `rbac.api/controllers` 当前对外暴露的 Controller 编写，面向前端接入、联调和运维排障使用。

## 基础约定

**业务 API Base URL**: `/api`  
**运维 API Base URL**: `/ops`  
**认证方式**: `Authorization: Bearer <jwt>`  
**项目上下文**: `X-Project: <projectCode>`，例如 `X-Project: oversia`

除 `/ops/*` 外，业务接口的 `project` 均由服务端 `CurrentRbacContext` 解析，前端不要把 `project` 放入 body 作为可信字段。

### 统一响应

业务接口返回统一包装：

```json
{
  "code": 0,
  "msg": "ok",
  "data": {},
  "time": 1715000000
}
```

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `code` | int | `0` 表示成功，非 0 表示业务错误 |
| `msg` | string | 成功通常为 `ok`，失败为可读错误信息 |
| `data` | any/null | 响应数据；无数据时为 `null` |
| `time` | long | 服务端 Unix 秒级时间戳 |

分页接口的 `data` 结构：

```json
{
  "list": [],
  "total": 100
}
```

通用分页参数：

| 参数 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `page` | int | `1` | 页码，从 1 开始 |
| `pageSize` | int | `20` | 每页条数 |
| `keyword` | string | - | 关键字查询 |
| `status` | string | - | 常用值：`Active` / `Disabled` |

常用错误码：

| code | 说明 |
| --- | --- |
| `40001` | 参数校验失败 |
| `40009` | 业务前置条件不满足 |
| `40100` | JWT 用户信息缺失 |
| `40300` | 无权限 |
| `40400` | 资源不存在 |
| `50000` | 运维操作部分或全部失败 |

## 认证与后台初始化

### `POST /api/auth/login`

校验当前 JWT 用户是否可进入当前 project，并返回前端登录态数据。该接口不签发、不刷新 token，只回传请求中的 Bearer token。

请求体：无。  
必要请求头：`Authorization`、`X-Project`。

响应 `data`：

```json
{
  "token": "jwt-token",
  "routePath": "/dashboard",
  "adminInfo": {
    "id": "1234567890123456789",
    "userid": "u001",
    "username": "张三",
    "super": false,
    "project": "oversia"
  }
}
```

### `GET /api/admin/index`

返回后台首页初始化数据，包括当前管理员信息、已按权限裁剪的菜单树和初始跳转路径。

响应 `data`：

```json
{
  "adminInfo": {
    "id": "1234567890123456789",
    "userid": "u001",
    "username": "张三",
    "super": false,
    "project": "oversia"
  },
  "menus": [
    {
      "id": "1234567890123456789",
      "pid": "0",
      "title": "系统管理",
      "name": "system",
      "path": "/system",
      "icon": "fa fa-cog",
      "type": "menu_dir",
      "menu_type": "",
      "url": "",
      "component": "",
      "extend": "",
      "remark": "",
      "keepalive": false,
      "permissionCode": "menu:system",
      "ruleCode": "system",
      "children": []
    }
  ],
  "routePath": "/system"
}
```

## 管理员接口 `/api/admin`

### `GET /api/admin/list`

分页查询管理员列表。

Query 参数：

| 参数 | 类型 | 说明 |
| --- | --- | --- |
| `userid` | string | 按用户 ID 过滤 |
| `groupCode` | string | 按权限组编码过滤 |
| `keyword` | string | 关键字 |
| `status` | string | `Active` / `Disabled` |
| `page` / `pageSize` | int | 分页 |

响应项字段：`userid`、`username`、`status`、`projectCodes`、`groupCodes`、`groupNames`、`superProjects`、`isSuper`。

`isSuper` 表示该用户是否为当前 `X-Project` 的超管；`superProjects` 为该用户拥有超管身份的 project 列表。

### `POST /api/admin`

新增管理员账号，可同时加入权限组。

请求体：

```json
{
  "userid": "u002",
  "username": "李四",
  "groupCode": ["operator"]
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `userid` | string | 是 | 用户业务 ID |
| `username` | string | 是 | 显示名称 |
| `groupCode` | string[] | 否 | 要加入的权限组编码列表 |

响应 `data`：`{ "userid": "u001" }`

### `PUT /api/admin/{userid}`

完整编辑管理员。`null` 字段表示不修改。

请求体：

```json
{
  "username": "新名称",
  "status": "Disabled",
  "groupArr": ["admin", "operator"]
}
```

`groupArr` 为目标权限组全量列表，服务端会对当前成员关系做 diff，自动新增或移除组成员。

### `PUT /api/admin/{userid}/status`

快速启用或禁用管理员。

```json
{ "status": "Disabled" }
```

### `PUT /api/admin/{userid}/username`

快速更新管理员显示名称。

```json
{ "username": "新名称" }
```

### `DELETE /api/admin/{userid}`

删除管理员账号，并清理相关授权关系。

## 权限组接口 `/api/group`

### `GET /api/group/index`

返回 BuildAdmin 兼容的权限组树/选择项。

Query 参数：

| 参数 | 类型 | 说明 |
| --- | --- | --- |
| `select` | bool | `true` 时返回 `options` |
| `isTree` | bool | 兼容参数，当前始终按树组织 |
| `quickSearch` | string | 按组名或组编码过滤 |

普通响应 `data`：

```json
{
  "list": [
    {
      "id": "1234567890123456789",
      "pid": "0",
      "name": "管理员组",
      "rules": "3 permissions",
      "status": "1",
      "update_time": 1715000000,
      "create_time": 1715000000,
      "children": []
    }
  ],
  "total": 1,
  "group": ["1234567890123456789"],
  "remark": "Group hierarchy is for display. Effective access is determined by permission codes."
}
```

`select=true` 响应 `data`：`{ "options": [...] }`

### `GET /api/group/list`

分页查询权限组列表。

Query 参数：

| 参数 | 类型 | 说明 |
| --- | --- | --- |
| `groupCode` | string | 按组编码过滤 |
| `permissionCode` | string | 按权限码过滤 |
| `keyword` | string | 关键字 |
| `status` | string | `Active` / `Disabled` |
| `page` / `pageSize` | int | 分页 |

响应项字段：`groupCode`、`groupName`、`project`、`status`、`permissionCodes`。

### `POST /api/group`

新建权限组。`groupCode` 有默认值 `group_<guid>`，前端也可以显式传入。

请求体：

```json
{
  "groupCode": "operator",
  "groupName": "操作员组",
  "parentGroupCode": "admin",
  "status": "Active",
  "ruleCodes": ["dashboard", "system.user"],
  "extraPermissionCodes": ["menu:system.user", "button:admin.list"]
}
```

说明：前端提交 `ruleCodes`，服务端从启用的规则中推导 `permissionCodes`。`extraPermissionCodes` 为可选字段，用于追加从 APIMap 权限视图选择的端点权限。不传 `extraPermissionCodes` 时行为与旧版一致。支持 `ruleCodes: ["*"]` 表示全部权限。

最终授权权限码：

```text
permissionCodes = ruleCodes 推导值 ∪ extraPermissionCodes
```

响应 `data`：

```json
{
  "groupCode": "operator"
}
```

### `PUT /api/group/{groupCode}`

完整编辑权限组。`null` 字段表示不修改，`parentGroupCode: ""` 表示提升为根组。

```json
{
  "groupName": "新组名",
  "name": "兼容字段，可替代 groupName",
  "parentGroupCode": "",
  "status": "Disabled",
  "ruleCodes": ["dashboard"],
  "extraPermissionCodes": ["button:admin.edit"]
}
```

`extraPermissionCodes` 可选；当传入 `ruleCodes` 时，服务端会把已有权限码、`ruleCodes` 推导值和 `extraPermissionCodes` 做三路 Union。

### `PUT /api/group/{groupCode}/rules`

更新权限组规则授权。

```json
{
  "ruleCodes": ["dashboard", "system.user"],
  "extraPermissionCodes": ["menu:search.audit"]
}
```

注意：当前实现会用新 `ruleCodes` 替换组内规则码，但 `permissionCodes` 会与旧权限码合并。`extraPermissionCodes` 会一并参与合并。

```text
permissionCodes = 已有值 ∪ ruleCodes 推导值 ∪ extraPermissionCodes
```

前端可从 `GET /api/api-map/list` 获取可选的 `permissionCode`，在权限组新建/编辑页中作为 API 端点权限选择器使用。

### `PUT /api/group/{groupCode}/status`

快速启用或禁用权限组。

```json
{ "status": "Disabled" }
```

### `POST /api/group/{groupCode}/members`

将用户加入权限组。

```json
{ "userid": "u001" }
```

### `DELETE /api/group/{groupCode}/members/{userid}`

将用户从权限组移除。

### `DELETE /api/group/{groupCode}`

删除权限组。删除前需满足：

1. 没有子权限组。
2. 组内没有关联用户。
3. 操作人自身不属于该组。

## 菜单/按钮规则接口 `/api/rule`

### `GET /api/rule/tree`

获取当前 project 下的完整菜单/按钮规则树。该接口用于管理端配置和前端菜单构建。

响应节点字段见 `GET /api/admin/index` 的 `menus` 示例。

### `GET /api/rule/list`

分页查询规则列表。

Query 参数：

| 参数 | 类型 | 说明 |
| --- | --- | --- |
| `ruleCode` | string | 按规则码过滤 |
| `permissionCode` | string | 按权限码过滤 |
| `type` | string | `MenuDir` / `Menu` / `Button` |
| `menuType` | string | `Tab` / `Link` / `Iframe` |
| `keyword` | string | 关键字 |
| `status` | string | `Active` / `Disabled` |
| `page` / `pageSize` | int | 分页 |

响应项字段：`ruleCode`、`permissionCode`、`title`、`type`、`status`、`icon`、`remark`、`weigh`。

### `POST /api/rule`

新建菜单目录、菜单或按钮规则。

```json
{
  "ruleCode": "system.user",
  "permissionCode": "menu:system.user",
  "title": "用户管理",
  "type": "Menu",
  "name": "SystemUser",
  "path": "/system/user",
  "icon": "fa fa-user",
  "parentRuleCode": "system",
  "menuType": "Tab",
  "url": "",
  "component": "/src/views/system/user/index.vue",
  "extend": "",
  "remark": "",
  "keepalive": true,
  "weigh": 10
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `ruleCode` | string | 是 | 规则码，project 内唯一 |
| `permissionCode` | string | 是 | 权限码 |
| `title` | string | 是 | 显示标题 |
| `type` | string | 是 | `MenuDir` / `Menu` / `Button` |
| `name` | string | 否 | 前端路由名或按钮权限名；默认取 `ruleCode` |
| `path` | string | 否 | 前端路由路径 |
| `icon` | string | 否 | 图标 |
| `parentRuleCode` | string | Button 必填 | 父规则码 |
| `menuType` | string | 否 | `Tab` / `Link` / `Iframe` |
| `url` | string | 否 | 外链或 iframe URL |
| `component` | string | 否 | 前端组件路径 |
| `extend` | string | 否 | 扩展标记 |
| `remark` | string | 否 | 备注 |
| `keepalive` | bool | 否 | 默认 `false` |
| `weigh` | int | 否 | 默认 `0` |

响应 `data`：`{ "ruleCode": "system.user", "weigh": 10 }`

### `PUT /api/rule/{ruleCode}`

完整编辑规则元数据。`null` 字段表示不修改，`parentRuleCode: ""` 表示提升为根节点。

```json
{
  "title": "新标题",
  "name": "NewName",
  "path": "/new/path",
  "icon": "fa fa-cog",
  "parentRuleCode": "",
  "menuType": "Link",
  "url": "https://example.com",
  "component": "new/component",
  "extend": "fullpage",
  "remark": "备注",
  "keepalive": false,
  "weigh": 20,
  "status": "Active",
  "permissionCode": "menu:new.code"
}
```

### `PUT /api/rule/{ruleCode}/status`

快速启用或禁用规则。

```json
{ "status": "Disabled" }
```

### `PUT /api/rule/{ruleCode}/weigh`

更新规则排序权重。

```json
{ "weigh": 50 }
```

### `DELETE /api/rule/{ruleCode}`

删除规则。

## Project 授权接口 `/api/project-grant`

### `POST /api/project-grant`

将用户授权到当前 project。若授权已存在，则只更新 super 标记。

```json
{
  "userid": "u001",
  "isSuper": false
}
```

### `DELETE /api/project-grant/{userid}`

撤销指定用户在当前 project 的授权。

### `PUT /api/project-grant/{userid}/super`

切换用户在当前 project 下的 super 标记。

```json
{ "isSuper": true }
```

## API 权限映射接口 `/api/api-map`

API 映射用于维护运行时鉴权所需的 `HTTP route -> permissionCode/action` 关系。变更会触发 api-map 缓存失效和版本递增。

前端管理页建议使用 `GET /api/api-map/records` 作为增删改查列表数据源，因为它直接读取 MySQL 真相表并返回 `id`。`GET /api/api-map/list` 是 ES 权限视图，只适合只读展示和搜索，不返回可编辑记录的完整字段。

### `GET /api/api-map/list`

分页查询权限视图，等价于 `/api/search/permission-view` 的业务查询入口。

Query 参数：

| 参数 | 类型 | 说明 |
| --- | --- | --- |
| `permissionCode` | string | 按权限码过滤 |
| `action` | string | 按动作过滤 |
| `resourceType` | string | 按资源类型过滤 |
| `keyword` | string | 关键字 |
| `status` | string | 状态过滤 |
| `page` / `pageSize` | int | 分页 |

响应项字段：`permissionCode`、`action`、`resourceType`、`title`。

### `GET /api/api-map/records`

分页查询 API 映射完整记录。该接口用于 APIMap 管理页的列表、编辑回显和删除定位。

Query 参数：
| 参数 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `keyword` | string | - | 按 `routePattern` 或 `permissionCode` 模糊查询 |
| `status` | string | - | `Active` / `Disabled` |
| `page` | int | `1` | 页码，从 1 开始 |
| `pageSize` | int | `20` | 每页条数，最大 `100` |

响应 `data`：
```json
{
  "list": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "httpMethod": "GET",
      "routePattern": "/api/admin/list",
      "permissionCode": "menu:system.user",
      "action": "read",
      "status": "Active",
      "createdAt": "2026-05-18T12:00:00+00:00",
      "updatedAt": "2026-05-18T12:00:00+00:00"
    }
  ],
  "total": 1
}
```

### `POST /api/api-map`

新增 API 权限映射。新增后会写入 MySQL 真相表，并通过 Outbox 触发当前 project 的 api-map 缓存失效。

```json
{
  "httpMethod": "GET",
  "routePattern": "/api/admin/list",
  "permissionCode": "menu:system.user",
  "action": "read"
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `httpMethod` | string | 是 | 允许 `GET` / `POST` / `PUT` / `DELETE` / `PATCH`，服务端会规范为大写 |
| `routePattern` | string | 是 | ASP.NET Core route template，例如 `/api/admin/{userid}` |
| `permissionCode` | string | 是 | 运行时鉴权使用的权限码 |
| `action` | string | 是 | 允许 `read` / `create` / `update` / `delete` / `execute` / `access`，服务端会规范为小写 |

响应 `data`：`{ "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6" }`

### `PUT /api/api-map/{id}`

更新 API 映射的权限码或动作。`id` 为 `GET /api/api-map/records` 返回的 Guid。当前接口只更新 `permissionCode` 和 `action`；如果需要修改 `httpMethod` 或 `routePattern`，请删除后重新新增。

```json
{
  "permissionCode": "menu:system.user.edit",
  "action": "update"
}
```

| 字段 | 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `permissionCode` | string | 否 | 为 `null` 时保持原值 |
| `action` | string | 否 | 为 `null` 时保持原值；允许值同新增接口 |

成功响应：`data` 为 `null`。

### `DELETE /api/api-map/{id}`

删除 API 权限映射。

`id` 为 `GET /api/api-map/records` 返回的 Guid。删除后会写入 Outbox，触发当前 project 的 api-map 缓存失效和版本递增。

## 查询接口 `/api/search`

只读查询接口，不产生写操作和 Outbox 事件。

### `GET /api/search/audit-logs`

查询鉴权审计日志。

Query 参数：

| 参数 | 类型 | 说明 |
| --- | --- | --- |
| `userid` | string | 按用户过滤 |
| `permissionCode` | string | 按权限码过滤 |
| `result` | string | 鉴权结果，如 `Allow` / `Deny` / `Error` |
| `httpMethod` | string | HTTP 方法 |
| `createdAtFrom` | ISO8601 | 起始时间 |
| `createdAtTo` | ISO8601 | 结束时间 |
| `keyword` | string | 关键字 |
| `status` | string | 兼容通用查询字段 |
| `page` / `pageSize` | int | 分页 |

响应项字段：`auditId`、`userid`、`project`、`permissionCode`、`result`、`reason`、`createdAt`。

### `GET /api/search/permission-view`

查询 API 到权限码的权限视图。

Query 参数：`permissionCode`、`action`、`resourceType`、`keyword`、`status`、`page`、`pageSize`。

响应项字段：`permissionCode`、`action`、`resourceType`、`title`。

## 运维接口 `/ops`

运维接口不在 `/api` 下，需要通过 `X-Ops-Key` 鉴权。

### `POST /ops/reindex`

重建 Elasticsearch 索引。

Query 参数：

| 参数 | 类型 | 说明 |
| --- | --- | --- |
| `project` | string | 可选；指定 project，不传则全部 |
| `index` | string | 可选；指定索引别名，不传则重建全部 |

支持的 `index`：`rbac_user_index`、`rbac_group_index`、`rbac_rule_index`、`rbac_permission_view_index`、`rbac_audit_log_index`。

### `POST /ops/cache-flush`

清理菜单树、API 映射缓存并递增 project 版本。

Query 参数：`project` 可选，不传则全部 project。

### `POST /ops/outbox-retry`

将失败的 Outbox 事件重置为待处理。

Query 参数：`project` 可选，不传则全部 project。

### `GET /ops/health`

检查 Outbox 和 Elasticsearch 索引状态。

Query 参数：`project` 可选。

## 接口总览

| 模块 | 方法 | 路径 | 说明 |
| --- | --- | --- | --- |
| 认证 | POST | `/api/auth/login` | 校验当前 JWT 用户能否进入 project |
| 初始化 | GET | `/api/admin/index` | 后台初始化 |
| 管理员 | GET | `/api/admin/list` | 分页列表 |
| 管理员 | POST | `/api/admin` | 新建管理员 |
| 管理员 | PUT | `/api/admin/{userid}` | 完整编辑 |
| 管理员 | PUT | `/api/admin/{userid}/status` | 切换状态 |
| 管理员 | PUT | `/api/admin/{userid}/username` | 更新名称 |
| 管理员 | DELETE | `/api/admin/{userid}` | 删除管理员 |
| 权限组 | GET | `/api/group/index` | BuildAdmin 兼容树/选项 |
| 权限组 | GET | `/api/group/list` | 分页列表 |
| 权限组 | POST | `/api/group` | 新建权限组 |
| 权限组 | PUT | `/api/group/{groupCode}` | 完整编辑 |
| 权限组 | PUT | `/api/group/{groupCode}/rules` | 更新规则授权 |
| 权限组 | PUT | `/api/group/{groupCode}/status` | 切换状态 |
| 权限组 | POST | `/api/group/{groupCode}/members` | 添加成员 |
| 权限组 | DELETE | `/api/group/{groupCode}/members/{userid}` | 移除成员 |
| 权限组 | DELETE | `/api/group/{groupCode}` | 删除权限组 |
| 规则 | GET | `/api/rule/tree` | 完整规则树 |
| 规则 | GET | `/api/rule/list` | 分页列表 |
| 规则 | POST | `/api/rule` | 新建规则 |
| 规则 | PUT | `/api/rule/{ruleCode}` | 完整编辑 |
| 规则 | PUT | `/api/rule/{ruleCode}/status` | 切换状态 |
| 规则 | PUT | `/api/rule/{ruleCode}/weigh` | 更新排序 |
| 规则 | DELETE | `/api/rule/{ruleCode}` | 删除规则 |
| Project 授权 | POST | `/api/project-grant` | 授权用户 |
| Project 授权 | DELETE | `/api/project-grant/{userid}` | 撤销授权 |
| Project 授权 | PUT | `/api/project-grant/{userid}/super` | 切换 super |
| API 映射 | GET | `/api/api-map/list` | 权限视图列表 |
| API 映射 | GET | `/api/api-map/records` | 完整映射记录列表 |
| API 映射 | POST | `/api/api-map` | 新建映射 |
| API 映射 | PUT | `/api/api-map/{id}` | 更新映射 |
| API 映射 | DELETE | `/api/api-map/{id}` | 删除映射 |
| 查询 | GET | `/api/search/audit-logs` | 审计日志 |
| 查询 | GET | `/api/search/permission-view` | 权限视图 |
| 运维 | POST | `/ops/reindex` | 重建 ES 索引 |
| 运维 | POST | `/ops/cache-flush` | 清理缓存 |
| 运维 | POST | `/ops/outbox-retry` | 重试 Outbox |
| 运维 | GET | `/ops/health` | 健康检查 |
