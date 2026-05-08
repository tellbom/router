# RBAC 权限模块只读核实报告

**核实日期**: 2026-05-08  
**核实范围**: 仅 RBAC 权限模块；未分析其他业务模块；未修改 PHP 代码；未设计 Casbin 接入。  
**核实对象**: 当前目录 `代码核实.md` 与原 PHP 项目代码。  
**重要前提**: 当前项目是原始 PHP/ThinkPHP + BuildAdmin 项目，不是 C# / ABP 项目。`代码核实.md` 中的 `/api/app/...` 与 `I*AppService` 属于拟重写契约，不是 PHP 项目中的实际命名。

## 1. 结论摘要

当前 `代码核实.md` **不足以直接作为后续 C# RBAC 重构依据**。它可以作为“意图说明”和“待核对清单”，但不能作为兼容性合同直接实现。

主要原因：

1. 文档中的 API 路由大多是 ABP 风格 `/api/app/...`，PHP 实际路由是 BuildAdmin 风格，例如 `/admin/index/login`、`/admin/auth.admin/index`、`/admin/auth.group/index`、`/admin/auth.menu/index`。
2. 文档大量使用 `DXE_id`、`project`、`allmenu`、`supre`、`UserIdentifier`、`infoMessage` 等字段，但在 PHP RBAC 代码和数据库表中未发现这些字段。
3. PHP 返回 DTO 使用原始字段名 `id/pid/rules/menu_type/keepalive/createtime/updatetime/status/children`，时间字段多为 Unix 秒级整数；文档中的 `DateTime`、`Guid Id`、`DXE_id`、PascalCase DTO 名称与实际不一致。
4. PHP 中后台管理员 RBAC 与会员 RBAC 是两套模型：后台使用 `admin/admin_group/admin_group_access/menu_rule`；会员使用 `user/user_group/user_rule`。文档把二者部分概念混在一起，需拆清。
5. 登录认证在 PHP 中使用 `batoken` / `refreshToken` 令牌机制，不是 JWT。后续 C# 可统一 JWT，但必须做响应字段兼容层。

## 2. PHP 实际 RBAC 边界

### 后台管理员 RBAC

核心文件：

- `app/admin/controller/Index.php`
- `app/admin/controller/auth/Admin.php`
- `app/admin/controller/auth/Group.php`
- `app/admin/controller/auth/Menu.php`
- `app/admin/model/Admin.php`
- `app/admin/model/AdminGroup.php`
- `app/admin/model/MenuRule.php`
- `app/admin/library/Auth.php`
- `extend/ba/Auth.php`
- `app/admin/buildadmin.sql`
- `web/src/api/controllerUrls.ts`
- `web/src/api/common.ts`

实际表：

- `admin`: 管理员表
- `admin_group`: 管理分组表
- `admin_group_access`: 管理员-分组关联表
- `menu_rule`: 菜单和权限规则表

### 会员 RBAC

存在但不应与后台管理员 RBAC 混淆：

- `app/admin/controller/user/Group.php`
- `app/admin/controller/user/Rule.php`
- `user_group`
- `user_rule`

会员 RBAC 没有独立 `user_group_access` 表，公共权限类配置中 `auth_group_access` 为空，用户通过 `user.group_id` 关联会员组。

## 3. 通用响应与请求结构

