-- =============================================================
-- RBAC bootstrap seed data for Dameng DM8.
--
-- Configuration:
--   Change the INSERT values below to set the bootstrap user's employee id
--   and the X-Project alias.
-- =============================================================

CREATE TABLE IF NOT EXISTS "rbac_bootstrap_config" (
    "userid"  VARCHAR2(128) NOT NULL,
    "project" VARCHAR2(64) NOT NULL
);

DELETE FROM "rbac_bootstrap_config";

INSERT INTO "rbac_bootstrap_config" ("userid", "project")
VALUES ('196045', 'oversia');

MERGE INTO "rbac_administrator" t
USING (
    SELECT
        UUID() AS "id",
        "userid",
        'Bootstrap Admin' AS "username",
        'Active' AS "status"
    FROM "rbac_bootstrap_config"
) s
ON (t."userid" = s."userid")
WHEN NOT MATCHED THEN
    INSERT ("id", "userid", "username", "status", "created_at", "updated_at")
    VALUES (s."id", s."userid", s."username", s."status", CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

MERGE INTO "rbac_project_grant" t
USING (
    SELECT
        UUID() AS "id",
        "userid",
        "project",
        1 AS "is_super",
        'bootstrap' AS "granted_by"
    FROM "rbac_bootstrap_config"
) s
ON (t."userid" = s."userid" AND t."project" = s."project")
WHEN NOT MATCHED THEN
    INSERT ("id", "userid", "project", "is_super", "granted_by", "granted_at", "updated_at")
    VALUES (s."id", s."userid", s."project", s."is_super", s."granted_by", CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

MERGE INTO "rbac_group" t
USING (
    SELECT
        UUID() AS "id",
        'system_admin' AS "group_code",
        "project",
        'System Admin (Bootstrap)' AS "group_name",
        '[]' AS "rule_codes",
        '[]' AS "permission_codes",
        'Active' AS "status"
    FROM "rbac_bootstrap_config"
) s
ON (t."group_code" = s."group_code" AND t."project" = s."project")
WHEN NOT MATCHED THEN
    INSERT ("id", "group_code", "project", "group_name", "parent_group_code",
            "rule_codes", "permission_codes", "status", "created_at", "updated_at")
    VALUES (s."id", s."group_code", s."project", s."group_name", NULL,
            s."rule_codes", s."permission_codes", s."status", CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

MERGE INTO "rbac_group_member" t
USING (
    SELECT
        UUID() AS "id",
        "userid",
        'system_admin' AS "group_code",
        "project",
        'bootstrap' AS "granted_by"
    FROM "rbac_bootstrap_config"
) s
ON (t."userid" = s."userid" AND t."group_code" = s."group_code" AND t."project" = s."project")
WHEN NOT MATCHED THEN
    INSERT ("id", "userid", "group_code", "project", "granted_by", "created_at", "updated_at")
    VALUES (s."id", s."userid", s."group_code", s."project", s."granted_by", CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

MERGE INTO "rbac_rule" t
USING (
    SELECT
        cfg."project",
        m."rule_code",
        m."permission_code",
        m."parent_rule_code",
        m."type",
        m."title",
        m."name",
        m."path",
        m."icon",
        m."menu_type",
        m."url",
        m."component",
        m."extend",
        m."remark",
        m."keepalive",
        m."weigh",
        m."status"
    FROM "rbac_bootstrap_config" cfg
    CROSS JOIN (
    SELECT 'auth' AS "rule_code", 'menu:auth' AS "permission_code", CAST(NULL AS VARCHAR2(128)) AS "parent_rule_code",
           'MenuDir' AS "type", '权限管理' AS "title", 'auth' AS "name", 'auth' AS "path",
           '' AS "icon", CAST(NULL AS VARCHAR2(16)) AS "menu_type", CAST(NULL AS VARCHAR2(512)) AS "url",
           CAST(NULL AS VARCHAR2(256)) AS "component", 'none' AS "extend", '' AS "remark",
           0 AS "keepalive", 10 AS "weigh", 'Active' AS "status" FROM dual
    UNION ALL SELECT 'auth/apiMap', 'menu:auth/apiMap', 'auth',
           'Menu', '端点授权', 'auth/apiMap', 'auth/apiMap',
           NULL, 'Tab', NULL,
           '/src/views/backend/auth/apiMap/index.vue', NULL, NULL,
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/projectGrant', 'menu:auth/projectGrant', 'auth',
           'Menu', '超管授权', 'auth/projectGrant', 'auth/projectGrant',
           NULL, 'Tab', NULL,
           '/src/views/backend/auth/projectGrant/index.vue', NULL, NULL,
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule', 'menu:auth/rule', 'auth',
           'Menu', '菜单规则管理', 'auth/rule', 'auth/rule',
           '', 'Tab', NULL,
           '/src/views/backend/auth/rule/index.vue', 'none', '',
           0, 97, 'Active' FROM dual
    UNION ALL SELECT 'auth/admin', 'menu:auth/admin', 'auth',
           'Menu', '管理员管理', 'auth/admin', 'auth/admin',
           '', 'Tab', NULL,
           '/src/views/backend/auth/admin/index.vue', 'none', '',
           0, 98, 'Active' FROM dual
    UNION ALL SELECT 'auth/group', 'menu:auth/group', 'auth',
           'Menu', '角色组管理', 'auth/group', 'auth/group',
           '', 'Tab', NULL,
           '/src/views/backend/auth/group/index.vue', 'none', '',
           0, 99, 'Active' FROM dual
    UNION ALL SELECT 'auth/admin/add', 'button:auth/admin/add', 'auth/admin',
           'Button', '添加', 'auth/admin/add', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/admin/del', 'button:auth/admin/del', 'auth/admin',
           'Button', '删除', 'auth/admin/del', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/admin/edit', 'button:auth/admin/edit', 'auth/admin',
           'Button', '编辑', 'auth/admin/edit', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/admin/index', 'button:auth/admin/index', 'auth/admin',
           'Button', '查看', 'auth/admin/index', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/group/add', 'button:auth/group/add', 'auth/group',
           'Button', '添加', 'auth/group/add', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/group/del', 'button:auth/group/del', 'auth/group',
           'Button', '删除', 'auth/group/del', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/group/edit', 'button:auth/group/edit', 'auth/group',
           'Button', '编辑', 'auth/group/edit', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/group/index', 'button:auth/group/index', 'auth/group',
           'Button', '查看', 'auth/group/index', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule/add', 'button:auth/rule/add', 'auth/rule',
           'Button', '添加', 'auth/rule/add', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule/del', 'button:auth/rule/del', 'auth/rule',
           'Button', '删除', 'auth/rule/del', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule/edit', 'button:auth/rule/edit', 'auth/rule',
           'Button', '编辑', 'auth/rule/edit', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule/index', 'button:auth/rule/index', 'auth/rule',
           'Button', '查看', 'auth/rule/index', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule/sortable', 'button:auth/rule/sortable', 'auth/rule',
           'Button', '快速排序', 'auth/rule/sortable', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'dashboard', 'menu:dashboard', CAST(NULL AS VARCHAR2(128)),
           'Menu', '首页', 'dashboard', 'dashboard',
           '', 'Tab', NULL,
           '/src/views/backend/dashboard.vue', 'none', '',
           0, 1, 'Active' FROM dual
    UNION ALL SELECT 'dashboard/index', 'button:dashboard/index', 'dashboard',
           'Button', '查看', 'dashboard/index', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    ) m
) s
ON (t."rule_code" = s."rule_code" AND t."project" = s."project")
WHEN MATCHED THEN
    UPDATE SET
        t."permission_code" = s."permission_code",
        t."parent_rule_code" = s."parent_rule_code",
        t."type" = s."type",
        t."title" = s."title",
        t."name" = s."name",
        t."path" = s."path",
        t."icon" = s."icon",
        t."menu_type" = s."menu_type",
        t."url" = s."url",
        t."component" = s."component",
        t."extend" = s."extend",
        t."remark" = s."remark",
        t."keepalive" = s."keepalive",
        t."weigh" = s."weigh",
        t."status" = s."status",
        t."updated_at" = CURRENT_TIMESTAMP
WHEN NOT MATCHED THEN
    INSERT ("id", "project", "rule_code", "permission_code", "parent_rule_code",
            "type", "title", "name", "path", "icon", "menu_type", "url",
            "component", "extend", "remark", "keepalive", "weigh", "status",
            "created_at", "updated_at")
    VALUES (UUID(), s."project", s."rule_code", s."permission_code", s."parent_rule_code",
            s."type", s."title", s."name", s."path", s."icon", s."menu_type", s."url",
            s."component", s."extend", s."remark", s."keepalive", s."weigh", s."status",
            CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

MERGE INTO "rbac_api_permission_map" t
USING (
    SELECT cfg."project", m."http_method", m."route_pattern", m."permission_code", m."action"
    FROM "rbac_bootstrap_config" cfg
    CROSS JOIN (
    SELECT 'GET' AS "http_method", '/api/admin/index' AS "route_pattern", 'menu:admin.index' AS "permission_code", 'access' AS "action" FROM dual
    UNION ALL SELECT 'GET',    '/api/admin/list',            'menu:admin.list',        'read'   FROM dual
    UNION ALL SELECT 'POST',   '/api/admin',                 'button:admin.create',    'create' FROM dual
    UNION ALL SELECT 'PUT',    '/api/admin/{userid}',        'button:admin.edit',      'update' FROM dual
    UNION ALL SELECT 'PUT',    '/api/admin/{userid}/status', 'button:admin.status',    'update' FROM dual
    UNION ALL SELECT 'PUT',    '/api/admin/{userid}/username','button:admin.username', 'update' FROM dual
    UNION ALL SELECT 'DELETE', '/api/admin/{userid}',        'button:admin.delete',    'delete' FROM dual
    UNION ALL SELECT 'GET',    '/api/group/list',                    'menu:group.list',         'read'   FROM dual
    UNION ALL SELECT 'POST',   '/api/group',                         'button:group.create',     'create' FROM dual
    UNION ALL SELECT 'PUT',    '/api/group/{groupCode}',             'button:group.edit',       'update' FROM dual
    UNION ALL SELECT 'PUT',    '/api/group/{groupCode}/rules',       'button:group.rules',      'update' FROM dual
    UNION ALL SELECT 'PUT',    '/api/group/{groupCode}/status',      'button:group.status',     'update' FROM dual
    UNION ALL SELECT 'POST',   '/api/group/{groupCode}/members',     'button:group.member.add', 'create' FROM dual
    UNION ALL SELECT 'DELETE', '/api/group/{groupCode}/members/{userid}', 'button:group.member.del', 'delete' FROM dual
    UNION ALL SELECT 'DELETE', '/api/group/{groupCode}',             'button:group.delete',     'delete' FROM dual
    UNION ALL SELECT 'GET',    '/api/rule/tree',           'menu:rule.tree',       'read'   FROM dual
    UNION ALL SELECT 'GET',    '/api/rule/list',           'menu:rule.list',       'read'   FROM dual
    UNION ALL SELECT 'POST',   '/api/rule',                'button:rule.create',   'create' FROM dual
    UNION ALL SELECT 'PUT',    '/api/rule/{ruleCode}',     'button:rule.edit',     'update' FROM dual
    UNION ALL SELECT 'PUT',    '/api/rule/{ruleCode}/status', 'button:rule.status','update' FROM dual
    UNION ALL SELECT 'PUT',    '/api/rule/{ruleCode}/weigh',  'button:rule.weigh', 'update' FROM dual
    UNION ALL SELECT 'DELETE', '/api/rule/{ruleCode}',     'button:rule.delete',   'delete' FROM dual
    UNION ALL SELECT 'GET',    '/api/api-map/list',        'menu:apimap.list',     'read'   FROM dual
    UNION ALL SELECT 'GET',    '/api/api-map/records',     'menu:apimap.list',     'read'   FROM dual
    UNION ALL SELECT 'POST',   '/api/api-map',             'button:apimap.create', 'create' FROM dual
    UNION ALL SELECT 'PUT',    '/api/api-map/{id}',        'button:apimap.edit',   'update' FROM dual
    UNION ALL SELECT 'DELETE', '/api/api-map/{id}',        'button:apimap.delete', 'delete' FROM dual
    UNION ALL SELECT 'POST',   '/api/project-grant',       'button:grant.create',  'create' FROM dual
    UNION ALL SELECT 'DELETE', '/api/project-grant/{userid}', 'button:grant.delete','delete' FROM dual
    UNION ALL SELECT 'PUT',    '/api/project-grant/{userid}/super', 'button:grant.super', 'update' FROM dual
    UNION ALL SELECT 'GET',    '/api/search/audit-logs',      'menu:search.audit',      'read' FROM dual
    UNION ALL SELECT 'GET',    '/api/search/permission-view', 'menu:search.permission', 'read' FROM dual
    ) m
) s
ON (
    t."project" = s."project"
    AND t."http_method" = s."http_method"
    AND t."route_pattern" = s."route_pattern"
)
WHEN NOT MATCHED THEN
    INSERT ("id", "project", "http_method", "route_pattern", "permission_code",
            "action", "status", "created_at", "updated_at")
    VALUES (UUID(), s."project", s."http_method", s."route_pattern",
            s."permission_code", s."action", 'Active', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

COMMIT;

SELECT 'administrator' AS table_name, COUNT(*) AS row_count
FROM "rbac_administrator"
WHERE "userid" = (SELECT "userid" FROM "rbac_bootstrap_config")
UNION ALL
SELECT 'project_grant', COUNT(*)
FROM "rbac_project_grant"
WHERE "userid" = (SELECT "userid" FROM "rbac_bootstrap_config")
  AND "project" = (SELECT "project" FROM "rbac_bootstrap_config")
UNION ALL
SELECT 'group', COUNT(*)
FROM "rbac_group"
WHERE "group_code" = 'system_admin'
  AND "project" = (SELECT "project" FROM "rbac_bootstrap_config")
UNION ALL
SELECT 'group_member', COUNT(*)
FROM "rbac_group_member"
WHERE "userid" = (SELECT "userid" FROM "rbac_bootstrap_config")
  AND "project" = (SELECT "project" FROM "rbac_bootstrap_config")
UNION ALL
SELECT 'rbac_rule', COUNT(*)
FROM "rbac_rule"
WHERE "project" = (SELECT "project" FROM "rbac_bootstrap_config")
  AND ("rule_code" = 'auth' OR "rule_code" LIKE 'auth/%'
       OR "rule_code" = 'dashboard' OR "rule_code" = 'dashboard/index')
UNION ALL
SELECT 'api_permission_map', COUNT(*)
FROM "rbac_api_permission_map"
WHERE "project" = (SELECT "project" FROM "rbac_bootstrap_config");

DROP TABLE "rbac_bootstrap_config";
