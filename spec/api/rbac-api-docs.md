# RBAC 权限中心 API 文档

**Base URL**: `/api`  
**认证方式**: Bearer JWT（`Authorization: Bearer <token>`）  
**Project 传递**: 请求头 `X-Project: <projectCode>`（或路由参数 / Query 参数，按服务配置）

---

## 通用约定

### 响应体结构

所有接口均返回以下统一包装结构：

```json
{
  "code": 0,
  "msg": "ok",
  "data": {},
  "time": 1715000000
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `code` | int | `0` = 成功；其他值 = 业务错误 |
| `msg` | string | 成功时为 `"ok"`，失败时为可读错误描述 |
| `data` | any / null | 响应数据，失败时为 `null` |
| `time` | long | 服务端 Unix 时间戳（秒） |

### 分页响应结构

列表接口的 `data` 字段为：

```json
{
  "list": [],
  "total": 100
}
```

### 分页请求参数（Query）

所有列表接口均支持以下公共参数：

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `page` | int | `1` | 页码，从 1 开始 |
| `pageSize` | int | `20` | 每页条数，最大 100 |
| `keyword` | string | - | 关键词模糊搜索 |
| `status` | string | - | 状态过滤：`Active` / `Disabled` |

> **注意**：`project` 参数由服务端从 `CurrentRbacContext` 注入，前端无需传入（传了也会被覆盖）。

### DxEId 说明

所有实体的业务 ID（`dxeId`）**始终以 string 返回**，前端不得将其作为 number 处理（雪花 ID 超过 JS 安全整数范围）。

### 错误码说明

| code | 含义 |
|------|------|
| `0` | 成功 |
| `40001` | 参数校验失败 |
| `40009` | 业务前置条件不满足 |
| `40400` | 资源不存在 |
| `40300` | 无权限 |

---

## 1. 后台初始化

### `GET /api/admin/index`

返回当前登录用户的后台首页初始化数据，包括个人信息、菜单树、前端路由表。

**响应 `data`**:

```json
{
  "adminInfo": {
    "userid": "u001",
    "username": "张三",
    "isSuper": false
  },
  "menus": [
    {
      "ruleCode": "system",
      "title": "系统管理",
      "path": "/system",
      "component": "Layout",
      "children": []
    }
  ],
  "routePath": ["/system/user", "/system/group"]
}
```

---

## 2. 管理员管理 `/api/admin`

### `GET /api/admin/list`

分页查询管理员列表（ES）。

**Query 参数**:

| 参数 | 类型 | 说明 |
|------|------|------|
| `userid` | string | 按 userid 过滤 |
| `groupCode` | string | 按权限组过滤 |
| `keyword` | string | 关键词（userid / username 模糊匹配） |
| `status` | string | `Active` / `Disabled` |
| `page` / `pageSize` | int | 分页 |

**响应 `data`**:

```json
{
  "list": [
    {
      "dxeId": "1234567890123456789",
      "userid": "u001",
      "username": "张三",
      "status": "Active",
      "groupCodes": ["admin", "operator"]
    }
  ],
  "total": 50
}
```

---

### `POST /api/admin`

新增管理员账号。

**请求体**:

```json
{
  "userid": "u002",
  "username": "李四"
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `userid` | string | ✅ | 用户业务 ID（来自公司门户 / JWT） |
| `username` | string | ✅ | 显示名称 |

**响应 `data`**:

```json
{ "dxeId": "1234567890123456789" }
```

---

### `PUT /api/admin/{dxeId}`

完整编辑管理员（username、status、所属权限组）。所有字段为可选，`null` 表示不修改。

**Path 参数**: `dxeId` — 管理员的业务 ID（string）

**请求体**:

```json
{
  "username": "新名称",
  "status": "Disabled",
  "groupArr": ["groupA", "groupB"]
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `username` | string | ❌ | 新的显示名称 |
| `status` | string | ❌ | `Active` / `Disabled` |
| `groupArr` | string[] | ❌ | 目标权限组 groupCode 列表（全量替换，diff 处理） |

> `groupArr` 传入时会与当前成员记录做 diff：新增 → 调用加入逻辑；移除 → 调用退出逻辑；同时产生 `PolicyChanged` + `GroupChanged` Outbox 事件。

**响应 `data`**: `null`

---

### `PUT /api/admin/{dxeId}/status`

快捷变更管理员状态。

**请求体**:

```json
{ "status": "Disabled" }
```

| 字段 | 类型 | 必填 | 枚举值 |
|------|------|------|--------|
| `status` | string | ✅ | `Active` / `Disabled` |

**响应 `data`**: `null`

---

### `PUT /api/admin/{dxeId}/username`

快捷更新管理员显示名称。

**请求体**:

```json
{ "username": "新名称" }
```

**响应 `data`**: `null`

---

### `DELETE /api/admin/{dxeId}`

物理删除管理员账号。

> 删除时服务端自动清理该用户在当前 project 的所有权限组成员记录，并产生对应 `PolicyChanged` + `GroupChanged` + `UserChanged` Outbox 事件。

**响应 `data`**: `null`

---

## 3. 权限组管理 `/api/group`

### `GET /api/group/list`

分页查询权限组列表（ES）。

**Query 参数**:

| 参数 | 类型 | 说明 |
|------|------|------|
| `groupCode` | string | 按 groupCode 过滤 |
| `permissionCode` | string | 按权限码过滤 |
| `keyword` | string | 关键词模糊搜索 |
| `status` | string | `Active` / `Disabled` |
| `page` / `pageSize` | int | 分页 |

**响应 `data`**:

```json
{
  "list": [
    {
      "dxeId": "1234567890123456789",
      "groupCode": "admin",
      "groupName": "管理员组",
      "parentGroupCode": null,
      "project": "default",
      "status": "Active",
      "ruleCodes": ["system.user", "system.group"],
      "permissionCodes": ["menu:system.user", "button:system.user.add"]
    }
  ],
  "total": 10
}
```

---

### `POST /api/group`

新建权限组。

**请求体**:

```json
{
  "groupCode": "operator",
  "groupName": "操作员组",
  "parentGroupCode": "admin"
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `groupCode` | string | ✅ | 组编码（project 内唯一） |
| `groupName` | string | ✅ | 显示名称 |
| `parentGroupCode` | string | ❌ | 父组编码，不填为根组 |

**响应 `data`**:

```json
{ "dxeId": "1234567890123456789" }
```

---

### `PUT /api/group/{dxeId}`

完整编辑权限组。所有字段为可选，`null` 表示不修改。

**请求体**:

```json
{
  "groupName": "新名称",
  "parentGroupCode": "",
  "status": "Active",
  "ruleCodes": ["system.user"],
  "permissionCodes": ["menu:system.user"],
  "affectedUserids": ["u001", "u002"]
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `groupName` | string | ❌ | 新名称 |
| `parentGroupCode` | string | ❌ | 父组编码；传空字符串 `""` 表示提升为根组 |
| `status` | string | ❌ | `Active` / `Disabled` |
| `ruleCodes` | string[] | ❌ | 规则码列表（全量替换） |
| `permissionCodes` | string[] | ❌ | 权限码列表（全量替换，变化时触发 PolicyChanged） |
| `affectedUserids` | string[] | ❌ | 受影响用户列表，用于 Outbox 事件 payload |

**响应 `data`**: `null`

---

### `PUT /api/group/{dxeId}/rules`

快捷更新权限组的 ruleCodes + permissionCodes。

**请求体**:

```json
{
  "ruleCodes": ["system.user", "system.log"],
  "permissionCodes": ["menu:system.user", "menu:system.log"],
  "affectedUserids": ["u001"]
}
```

**响应 `data`**: `null`

---

### `PUT /api/group/{dxeId}/status`

快捷变更权限组状态。

**请求体**:

```json
{
  "status": "Disabled",
  "affectedUserids": ["u001", "u002"]
}
```

**响应 `data`**: `null`

---

### `POST /api/group/{dxeId}/members`

将用户加入权限组。

**请求体**:

```json
{ "userid": "u001" }
```

**响应 `data`**: `null`

---

### `DELETE /api/group/{dxeId}/members/{userid}`

将用户从权限组移除。

**Path 参数**: `dxeId` — 权限组 dxeId；`userid` — 用户 userid

**响应 `data`**: `null`

---

### `DELETE /api/group/{dxeId}`

物理删除权限组。

**删除前置校验**（任一不满足返回 `40009`）：

1. 不存在子组（需先删除或迁移子组）
2. 组内无关联用户（需先移除所有成员）
3. 操作者自身不属于该组

**响应 `data`**: `null`

---

## 4. 菜单/按钮规则管理 `/api/rule`

### `GET /api/rule/tree`

获取当前 project 下的完整菜单树（供管理端配置使用，非运行态菜单）。

**响应 `data`**:

```json
[
  {
    "dxeId": "1234567890123456789",
    "ruleCode": "system",
    "title": "系统管理",
    "type": "MenuDir",
    "path": "/system",
    "permissionCode": "menu:system",
    "status": "Active",
    "weigh": 0,
    "children": [
      {
        "ruleCode": "system.user",
        "title": "用户管理",
        "type": "Menu",
        "children": []
      }
    ]
  }
]
```

---

### `GET /api/rule/list`

分页查询规则列表（ES）。

**Query 参数**:

| 参数 | 类型 | 说明 |
|------|------|------|
| `ruleCode` | string | 按 ruleCode 过滤 |
| `permissionCode` | string | 按权限码过滤 |
| `type` | string | `MenuDir` / `Menu` / `Button` |
| `menuType` | string | `Tab` / `Link` / `Iframe` |
| `keyword` | string | 关键词 |
| `status` | string | `Active` / `Disabled` |
| `page` / `pageSize` | int | 分页 |

---

### `POST /api/rule`

新建菜单或按钮规则。

**请求体**:

```json
{
  "ruleCode": "system.user",
  "permissionCode": "menu:system.user",
  "title": "用户管理",
  "type": "Menu",
  "name": "SystemUser",
  "path": "/system/user",
  "parentRuleCode": "system",
  "menuType": "Tab",
  "component": "system/user/index",
  "keepalive": true,
  "weigh": 10
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `ruleCode` | string | ✅ | 规则码（project 内唯一） |
| `permissionCode` | string | ✅ | 权限码 |
| `title` | string | ✅ | 显示标题 |
| `type` | string | ✅ | `MenuDir` / `Menu` / `Button` |
| `name` | string | ❌ | 前端路由 name |
| `path` | string | ❌ | 前端路由 path |
| `parentRuleCode` | string | ❌（Button 必填）| 父规则码 |
| `menuType` | string | ❌ | `Tab` / `Link` / `Iframe` |
| `url` | string | ❌ | 外链 / iframe URL |
| `component` | string | ❌ | 前端组件路径 |
| `extend` | string | ❌ | 扩展标记 |
| `keepalive` | bool | ❌ | 是否开启路由缓存，默认 `false` |
| `weigh` | int | ❌ | 排序权重，默认 `0` |

**响应 `data`**:

```json
{ "dxeId": "1234567890123456789" }
```

---

### `PUT /api/rule/{dxeId}`

完整编辑规则元数据。所有字段为可选，`null` 表示不修改。

**请求体**:

```json
{
  "title": "新标题",
  "name": "NewName",
  "path": "/new/path",
  "parentRuleCode": "",
  "menuType": "Link",
  "url": "https://example.com",
  "component": "new/component",
  "extend": "fullpage",
  "keepalive": false,
  "weigh": 20,
  "status": "Active",
  "permissionCode": "menu:new.code"
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `title` | string | ❌ | |
| `name` | string | ❌ | |
| `path` | string | ❌ | |
| `parentRuleCode` | string | ❌ | 空字符串表示提升为根节点 |
| `menuType` | string | ❌ | `Tab` / `Link` / `Iframe` |
| `url` | string | ❌ | |
| `component` | string | ❌ | |
| `extend` | string | ❌ | |
| `keepalive` | bool | ❌ | |
| `weigh` | int | ❌ | |
| `status` | string | ❌ | `Active` / `Disabled` |
| `permissionCode` | string | ❌ | 变更时同时更新 api-map 缓存 |

**响应 `data`**: `null`

---

### `PUT /api/rule/{dxeId}/status`

快捷变更规则状态。

**请求体**:

```json
{ "status": "Disabled" }
```

**响应 `data`**: `null`

---

### `PUT /api/rule/{dxeId}/weigh`

更新规则排序权重（拖拽排序时逐条调用）。

**请求体**:

```json
{ "weigh": 50 }
```

**响应 `data`**: `null`

---

### `DELETE /api/rule/{dxeId}`

物理删除规则。

**响应 `data`**: `null`

---

## 5. Project 授权管理 `/api/project-grant`

### `POST /api/project-grant`

将用户授权到当前 project（若已存在则更新 super 标志）。

**请求体**:

```json
{
  "userid": "u001",
  "isSuper": false
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `userid` | string | ✅ | 用户 userid |
| `isSuper` | bool | ❌ | 是否赋予超级权限，默认 `false` |

**响应 `data`**: `null`

---

### `DELETE /api/project-grant/{userid}`

撤销指定用户在当前 project 的授权。

**Path 参数**: `userid` — 用户 userid

**响应 `data`**: `null`

---

### `PUT /api/project-grant/{userid}/super`

快捷切换用户的 super 状态。

**请求体**:

```json
{ "isSuper": true }
```

**响应 `data`**: `null`

---

## 6. API 权限映射管理 `/api/api-map`

路由 → permissionCode 的映射关系，供运行时鉴权使用。

### `GET /api/api-map/list`

分页查询权限视图（ES）。

**Query 参数**:

| 参数 | 类型 | 说明 |
|------|------|------|
| `permissionCode` | string | 按权限码过滤 |
| `action` | string | 按 action 过滤 |
| `resourceType` | string | 资源类型过滤 |
| `keyword` | string | 关键词 |
| `page` / `pageSize` | int | 分页 |

---

### `POST /api/api-map`

新增 API 路由权限映射。

**请求体**:

```json
{
  "httpMethod": "GET",
  "routePattern": "/api/admin/list",
  "permissionCode": "menu:system.user",
  "action": "access"
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `httpMethod` | string | ✅ | `GET` / `POST` / `PUT` / `DELETE` 等 |
| `routePattern` | string | ✅ | 路由模板，支持 `{param}` 占位符 |
| `permissionCode` | string | ✅ | 对应的权限码 |
| `action` | string | ✅ | 动作标识，如 `access` / `read` / `write` |

**响应 `data`**:

```json
{ "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6" }
```

---

### `PUT /api/api-map/{id}`

更新 API 路由权限映射。

**Path 参数**: `id` — 映射记录的 Guid

**请求体**（null 字段不修改）:

```json
{
  "permissionCode": "menu:system.user.edit",
  "action": "write"
}
```

**响应 `data`**: `null`

---

### `DELETE /api/api-map/{id}`

删除 API 路由权限映射。

**Path 参数**: `id` — 映射记录的 Guid

**响应 `data`**: `null`

---

## 7. 查询接口 `/api/search`

只读，不产生任何写操作。

### `GET /api/search/audit-logs`

查询鉴权审计日志（ES）。

**Query 参数**:

| 参数 | 类型 | 说明 |
|------|------|------|
| `userid` | string | 按用户过滤 |
| `permissionCode` | string | 按权限码过滤 |
| `result` | string | 鉴权结果：`Allow` / `Deny` / `Error` |
| `httpMethod` | string | HTTP 方法 |
| `createdAtFrom` | ISO8601 | 时间范围起始 |
| `createdAtTo` | ISO8601 | 时间范围结束 |
| `keyword` | string | 关键词 |
| `page` / `pageSize` | int | 分页 |

**响应 `data`**:

```json
{
  "list": [
    {
      "auditId": "uuid",
      "userid": "u001",
      "project": "default",
      "permissionCode": "menu:system.user",
      "action": "access",
      "result": "Allow",
      "httpMethod": "GET",
      "requestPath": "/api/admin/list",
      "createdAt": "2026-05-10T08:00:00Z"
    }
  ],
  "total": 200
}
```

---

### `GET /api/search/permission-view`

查询权限视图（API → permissionCode 映射视图，ES）。

**Query 参数**:

| 参数 | 类型 | 说明 |
|------|------|------|
| `permissionCode` | string | |
| `action` | string | |
| `resourceType` | string | |
| `keyword` | string | |
| `page` / `pageSize` | int | 分页 |

**响应 `data`**:

```json
{
  "list": [
    {
      "project": "default",
      "permissionCode": "menu:system.user",
      "action": "access",
      "resourceType": "api",
      "path": "/api/admin/list",
      "groupCodes": ["admin"],
      "groupNames": ["管理员组"],
      "status": "Active"
    }
  ],
  "total": 30
}
```

---

## 附录：接口总览

| 模块 | 方法 | 路径 | 说明 |
|------|------|------|------|
| 初始化 | GET | `/api/admin/index` | 后台首页初始化 |
| 管理员 | GET | `/api/admin/list` | 分页列表 |
| 管理员 | POST | `/api/admin` | 新建 |
| 管理员 | PUT | `/api/admin/{dxeId}` | 完整编辑（含 group_arr） |
| 管理员 | PUT | `/api/admin/{dxeId}/status` | 快捷变更状态 |
| 管理员 | PUT | `/api/admin/{dxeId}/username` | 快捷更新名称 |
| 管理员 | DELETE | `/api/admin/{dxeId}` | 物理删除 |
| 权限组 | GET | `/api/group/list` | 分页列表 |
| 权限组 | POST | `/api/group` | 新建 |
| 权限组 | PUT | `/api/group/{dxeId}` | 完整编辑 |
| 权限组 | PUT | `/api/group/{dxeId}/rules` | 快捷更新规则/权限码 |
| 权限组 | PUT | `/api/group/{dxeId}/status` | 快捷变更状态 |
| 权限组 | POST | `/api/group/{dxeId}/members` | 添加成员 |
| 权限组 | DELETE | `/api/group/{dxeId}/members/{userid}` | 移除成员 |
| 权限组 | DELETE | `/api/group/{dxeId}` | 物理删除 |
| 规则 | GET | `/api/rule/tree` | 完整菜单树 |
| 规则 | GET | `/api/rule/list` | 分页列表 |
| 规则 | POST | `/api/rule` | 新建 |
| 规则 | PUT | `/api/rule/{dxeId}` | 完整编辑 |
| 规则 | PUT | `/api/rule/{dxeId}/status` | 快捷变更状态 |
| 规则 | PUT | `/api/rule/{dxeId}/weigh` | 更新排序权重 |
| 规则 | DELETE | `/api/rule/{dxeId}` | 物理删除 |
| Project 授权 | POST | `/api/project-grant` | 授权用户 |
| Project 授权 | DELETE | `/api/project-grant/{userid}` | 撤销授权 |
| Project 授权 | PUT | `/api/project-grant/{userid}/super` | 切换 super |
| API 映射 | GET | `/api/api-map/list` | 分页列表 |
| API 映射 | POST | `/api/api-map` | 新建映射 |
| API 映射 | PUT | `/api/api-map/{id}` | 更新映射 |
| API 映射 | DELETE | `/api/api-map/{id}` | 删除映射 |
| 查询 | GET | `/api/search/audit-logs` | 审计日志查询 |
| 查询 | GET | `/api/search/permission-view` | 权限视图查询 |
