-- =============================================================
-- RBAC Bootstrap 种子数据
--
-- 目的：解决首次启动的"鸡生蛋"问题。
--   系统首次运行时 rbac_api_permission_map 为空，
--   deny-by-default 会拦截所有管理接口，包括配置权限本身的接口。
--   本脚本预置最小运行数据，让 bootstrap 用户能进入系统完成初始配置。
--
-- 执行时机：建表脚本（rbac-init.sql）执行完毕后，首次启动服务前执行。
-- 执行方式：
--   1. 停服务
--   2. 执行本脚本
--   3. 启动 API
--   4. 用 bootstrap 用户登录
--   5. 调 /api/rule/tree 查看规则树
--   6. 通过 API 正常配置后续权限组和成员
--
-- 替换说明：
--   将下方所有 'REPLACE_USERID' 替换为实际的 bootstrap 用户 userid（来自 JWT/公司门户）。
--   将下方所有 'REPLACE_PROJECT' 替换为实际的 project 标识（X-Project header 值）。
--   UUID() 自动生成主键，重复执行安全（INSERT IGNORE）。
--
-- 注意：
--   TEXT 列不支持 DEFAULT 值，rule_codes / permission_codes 使用 JSON 数组字符串格式。
--   该格式与 EF Core GroupMapping.HasConversion 完全对齐：JsonSerializer.Serialize(List<string>)
-- =============================================================

SET NAMES utf8mb4;
SET @userid  = _utf8mb4'196045' COLLATE utf8mb4_unicode_ci;
SET @project = _utf8mb4'project' COLLATE utf8mb4_unicode_ci;
SET @global_project = _utf8mb4'__global__' COLLATE utf8mb4_unicode_ci;

-- =============================================================
-- 1. bootstrap 管理员账号
-- =============================================================
INSERT IGNORE INTO `rbac_administrator`
    (`id`, `userid`, `username`, `status`, `created_at`, `updated_at`)
VALUES (
    UUID(),
    @userid,
    'Bootstrap Admin',
    'Active',
    NOW(6), NOW(6)
);

-- =============================================================
-- 2. project 授权（super = 1，bootstrap 阶段最高权限）
-- =============================================================
INSERT IGNORE INTO `rbac_project_grant`
    (`id`, `userid`, `project`, `is_super`, `granted_by`, `granted_at`, `updated_at`)
VALUES (
    UUID(),
    @userid,
    @project,
    1,            -- super：跳过 permset 判断，所有接口均放行
    'bootstrap',
    NOW(6), NOW(6)
);

-- =============================================================
-- 3. bootstrap 权限组
-- =============================================================
INSERT IGNORE INTO `rbac_group`
    (`id`, `group_code`, `project`, `group_name`,
     `parent_group_code`, `rule_codes`, `permission_codes`, `status`,
     `created_at`, `updated_at`)
VALUES (
    UUID(),
    'system_admin',
    @project,
    '系统管理员（Bootstrap）',
    NULL,
    -- rule_codes：与 rbac_rule.rule_code 对齐，此处为空，启动后通过 /api/rule/tree 配置
    '[]',
    -- permission_codes：bootstrap 阶段通过 is_super 授权，此处为空占位
    '[]',
    'Active',
    NOW(6), NOW(6)
);

-- =============================================================
-- 4. bootstrap 用户加入 system_admin 组
-- =============================================================
INSERT IGNORE INTO `rbac_group_member`
    (`id`, `userid`, `group_code`, `project`, `granted_by`, `created_at`, `updated_at`)
VALUES (
    UUID(),
    @userid,
    'system_admin',
    @project,
    'bootstrap',
    NOW(6), NOW(6)
);

-- =============================================================
-- 4.1 Unified Permission Center 保留系统（__global__）
--
-- 说明：
--   __global__ 是普通 RBAC project，用现有授权管道管理全局控制台。
--   全局 bootstrap 用户复用 @userid；业务 project 仍使用 @project。
--   跨 project 目标枚举必须排除 __global__，见 RbacGlobalConstants.IsReservedProject。
-- =============================================================

INSERT IGNORE INTO `rbac_project_grant`
    (`id`, `userid`, `project`, `is_super`, `granted_by`, `granted_at`, `updated_at`)
VALUES (
    UUID(),
    @userid,
    @global_project,
    1,
    'bootstrap',
    NOW(6), NOW(6)
);

INSERT IGNORE INTO `rbac_rule`
    (`id`, `project`, `rule_code`, `permission_code`, `parent_rule_code`, `type`,
     `title`, `name`, `path`, `icon`, `menu_type`, `url`, `component`, `extend`,
     `remark`, `keepalive`, `weigh`, `status`, `created_at`, `updated_at`)