| 项目 | 文档定义 | PHP 实际定义 | 是否一致 | 差异点 | 风险 | 建议 |
|---|---|---|---|---|---|---|
| 响应壳 | `HttpCode<T>{ Code, Msg, Time, Data }` | `Api::result()` 返回 `code/msg/time/data` | 部分一致 | 字段为小写；`time` 是 `REQUEST_TIME` Unix 秒；错误默认 `code=0`，成功默认 `code=1` | 中 | C# 对外 JSON 必须保持 `code/msg/time/data` 小写；`time` 建议兼容 Unix 秒 |
| 分页查询 | `currentPage/pagesize/search/sorter/sorterDirection/complex` | `quick_search/limit/order/search/initKey/initValue/select/isTree/absoluteAuth` | 不一致 | PHP 前端 `baTableApi.index()` 以 GET params 传参；复杂查询数组字段为 `operator/field/val/render` | 高 | 重构时应优先兼容 PHP/前端参数名，而不是文档中的 ABP `FilterInput` |
| 批量编辑 | `BaTableEdit<T>{ keys, extra, type, data }` | 列表删除为 `DELETE .../del?ids[]=...`；排序为 POST `id/targetId`；新增/编辑为 POST 表单对象 | 不一致 | PHP 未发现 `keys/extra/type/data` 通用批量结构 | 高 | 不要按 `BaTableEdit` 直接实现旧前端兼容接口 |

## 4. 用户模块

### 4.1 后台登录

| 项目 | 文档定义 | PHP 实际定义 | 是否一致 | 差异点 | 风险 | 建议 |
|---|---|---|---|---|---|---|
| 登录接口 | `/api/app/user-info/login` | `/admin/index/login`，POST `username/password/keep`，可能带验证码 `captcha/captcha_id` | 不一致 | 路由、参数、返回字段均不同 | 高 | C# 若兼容旧前端，应提供 `/admin/index/login` 或兼容路由映射 |
| 登录成功返回 | `UserInfoDto` 直接含用户、token、routePath、allmenu 等 | `data.userInfo` + `data.routePath`；`userInfo` 来自 `Auth::getInfo()` | 不一致 | PHP 登录不返回菜单；菜单在 `/admin/index/index` 返回 | 高 | 登录接口和菜单初始化接口需拆开兼容 |
| 登录认证 | JWT | PHP `batoken` header/request/cookie + `refreshToken` | 不一致但可重构 | 用户要求后续统一 JWT；PHP 不使用 JWT | 中 | C# 内部可用 JWT，但对旧前端需兼容 `batoken` 或提供迁移层 |

PHP 登录成功 `userInfo` 关键字段：

- `id`
- `username`
- `nickname`
- `avatar`
- `lastlogintime`
- `token`
- `refreshToken`

文档中存在但 PHP 登录未返回或不存在的字段：

- `UserIdentifier`
- `DXE_id`
- `userid`
- `createtime`
- `updatetime`
- `status`
- `supre`
- `allmenu`
- `isLogout`
- `group_arr`
- `group_name_arr`

### 4.2 后台用户/管理员列表

| 项目 | 文档定义 | PHP 实际定义 | 是否一致 | 差异点 | 风险 | 建议 |
|---|---|---|---|---|---|---|
| 获取用户列表 | `GetUserInfoListAsync(FilterInput)` | `GET /admin/auth.admin/index` | 部分一致 | 文档称用户；PHP 实际是管理员 `Admin` | 高 | 重命名为“管理员管理”或明确此处不是会员用户 |
| 新增用户 | `CreateUserInfoAsync(UserInfoDto)` | `POST /admin/auth.admin/add` | 部分一致 | PHP 必填 `username/nickname/password/group_arr`；不接收 `DXE_id/allmenu/project` | 高 | 请求 DTO 应按 PHP 字段重建 |
| 编辑用户 | `UpdateUserInfoAsync(UserInfoDto)` | `GET /admin/auth.admin/edit?id=...` 查详情；`POST /admin/auth.admin/edit` 更新 | 部分一致 | PHP 同一接口 GET/POST 双语义 | 中 | C# 兼容层需保留 GET 详情 + POST 修改 |
| 删除用户 | `DeleteUserInfoAsync(BaTableEdit<UserInfoDto>)` | `DELETE /admin/auth.admin/del?ids[]=...` | 不一致 | PHP 使用 `ids` 数组 query 参数 | 高 | 删除接口需兼容 `ids` |

管理员列表实际返回：

```json
{
  "code": 1,
  "msg": "",
  "time": 1778209450,
  "data": {
    "list": [],
    "total": 0,
    "remark": ""
  }
}
```

