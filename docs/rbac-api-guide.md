# RBAC API Guide

This document describes the HTTP API currently exposed by `Rbac.Api`.

The current API layer is a thin controller layer over Application services. Runtime authorization, project resolution, cache invalidation, Outbox production, Redis, ES, and Casbin behavior are handled by registered middleware, filters, and infrastructure services.

## Current Status

Implemented controllers:

- `AdminController` at `/api/admin`
- `GroupController` at `/api/group`
- `RuleController` at `/api/rule`
- `ApiMapController` at `/api/api-map`
- `ProjectGrantController` at `/api/project-grant`
- `SearchController` at `/api/search`

Not yet implemented as compatibility routes:

- `/api/app/user-info/login`
- `/api/app/rule/index`
- `/api/app/rule/get-rule-index`
- `/api/app/group/index`

The current routes are native RBAC routes, not ABP auto-generated `/api/app/*` routes.

## Request Context

All non-allowlisted requests go through:

1. JWT authentication
2. `CurrentRbacContextMiddleware`
3. `RbacAuthorizationFilter`
4. Controller action

Required headers for normal requests:

```http
Authorization: Bearer <jwt>
X-Project: <project-code>
```

`X-Project` is the preferred project source. The configured fallback sources are route and query parameters.

Allowlisted paths currently include:

- `/api/auth/login`
- `/health`
- `/healthz`
- `/swagger`
- `/favicon.ico`

## Response Envelope

Most APIs return:

```json
{
  "code": 0,
  "msg": "ok",
  "data": {},
  "time": 1778209450
}
```

Paged data uses:

```json
{
  "code": 0,
  "msg": "ok",
  "data": {
    "list": [],
    "total": 0
  },
  "time": 1778209450
}
```

## Admin APIs

Base route: `/api/admin`

### Get Backend Index

```http
GET /api/admin/index
```

Returns current admin info, menus, and initial `routePath`.

### Search Admins

```http
GET /api/admin/list?page=1&pageSize=20&keyword=alice&status=Active
```

The controller ignores any incoming `project` value and forces the project from `CurrentRbacContext`.

### Create Admin

```http
POST /api/admin
Content-Type: application/json

{
  "userid": "10001",
  "username": "Alice"
}
```

Writes MySQL and produces a `UserChanged` Outbox event.

### Change Admin Status

```http
PUT /api/admin/{dxeId}/status
Content-Type: application/json

{
  "status": "Disabled"
}
```

Supported status values in the controller flow:

- `Disabled`
- any other value currently maps to enable

### Update Admin Username

```http
PUT /api/admin/{dxeId}/username
Content-Type: application/json

{
  "username": "Alice Zhang"
}
```

## Group APIs

Base route: `/api/group`

### Search Groups

```http
GET /api/group/list?page=1&pageSize=20&keyword=admin&status=Active
```

### Create Group

```http
POST /api/group
Content-Type: application/json

{
  "groupCode": "admin",
  "groupName": "Administrators",
  "parentGroupCode": null
}
```

Writes MySQL and produces a `GroupChanged` Outbox event.

### Update Group Rules And Permissions

```http
PUT /api/group/{dxeId}/rules
Content-Type: application/json

{
  "ruleCodes": ["system.user", "system.user.add"],
  "permissionCodes": ["menu:system.user", "button:system.user.add"],
  "affectedUserids": ["10001", "10002"]
}
```

Writes MySQL and produces:

- `GroupChanged`
- `PolicyChanged` when permission codes changed

### Change Group Status

```http
PUT /api/group/{dxeId}/status
Content-Type: application/json

{
  "status": "Disabled",
  "affectedUserids": ["10001"]
}
```

### Add Group Member

```http
POST /api/group/{dxeId}/members
Content-Type: application/json

{
  "userid": "10001"
}
```

Writes `rbac_group_member` and produces:

- `PolicyChanged`
- `GroupChanged`

### Remove Group Member

```http
DELETE /api/group/{dxeId}/members/{userid}
```

The controller loads the real `RbacGroupMember` row from MySQL before deletion.

## Rule APIs

Base route: `/api/rule`