VALUES
    (UUID(), @global_project, 'global.console', 'rbac.global.admin', NULL,
     'MenuDir', '统一权限中心', 'GlobalConsole', '/global',
     'Monitor', NULL, NULL, 'LAYOUT', NULL, NULL, 0, 0, 'Active', NOW(6), NOW(6)),
    (UUID(), @global_project, 'global.project', 'rbac.global.admin', 'global.console',
     'Menu', '项目列表', 'GlobalProject', '/global/project',
     'Grid', 'Tab', NULL, 'views/global/project/index', NULL, NULL, 0, 5, 'Active', NOW(6), NOW(6)),
    (UUID(), @global_project, 'global.user', 'rbac.global.user.manage', 'global.console',
     'Menu', '跨项目用户管理', 'GlobalUser', '/global/user',
     'User', 'Tab', NULL, 'views/global/user/index', NULL, NULL, 0, 10, 'Active', NOW(6), NOW(6)),
    (UUID(), @global_project, 'global.group', 'rbac.global.group.manage', 'global.console',
     'Menu', '跨项目权限组管理', 'GlobalGroup', '/global/group',
     'UserGroup', 'Tab', NULL, 'views/global/group/index', NULL, NULL, 0, 20, 'Active', NOW(6), NOW(6)),
    (UUID(), @global_project, 'global.menu', 'rbac.global.menu.manage', 'global.console',
     'Menu', '跨项目规则管理', 'GlobalMenu', '/global/menu',
     'Menu', 'Tab', NULL, 'views/global/menu/index', NULL, NULL, 0, 30, 'Active', NOW(6), NOW(6));

INSERT IGNORE INTO `rbac_group`
    (`id`, `group_code`, `project`, `group_name`,
     `parent_group_code`, `rule_codes`, `permission_codes`, `status`,
     `created_at`, `updated_at`)
VALUES (
    UUID(),
    'global_admins',
    @global_project,
    '全局管理员',
    NULL,
    '["global.console","global.project","global.user","global.group","global.menu"]',
    '["rbac.global.admin","rbac.global.user.manage","rbac.global.group.manage","rbac.global.menu.manage"]',
    'Active',
    NOW(6), NOW(6)
);

INSERT IGNORE INTO `rbac_group_member`
    (`id`, `userid`, `group_code`, `project`, `granted_by`, `created_at`, `updated_at`)
VALUES (
    UUID(),
    @userid,
    'global_admins',
    @global_project,
    'bootstrap',
    NOW(6), NOW(6)
);

-- =============================================================
-- 5. rbac_api_permission_map — 权限管理核心接口映射
--
-- 说明：
--   /api/admin/index 已在 ProjectAccessAllowlist，此处可不写。
--   但写进来无害，且方便未来移出 allowlist 时直接生效。
--
--   permission_code 格式：{resourceType}:{scope}
--   action 允许值：read / create / update / delete / execute / access
-- =============================================================

-- ── 后台初始化（/api/admin/index 已在 ProjectAccessAllowlist，此处补充 api-map 以备移出）
INSERT IGNORE INTO `rbac_api_permission_map`
    (`id`, `project`, `http_method`, `route_pattern`, `permission_code`, `action`, `status`, `created_at`, `updated_at`)
VALUES
    (UUID(), @project, 'GET', '/api/admin/index',   'menu:admin.index',      'access', 'Active', NOW(6), NOW(6));

-- ── 管理员管理
INSERT IGNORE INTO `rbac_api_permission_map`
    (`id`, `project`, `http_method`, `route_pattern`, `permission_code`, `action`, `status`, `created_at`, `updated_at`)