管理员行兼容关键字段：

- `id`
- `username`
- `nickname`
- `avatar`
- `email`
- `mobile`
- `lastlogintime`
- `lastloginip`
- `createtime`
- `updatetime`
- `status`
- `group_arr`
- `group_name_arr`

其中 `group_arr`、`group_name_arr` 是 PHP `Admin` 模型追加属性，前端表单和远程选择会依赖。

## 5. 权限组模块

| 项目 | 文档定义 | PHP 实际定义 | 是否一致 | 差异点 | 风险 | 建议 |
|---|---|---|---|---|---|---|
| 获取组列表 | `/api/app/group/index` / `GetGroupListAsync` | `GET /admin/auth.group/index` | 路由不一致，返回结构相近 | PHP 返回 `data.list/remark/group` | 高 | 兼容路由应以 `/admin/auth.group/index` 为准 |
| 获取组下拉 | 文档另列 `IGroupListAppService.GetGroupTreeAsync()` | 同一个 `GET /admin/auth.group/index?select=true...` 触发 `select()` | 不一致 | PHP 没有独立 GroupList 服务 | 中 | 不应新增独立旧兼容接口，除非新前端需要 |
| 新增组 | `CreateGroupAsync(GroupAddCreDto)` | `POST /admin/auth.group/add` | 部分一致 | PHP 接收 `name/pid/rules/status`；`rules` 数组会转成字符串或 `*` | 中 | 输入可用 `rules: number[]`，持久化为 CSV/`*` |
| 编辑组 | `UpdateGroupAsync(GroupEditDto)` | `GET /admin/auth.group/edit?id=...` 详情；`POST /admin/auth.group/edit` 更新 | 部分一致 | PHP 详情返回 `data.row`；规则回显时会移除父节点 id | 高 | C# 回显逻辑需复刻，否则权限树勾选状态会变 |
| 删除组 | `BatchDeleteGroupAsync(BaTableEdit<...>)` | `DELETE /admin/auth.group/del?ids[]=...` | 不一致 | PHP 删除前校验子组和自身所属组 | 中 | 兼容 `ids` query 参数 |

权限组列表实际返回关键字段：

- `id`
- `pid`
- `name`
- `rules`
- `createtime`
- `updatetime`
- `status`
- `children`

字段语义核实：

- `group`: 当前登录管理员所属的后台角色组 ID 数组，来自 `admin_group_access.group_id`。
- `rules`: 数据库存储为 `admin_group.rules`，是规则 ID CSV 或 `*`；列表展示时被转换为文本，如“某菜单等 N 项”或“超级管理员”。
- `children`: 仅树模式下由 `Tree::assembleChild()` 组装。
- `DXE_id`: PHP 不存在。文档示例中 `DXE_id` 应被视为错误字段或外部系统自定义字段，不能作为 PHP 兼容字段。

## 6. 用户-权限组关联模块

| 项目 | 文档定义 | PHP 实际定义 | 是否一致 | 差异点 | 风险 | 建议 |
|---|---|---|---|---|---|---|
| 关联列表接口 | `GetGroupAccessListAsync(FilterInput)` | PHP 未发现独立后台 `group_access` 控制器接口 | 不一致 | 关联表通过管理员新增/编辑隐式维护 | 高 | 不要把独立 GroupAccess 接口视为旧合同 |
| 更新关联接口 | `UpdateGroupAccessAsync(GroupEditCreDto)` | `POST /admin/auth.admin/add` 或 `POST /admin/auth.admin/edit` 中处理 `group_arr` | 不一致 | PHP 删除旧关联后重建 `admin_group_access` | 高 | C# 兼容应在管理员保存接口中处理 `group_arr` |
| 关联表字段 | `id/uid/group_id` | `admin_group_access` 只有 `uid/group_id`，联合唯一键 | 不一致 | PHP 表没有 `id` 主键 | 高 | DTO 不应要求 `id` |