Rules represent menus and buttons.

### Get Project Menu Tree

```http
GET /api/rule/tree
```

Returns the project menu tree for the current project.

### Search Rules

```http
GET /api/rule/list?page=1&pageSize=20&keyword=user&type=Menu
```

### Create Rule

```http
POST /api/rule
Content-Type: application/json

{
  "ruleCode": "system.user",
  "permissionCode": "menu:system.user",
  "title": "User Management",
  "type": "Menu",
  "name": "SystemUser",
  "path": "/system/user",
  "parentRuleCode": null,
  "menuType": "Tab",
  "url": null,
  "component": "views/system/user/index",
  "extend": null,
  "keepalive": false,
  "weigh": 10
}
```

For button rules:

```json
{
  "ruleCode": "system.user.add",
  "permissionCode": "button:system.user.add",
  "title": "Add",
  "type": "Button",
  "parentRuleCode": "system.user"
}
```

Writes MySQL and produces a `MenuChanged` Outbox event.

### Change Rule Status

```http
PUT /api/rule/{dxeId}/status
Content-Type: application/json

{
  "status": "Disabled"
}
```

### Update Rule Sort Weight

```http
PUT /api/rule/{dxeId}/weigh
Content-Type: application/json

{
  "weigh": 20
}
```

### Delete Rule

```http
DELETE /api/rule/{dxeId}
```

Deletes the rule and produces a `MenuChanged` Outbox event.

## API Permission Map APIs

Base route: `/api/api-map`

API maps bind HTTP method + route pattern to `permissionCode + action`.

### Search Permission View

```http
GET /api/api-map/list?page=1&pageSize=20&permissionCode=api:system.user.create
```

### Create API Map

```http
POST /api/api-map
Content-Type: application/json

{
  "httpMethod": "POST",
  "routePattern": "/api/admin",
  "permissionCode": "api:admin.create",
  "action": "create"
}
```

Writes MySQL and produces an `ApiMapChanged` Outbox event.

### Update API Map

```http
PUT /api/api-map/{id}
Content-Type: application/json

{
  "permissionCode": "api:admin.update",
  "action": "update"
}
```

### Delete API Map

```http
DELETE /api/api-map/{id}
```

## Project Grant APIs

Base route: `/api/project-grant`

Project grant changes are high-risk permission changes. They actively invalidate the user's project-level cache through Outbox consumers.

### Grant Current Project To User

```http
POST /api/project-grant
Content-Type: application/json

{
  "userid": "10001",
  "isSuper": false
}
```

### Revoke Current Project From User

```http
DELETE /api/project-grant/{userid}
```

### Toggle Project Super

```http
PUT /api/project-grant/{userid}/super
Content-Type: application/json

{
  "isSuper": true
}
```

## Search APIs

Base route: `/api/search`

These APIs are read-only and query Elasticsearch.

### Search Audit Logs

```http
GET /api/search/audit-logs?page=1&pageSize=20&userid=10001&result=deny
```

### Search Permission View

```http
GET /api/search/permission-view?page=1&pageSize=20&resourceType=api
```

## Test Environment Checklist

Before running integration tests, prepare:

- MySQL connection string: `ConnectionStrings:Rbac`
- Redis connection string: `ConnectionStrings:Redis`
- Elasticsearch URI: `Elasticsearch:Uri`
- JWT settings under `Jwt`
- Project settings under `Project`
- Allowlist settings under `Allowlist`

Recommended test order:

1. `dotnet build Rbac.sln`
2. Start `Rbac.Api`
3. Verify `/swagger` loads
4. Verify JWT + `X-Project` context resolution
5. Call read-only APIs
6. Call write APIs and verify MySQL rows
7. Verify `rbac_outbox` receives events
8. Start `Rbac.Worker`
9. Verify Redis/ES/Casbin consumers process Outbox events

## Known Gaps

- Existing routes are not yet PHP/ABP-compatible `/api/app/*` routes.
- Login endpoint is allowlisted but not implemented in the current Controller set.
- Some response field names may still differ from the original PHP contract.
- Some APIs are MVP-level and expose narrower operations than the original batch-edit contract.