VALUES
    (UUID(), @project, 'GET',    '/api/admin/list',           'menu:admin.list',        'read',   'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'POST',   '/api/admin',                'button:admin.create',    'create', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'PUT',    '/api/admin/{userid}',        'button:admin.edit',      'update', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'PUT',    '/api/admin/{userid}/status', 'button:admin.status',    'update', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'PUT',    '/api/admin/{userid}/username','button:admin.username', 'update', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'DELETE', '/api/admin/{userid}',        'button:admin.delete',    'delete', 'Active', NOW(6), NOW(6));

-- ── 权限组管理
INSERT IGNORE INTO `rbac_api_permission_map`
    (`id`, `project`, `http_method`, `route_pattern`, `permission_code`, `action`, `status`, `created_at`, `updated_at`)
VALUES
    (UUID(), @project, 'GET',    '/api/group/list',                    'menu:group.list',         'read',   'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'POST',   '/api/group',                         'button:group.create',     'create', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'PUT',    '/api/group/{groupCode}',                 'button:group.edit',       'update', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'PUT',    '/api/group/{groupCode}/rules',           'button:group.rules',      'update', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'PUT',    '/api/group/{groupCode}/status',          'button:group.status',     'update', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'POST',   '/api/group/{groupCode}/members',         'button:group.member.add', 'create', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'DELETE', '/api/group/{groupCode}/members/{userid}','button:group.member.del', 'delete', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'DELETE', '/api/group/{groupCode}',                 'button:group.delete',     'delete', 'Active', NOW(6), NOW(6));

-- ── 菜单/按钮规则管理
INSERT IGNORE INTO `rbac_api_permission_map`
    (`id`, `project`, `http_method`, `route_pattern`, `permission_code`, `action`, `status`, `created_at`, `updated_at`)
VALUES
    (UUID(), @project, 'GET',    '/api/rule/tree',           'menu:rule.tree',       'read',   'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'GET',    '/api/rule/list',           'menu:rule.list',       'read',   'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'POST',   '/api/rule',                'button:rule.create',   'create', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'PUT',    '/api/rule/{ruleCode}',        'button:rule.edit',     'update', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'PUT',    '/api/rule/{ruleCode}/status', 'button:rule.status',   'update', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'PUT',    '/api/rule/{ruleCode}/weigh',  'button:rule.weigh',    'update', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'DELETE', '/api/rule/{ruleCode}',        'button:rule.delete',   'delete', 'Active', NOW(6), NOW(6));

-- ── API 权限映射管理
INSERT IGNORE INTO `rbac_api_permission_map`
    (`id`, `project`, `http_method`, `route_pattern`, `permission_code`, `action`, `status`, `created_at`, `updated_at`)
VALUES
    (UUID(), @project, 'GET',    '/api/api-map/list',  'menu:apimap.list',     'read',   'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'GET',    '/api/api-map/records','menu:apimap.list',    'read',   'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'POST',   '/api/api-map',       'button:apimap.create', 'create', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'PUT',    '/api/api-map/{id}',  'button:apimap.edit',   'update', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'DELETE', '/api/api-map/{id}',  'button:apimap.delete', 'delete', 'Active', NOW(6), NOW(6));

-- ── Project 授权管理
INSERT IGNORE INTO `rbac_api_permission_map`
    (`id`, `project`, `http_method`, `route_pattern`, `permission_code`, `action`, `status`, `created_at`, `updated_at`)
VALUES
    (UUID(), @project, 'POST',   '/api/project-grant',             'button:grant.create', 'create', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'DELETE', '/api/project-grant/{userid}',    'button:grant.delete', 'delete', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'PUT',    '/api/project-grant/{userid}/super','button:grant.super', 'update', 'Active', NOW(6), NOW(6));

-- ── 查询接口（只读）
INSERT IGNORE INTO `rbac_api_permission_map`
    (`id`, `project`, `http_method`, `route_pattern`, `permission_code`, `action`, `status`, `created_at`, `updated_at`)
VALUES
    (UUID(), @project, 'GET', '/api/search/audit-logs',     'menu:search.audit',      'read', 'Active', NOW(6), NOW(6)),
    (UUID(), @project, 'GET', '/api/search/permission-view','menu:search.permission', 'read', 'Active', NOW(6), NOW(6));

-- ── Unified Permission Center 全局接口映射
INSERT IGNORE INTO `rbac_api_permission_map`
    (`id`, `project`, `http_method`, `route_pattern`, `permission_code`, `action`, `status`, `created_at`, `updated_at`)
VALUES
    (UUID(), @global_project, 'GET', '/api/global/project/list',        'rbac.global.admin',        'access', 'Active', NOW(6), NOW(6)),
    (UUID(), @global_project, 'GET', '/api/global/user/list',           'rbac.global.user.manage',  'access', 'Active', NOW(6), NOW(6)),
    (UUID(), @global_project, 'PUT', '/api/global/user/{userid}/status','rbac.global.user.manage',  'write',  'Active', NOW(6), NOW(6)),
    (UUID(), @global_project, 'GET', '/api/global/group/list',          'rbac.global.group.manage', 'access', 'Active', NOW(6), NOW(6)),
    (UUID(), @global_project, 'GET', '/api/global/menu/list',           'rbac.global.menu.manage',  'access', 'Active', NOW(6), NOW(6));

-- =============================================================
-- 执行完毕检查
-- =============================================================
SELECT 'administrator' AS table_name, COUNT(*) AS row_count FROM rbac_administrator WHERE userid = @userid
UNION ALL
SELECT 'project_grant', COUNT(*) FROM rbac_project_grant WHERE userid = @userid AND project = @project
UNION ALL
SELECT 'group', COUNT(*) FROM rbac_group WHERE group_code = 'system_admin' AND project = @project
UNION ALL
SELECT 'group_member', COUNT(*) FROM rbac_group_member WHERE userid = @userid AND project = @project
UNION ALL
SELECT 'api_permission_map', COUNT(*) FROM rbac_api_permission_map WHERE project = @project
UNION ALL
SELECT 'global_project_grant', COUNT(*) FROM rbac_project_grant WHERE userid = @userid AND project = @global_project
UNION ALL
SELECT 'global_group', COUNT(*) FROM rbac_group WHERE group_code = 'global_admins' AND project = @global_project
UNION ALL
SELECT 'global_group_member', COUNT(*) FROM rbac_group_member WHERE userid = @userid AND group_code = 'global_admins' AND project = @global_project
UNION ALL
SELECT 'global_rule', COUNT(*) FROM rbac_rule WHERE project = @global_project AND rule_code LIKE 'global.%'
UNION ALL
SELECT 'global_api_permission_map', COUNT(*) FROM rbac_api_permission_map WHERE project = @global_project;