前端兼容关键字段：

- 管理员行上的 `group_arr`
- 管理员行上的 `group_name_arr`
- 表 `admin_group_access.uid`
- 表 `admin_group_access.group_id`

## 7. 规则/菜单模块

| 项目 | 文档定义 | PHP 实际定义 | 是否一致 | 差异点 | 风险 | 建议 |
|---|---|---|---|---|---|---|
| 获取规则列表 | `/api/app/rule/index` / `GetRulesAsync` | `GET /admin/auth.menu/index` | 路由不一致，返回结构相近 | PHP 是菜单规则管理，返回 `data.list/remark` | 高 | 以 `auth.menu` 命名和路由为兼容基础 |
| 获取规则编辑索引 | `GetRuleMenuAsync()` | `GET /admin/auth.menu/index?select=true` | 部分一致 | PHP 下拉只取 `type in menu_dir, menu` 且 `status=1`，返回 `data.options` | 中 | `RuleMenuAppService` 是拟重写抽象，不是 PHP 接口 |
| 新增规则 | `CreateRuleAsync(RuleMessageInputDto)` | 通用 trait `POST /admin/auth.menu/add` | 部分一致 | PHP `Menu` 控制器关闭模型验证，但数据库字段枚举约束仍生效 | 中 | 输入字段需匹配 `menu_rule` 表 |
| 编辑规则 | `UpdateRuleAsync(RuleEditDto)` | `GET /admin/auth.menu/edit?id=...` 详情；`POST /admin/auth.menu/edit` 更新 | 部分一致 | GET 返回 `data.row` | 中 | 兼容 GET/POST 双语义 |
| 删除规则 | `BatchDeleteRuleAsync(BaTableEdit<...>)` | `DELETE /admin/auth.menu/del?ids[]=...` | 不一致 | 删除前检查子节点 | 中 | 兼容 `ids` |
| 排序规则 | 文档只在 `Sortable` DTO 中提及 | `POST /admin/auth.menu/sortable`，`id/targetId` | 文档遗漏接口 | PHP 前端 `baTableApi.sortableApi()` 使用 | 中 | 应补入旧兼容接口 |

`menu_rule` 实际字段：

- `id`
- `pid`
- `type`
- `title`
- `name`
- `path`
- `icon`
- `menu_type`
- `url`
- `component`
- `keepalive`
- `extend`
- `remark`
- `weigh`
- `status`
- `updatetime`
- `createtime`
- `children` 由树组装追加，不是表字段

枚举核实：

- `type`: `menu_dir`、`menu`、`button`
- `menu_type`: `tab`、`link`、`iframe`
- `extend`: `none`、`add_rules_only`、`add_menu_only`
- `keepalive`: `0/1`
- `status`: 字符串枚举 `'1'/'0'`

文档错误或风险字段：

- `DXE_id`: PHP 不存在，实际业务主键就是 `id`。
- `Project`: PHP `menu_rule` 表没有 `project` 字段，未发现权限过滤使用 project。
- `Type` 文档写“父级菜单/子集菜单/按钮”，实际值是 `menu_dir/menu/button`。
- `Menu_type` 文档只写 `Tab/Link`，遗漏 `iframe`，实际大小写为小写。
- `Keepalive` 文档在不同 DTO 中有 `string/int` 混用，PHP 是 tinyint 0/1，返回到前端通常为数值或字符串形式，需兼容。

## 8. 权限菜单返回

| 项目 | 文档定义 | PHP 实际定义 | 是否一致 | 差异点 | 风险 | 建议 |
|---|---|---|---|---|---|---|
| 获取权限菜单 | `/api/app/rule/get-rule-index` 返回 `infoMessage/menus` | `GET /admin/index/index` 返回 `adminInfo/menus/siteConfig/terminal` | 不一致 | PHP 没有 `get-rule-index`；菜单不在 `auth.menu` 控制器返回给首页 | 高 | 后续 C# 必须提供旧首页初始化 DTO |
| 菜单来源 | `UserInfoDto.Allmenu` 或 `GetRuleIndexDto.Menus` | `Auth::getMenus()` 从 `menu_rule` 按登录用户规则树过滤 | 部分一致 | PHP 字段名是 `menus`，不是 `allmenu` | 高 | 前端兼容字段应是 `menus` |
| 超管逻辑 | `supre` 字段 | `isSuperAdmin()` 判断规则 ID 是否含 `*`，首页 `adminInfo.super` | 不一致 | PHP 字段是 `super`，不是 `supre`；登录不返回，首页返回 | 高 | 文档 `supre` 应改为 `super`，或确认是否外部系统另有拼写 |

首页初始化实际返回：

```json
{
  "code": 1,
  "msg": "",
  "time": 1778209450,
  "data": {
    "adminInfo": {
      "id": 1,
      "username": "admin",
      "nickname": "Admin",
      "avatar": "...",
      "lastlogintime": "...",
      "super": true
    },
    "menus": [],
    "siteConfig": {},
    "terminal": {}
  }
}
```

权限菜单前端兼容关键字段：

- `menus`
- `id`
- `pid`
- `type`
- `title`
- `name`
- `path`
- `icon`
- `menu_type`
- `url`
- `component`
- `keepalive`
- `extend`
- `children`

前端会基于菜单树构造路由，并将按钮规则转换为 auth 节点：

- 菜单 `type=menu/menu_dir` 进入路由树。
- 按钮 `type=button` 作为权限节点。
- `extend=add_rules_only` 不显示为菜单。
- `extend=add_menu_only` 不作为规则。
- `keepalive` 在前端路由 meta 中使用。

## 9. 指定字段语义核实

| 字段 | PHP 是否存在 | PHP 语义 | 文档状态 | 风险 |
|---|---:|---|---|---|
| `group` | 是 | `/admin/auth.group/index` 返回当前登录管理员所属分组 ID 数组 | 基本正确 | 中 |
| `rules` | 是 | 组表中为规则 ID CSV 或 `*`；组列表返回时会变成展示文本；菜单表中不存在 `rules` 字段 | 文档混用数组和字符串，需要明确上下文 | 高 |
| `menus` | 是 | `/admin/index/index` 返回当前用户可访问菜单树 | 文档写在 `GetRuleIndexDto` 中，但路由不对 | 高 |
| `allmenu` | 否 | 未发现 PHP 字段 | 文档字段不兼容 | 高 |
| `group_arr` | 是 | 管理员追加属性，当前管理员所属组 ID 数组 | 正确但仅在管理员模型中存在 | 高 |
| `group_name_arr` | 是 | 管理员追加属性，当前管理员所属组名称数组 | 正确但仅在管理员模型中存在 | 高 |
| `DXE_id` | 否 | 未发现 PHP 字段；PHP 使用 `id` | 文档大量使用，需删除或另行证明来源 | 高 |
| `project` | 否 | 未发现表字段或权限过滤逻辑 | 文档推断，不是 PHP 合同 | 高 |
| `routePath` | 是 | 登录态异常、已登录跳转、登录成功时返回 `/admin`、`/admin/login`、前台 `/user`、`/user/login` | 文档放在用户 DTO 中不准确 | 中 |
| `supre` | 否 | PHP 首页 `adminInfo.super`，来自 `isSuperAdmin()` | 文档拼写疑似错误 | 高 |

## 10. 文档中存在但 PHP 未发现的接口/字段

### 接口

- `/api/app/user-info/login`
- `/api/app/rule/get-rule-index`
- `/api/app/rule/index`
- `/api/app/group/index`
- `IGroupAccessAppService` 独立关联服务
- `IGroupListAppService` 独立组树服务

### 字段 / DTO 名

- `DXE_id`
- `Project/project`
- `Allmenu/allmenu`
- `Supre/supre`
- `UserIdentifier`
- `InfoMessage/infoMessage`
- `IsLogout`
- `Userid/userid`
- `HttpCodeArray`
- `BaTableEdit.keys/extra/type/data`
- `FilterInput.currentPage/pagesize/sorter/sorterDirection/complex`

## 11. PHP 存在但文档遗漏或弱化的接口/字段

### 接口

- `GET /admin/index/index`: 后台初始化，返回 `adminInfo/menus/siteConfig/terminal`
- `GET|POST /admin/index/login`: 后台登录/登录页信息
- `POST /admin/index/logout`
- `GET /admin/auth.admin/edit?id=...`: 管理员详情
- `GET /admin/auth.group/edit?id=...`: 分组详情，含规则回显处理
- `GET /admin/auth.menu/edit?id=...`: 菜单规则详情
- `POST /admin/auth.menu/sortable`: 菜单规则排序
- `POST /admin/auth.group/sortable`: 按通用 trait 理论存在，前端未见主要使用

### 字段

- `nickname`
- `avatar`
- `email`
- `mobile`
- `refreshToken`
- `super`
- `siteConfig`
- `terminal`
- `total`
- `remark`
- `options`
- `menu_type=iframe`
- `ids` 删除参数
- `id/targetId` 排序参数

## 12. 兼容关键字段清单

必须优先兼容这些字段，因为 PHP 前端代码或模型明确依赖：

- 响应壳：`code`、`msg`、`time`、`data`
- 管理员信息：`id`、`username`、`nickname`、`avatar`、`lastlogintime`、`token`、`refreshToken`、`super`
- 管理员管理：`email`、`mobile`、`lastloginip`、`createtime`、`updatetime`、`status`、`group_arr`、`group_name_arr`
- 权限组：`id`、`pid`、`name`、`rules`、`createtime`、`updatetime`、`status`、`children`
- 菜单规则：`id`、`pid`、`type`、`title`、`name`、`path`、`icon`、`menu_type`、`url`、`component`、`keepalive`、`extend`、`remark`、`weigh`、`status`、`updatetime`、`createtime`、`children`
- 首页初始化：`adminInfo`、`menus`、`siteConfig`、`terminal`
- 表格列表：`list`、`total`、`remark`
- 下拉树：`options`
- 权限节点：按钮规则的 `name`，例如 `auth/group/add`

不得在旧兼容接口中强制依赖这些字段，除非能从内网前端另行证明：

- `DXE_id`
- `project`
- `allmenu`
- `supre`
- `UserIdentifier`

## 13. 后续重构建议边界

本次不设计 Casbin 接入方案。仅建议：

1. 先将 PHP 实际接口合同整理为 C# 兼容层合同。
2. C# 内部可使用更规范的实体/DTO，但对外旧接口必须保留 PHP 字段名和大小写。
3. 登录认证可统一 JWT，但旧前端如果仍发送 `batoken`，需做请求头兼容或前端同步改造。
4. `project`、`DXE_id` 若来自其他内网系统，不应从当前 PHP 项目推断；需要额外前端或数据库样本确认。
5. Casbin 后续可评估，但当前 PHP 合同中没有 Casbin 概念，也没有 `sub/dom/obj/act` 结构。

## 14. 最终判断

`代码核实.md` 当前版本适合作为“RBAC 重构讨论稿”，但不适合作为“兼容旧 PHP/前端的实现合同”。

进入 C# RBAC 重写前，建议先生成一份新的“PHP 实际合同版”文档，基于以下实际路由和字段：

- `/admin/index/login`
- `/admin/index/index`
- `/admin/index/logout`
- `/admin/auth.admin/index|add|edit|del`
- `/admin/auth.group/index|add|edit|del`
- `/admin/auth.menu/index|add|edit|del|sortable`

其中返回 DTO 应以 PHP 的 `id`、`menus`、`super`、`group_arr`、`group_name_arr`、`menu_type`、`keepalive`、`extend` 等字段为准，而不是以 `DXE_id`、`allmenu`、`supre`、`project` 为准。
