-- =============================================================
-- Unified Permission Center bootstrap seed data for Dameng DM8.
--
-- Purpose:
--   Initialize the reserved RBAC project "__global__". This project protects
--   the Unified Permission Center itself and owns the /api/global/* API
--   RBAC management API mappings for the "__global__" system itself.
--   permission mappings. It also seeds the baseline dashboard/auth menus and
--
-- Usage:
--   Execute this only when deploying or repairing the Unified Permission
--   Center. Ordinary business-project bootstrap does not need this script.
--
-- Configuration:
--   Change the userid below to the first global administrator's employee id.
-- =============================================================

CREATE TABLE IF NOT EXISTS "rbac_global_bootstrap_config" (
    "userid" VARCHAR2(128) NOT NULL
);

DELETE FROM "rbac_global_bootstrap_config";

INSERT INTO "rbac_global_bootstrap_config" ("userid")
VALUES ('196045');

MERGE INTO "rbac_administrator" t
USING (
    SELECT
        LOWER(REGEXP_REPLACE(GUID(), '([0-9A-F]{8})([0-9A-F]{4})([0-9A-F]{4})([0-9A-F]{4})([0-9A-F]{12})', '\1-\2-\3-\4-\5')) AS "id",
        "userid",
        'Global Bootstrap Admin' AS "username",
        'Active' AS "status"
    FROM "rbac_global_bootstrap_config"
) s
ON (t."userid" = s."userid")
WHEN NOT MATCHED THEN
    INSERT ("id", "userid", "username", "status", "created_at", "updated_at")
    VALUES (s."id", s."userid", s."username", s."status", CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

MERGE INTO "rbac_project_grant" t
USING (
    SELECT
        LOWER(REGEXP_REPLACE(GUID(), '([0-9A-F]{8})([0-9A-F]{4})([0-9A-F]{4})([0-9A-F]{4})([0-9A-F]{12})', '\1-\2-\3-\4-\5')) AS "id",
        "userid",
        '__global__' AS "project",
        1 AS "is_super",
        'global-bootstrap' AS "granted_by"
    FROM "rbac_global_bootstrap_config"
) s
ON (t."userid" = s."userid" AND t."project" = s."project")
WHEN NOT MATCHED THEN
    INSERT ("id", "userid", "project", "is_super", "granted_by", "granted_at", "updated_at")
    VALUES (s."id", s."userid", s."project", s."is_super", s."granted_by", CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

MERGE INTO "rbac_group" t
USING (
    SELECT
        LOWER(REGEXP_REPLACE(GUID(), '([0-9A-F]{8})([0-9A-F]{4})([0-9A-F]{4})([0-9A-F]{4})([0-9A-F]{12})', '\1-\2-\3-\4-\5')) AS "id",
        'global_admins' AS "group_code",
        '__global__' AS "project",
        'Global Administrators' AS "group_name",
        '["dashboard","dashboard/index","auth","auth/apiMap","auth/projectGrant","auth/rule","auth/admin","auth/group","auth/admin/add","auth/admin/del","auth/admin/edit","auth/admin/index","auth/group/add","auth/group/del","auth/group/edit","auth/group/index","auth/rule/add","auth/rule/del","auth/rule/edit","auth/rule/index","auth/rule/sortable"]' AS "rule_codes",
        '["menu:dashboard","button:dashboard/index","menu:auth","menu:auth/apiMap","menu:auth/projectGrant","menu:auth/rule","menu:auth/admin","menu:auth/group","button:auth/admin/add","button:auth/admin/del","button:auth/admin/edit","button:auth/admin/index","button:auth/group/add","button:auth/group/del","button:auth/group/edit","button:auth/group/index","button:auth/rule/add","button:auth/rule/del","button:auth/rule/edit","button:auth/rule/index","button:auth/rule/sortable","menu:admin.index","menu:admin.list","button:admin.create","button:admin.edit","button:admin.status","button:admin.username","button:admin.delete","menu:group.list","button:group.create","button:group.edit","button:group.rules","button:group.status","button:group.member.add","button:group.member.del","button:group.delete","auth.group","menu:rule.tree","menu:rule.list","button:rule.create","button:rule.edit","button:rule.status","button:rule.weigh","button:rule.delete","menu:apimap.list","button:apimap.create","button:apimap.edit","button:apimap.delete","button:grant.create","button:grant.delete","button:grant.super","menu:search.audit","menu:search.permission","rbac.global.admin","rbac.global.user.manage","rbac.global.group.manage","rbac.global.menu.manage"]' AS "permission_codes",
        'Active' AS "status"
    FROM dual
) s
ON (t."group_code" = s."group_code" AND t."project" = s."project")
WHEN MATCHED THEN
    UPDATE SET
        t."group_name" = s."group_name",
        t."rule_codes" = s."rule_codes",
        t."permission_codes" = s."permission_codes",
        t."status" = s."status",
        t."updated_at" = CURRENT_TIMESTAMP
WHEN NOT MATCHED THEN
    INSERT ("id", "group_code", "project", "group_name", "parent_group_code",
            "rule_codes", "permission_codes", "status", "created_at", "updated_at")
    VALUES (s."id", s."group_code", s."project", s."group_name", NULL,
            s."rule_codes", s."permission_codes", s."status", CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

MERGE INTO "rbac_group_member" t
USING (
    SELECT
        LOWER(REGEXP_REPLACE(GUID(), '([0-9A-F]{8})([0-9A-F]{4})([0-9A-F]{4})([0-9A-F]{4})([0-9A-F]{12})', '\1-\2-\3-\4-\5')) AS "id",
        "userid",
        'global_admins' AS "group_code",
        '__global__' AS "project",
        'global-bootstrap' AS "granted_by"
    FROM "rbac_global_bootstrap_config"
) s
ON (t."userid" = s."userid" AND t."group_code" = s."group_code" AND t."project" = s."project")
WHEN NOT MATCHED THEN
    INSERT ("id", "userid", "group_code", "project", "granted_by", "created_at", "updated_at")
    VALUES (s."id", s."userid", s."group_code", s."project", s."granted_by", CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

MERGE INTO "rbac_rule" t
USING (
    SELECT
        '__global__' AS "project",
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
    FROM (
    SELECT 'auth' AS "rule_code", 'menu:auth' AS "permission_code", CAST(NULL AS VARCHAR2(128)) AS "parent_rule_code",
           'MenuDir' AS "type", 'Permission Management' AS "title", 'auth' AS "name", 'auth' AS "path",
           '' AS "icon", CAST(NULL AS VARCHAR2(16)) AS "menu_type", CAST(NULL AS VARCHAR2(512)) AS "url",
           CAST(NULL AS VARCHAR2(256)) AS "component", 'none' AS "extend", '' AS "remark",
           0 AS "keepalive", 10 AS "weigh", 'Active' AS "status" FROM dual
    UNION ALL SELECT 'auth/apiMap', 'menu:auth/apiMap', 'auth',
           'Menu', 'API Permission Map', 'auth/apiMap', 'auth/apiMap',
           NULL, 'Tab', NULL,
           '/src/views/backend/auth/apiMap/index.vue', NULL, NULL,
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/projectGrant', 'menu:auth/projectGrant', 'auth',
           'Menu', 'Project Grants', 'auth/projectGrant', 'auth/projectGrant',
           NULL, 'Tab', NULL,
           '/src/views/backend/auth/projectGrant/index.vue', NULL, NULL,
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule', 'menu:auth/rule', 'auth',
           'Menu', 'Menu Rules', 'auth/rule', 'auth/rule',
           '', 'Tab', NULL,
           '/src/views/backend/auth/rule/index.vue', 'none', '',
           0, 97, 'Active' FROM dual
    UNION ALL SELECT 'auth/admin', 'menu:auth/admin', 'auth',
           'Menu', 'Administrators', 'auth/admin', 'auth/admin',
           '', 'Tab', NULL,
           '/src/views/backend/auth/admin/index.vue', 'none', '',
           0, 98, 'Active' FROM dual
    UNION ALL SELECT 'auth/group', 'menu:auth/group', 'auth',
           'Menu', 'Permission Groups', 'auth/group', 'auth/group',
           '', 'Tab', NULL,
           '/src/views/backend/auth/group/index.vue', 'none', '',
           0, 99, 'Active' FROM dual
    UNION ALL SELECT 'auth/admin/add', 'button:auth/admin/add', 'auth/admin',
           'Button', 'Add', 'auth/admin/add', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/admin/del', 'button:auth/admin/del', 'auth/admin',
           'Button', 'Delete', 'auth/admin/del', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/admin/edit', 'button:auth/admin/edit', 'auth/admin',
           'Button', 'Edit', 'auth/admin/edit', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/admin/index', 'button:auth/admin/index', 'auth/admin',
           'Button', 'View', 'auth/admin/index', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/group/add', 'button:auth/group/add', 'auth/group',
           'Button', 'Add', 'auth/group/add', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/group/del', 'button:auth/group/del', 'auth/group',
           'Button', 'Delete', 'auth/group/del', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/group/edit', 'button:auth/group/edit', 'auth/group',
           'Button', 'Edit', 'auth/group/edit', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/group/index', 'button:auth/group/index', 'auth/group',
           'Button', 'View', 'auth/group/index', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule/add', 'button:auth/rule/add', 'auth/rule',
           'Button', 'Add', 'auth/rule/add', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule/del', 'button:auth/rule/del', 'auth/rule',
           'Button', 'Delete', 'auth/rule/del', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule/edit', 'button:auth/rule/edit', 'auth/rule',
           'Button', 'Edit', 'auth/rule/edit', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule/index', 'button:auth/rule/index', 'auth/rule',
           'Button', 'View', 'auth/rule/index', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule/sortable', 'button:auth/rule/sortable', 'auth/rule',
           'Button', 'Sort', 'auth/rule/sortable', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'dashboard', 'menu:dashboard', CAST(NULL AS VARCHAR2(128)),
           'Menu', 'Dashboard', 'dashboard', 'dashboard',
           '', 'Tab', NULL,
           '/src/views/backend/dashboard.vue', 'none', '',
           0, 1, 'Active' FROM dual
    UNION ALL SELECT 'dashboard/index', 'button:dashboard/index', 'dashboard',
           'Button', 'View', 'dashboard/index', '',
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
    VALUES (LOWER(REGEXP_REPLACE(GUID(), '([0-9A-F]{8})([0-9A-F]{4})([0-9A-F]{4})([0-9A-F]{4})([0-9A-F]{12})', '\1-\2-\3-\4-\5')), s."project", s."rule_code", s."permission_code", s."parent_rule_code",
            s."type", s."title", s."name", s."path", s."icon", s."menu_type", s."url",
            s."component", s."extend", s."remark", s."keepalive", s."weigh", s."status",
            CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

MERGE INTO "rbac_api_permission_map" t
USING (
    SELECT
        '__global__' AS "project",
        m."http_method",
        m."route_pattern",
        m."permission_code",
        m."action"
    FROM (
    SELECT 'GET' AS "http_method", '/api/global/project/list' AS "route_pattern", 'rbac.global.admin' AS "permission_code", 'access' AS "action" FROM dual
    UNION ALL SELECT 'GET', '/api/global/user/list', 'rbac.global.user.manage', 'access' FROM dual
    UNION ALL SELECT 'POST', '/api/global/user', 'rbac.global.user.manage', 'write' FROM dual
    UNION ALL SELECT 'PUT', '/api/global/user/{userid}', 'rbac.global.user.manage', 'write' FROM dual
    UNION ALL SELECT 'PUT', '/api/global/user/{userid}/status', 'rbac.global.user.manage', 'write' FROM dual
    UNION ALL SELECT 'DELETE', '/api/global/user/{userid}', 'rbac.global.user.manage', 'write' FROM dual
    UNION ALL SELECT 'POST', '/api/global/user/{userid}/project-grants', 'rbac.global.user.manage', 'write' FROM dual
    UNION ALL SELECT 'PUT', '/api/global/user/{userid}/project-grants/{project}/super', 'rbac.global.user.manage', 'write' FROM dual
    UNION ALL SELECT 'DELETE', '/api/global/user/{userid}/project-grants/{project}', 'rbac.global.user.manage', 'write' FROM dual
    UNION ALL SELECT 'GET', '/api/global/group/list', 'rbac.global.group.manage', 'access' FROM dual
    UNION ALL SELECT 'POST', '/api/global/group', 'rbac.global.group.manage', 'write' FROM dual
    UNION ALL SELECT 'PUT', '/api/global/group/{groupCode}', 'rbac.global.group.manage', 'write' FROM dual
    UNION ALL SELECT 'DELETE', '/api/global/group/{groupCode}', 'rbac.global.group.manage', 'write' FROM dual
    UNION ALL SELECT 'POST', '/api/global/group/{groupCode}/members', 'rbac.global.group.manage', 'write' FROM dual
    UNION ALL SELECT 'DELETE', '/api/global/group/{groupCode}/members/{userid}', 'rbac.global.group.manage', 'write' FROM dual
    UNION ALL SELECT 'GET', '/api/global/menu/list', 'rbac.global.menu.manage', 'access' FROM dual
    UNION ALL SELECT 'POST', '/api/global/menu', 'rbac.global.menu.manage', 'write' FROM dual
    UNION ALL SELECT 'PUT', '/api/global/menu/{ruleCode}', 'rbac.global.menu.manage', 'write' FROM dual
    UNION ALL SELECT 'DELETE', '/api/global/menu/{ruleCode}', 'rbac.global.menu.manage', 'write' FROM dual
    ) m
) s
ON (
    t."project" = s."project"
    AND t."http_method" = s."http_method"
    AND t."route_pattern" = s."route_pattern"
)
WHEN MATCHED THEN
    UPDATE SET
        t."permission_code" = s."permission_code",
        t."action" = s."action",
        t."status" = 'Active',
        t."updated_at" = CURRENT_TIMESTAMP
WHEN NOT MATCHED THEN
    INSERT ("id", "project", "http_method", "route_pattern", "permission_code",
            "action", "status", "created_at", "updated_at")
    VALUES (LOWER(REGEXP_REPLACE(GUID(), '([0-9A-F]{8})([0-9A-F]{4})([0-9A-F]{4})([0-9A-F]{4})([0-9A-F]{12})', '\1-\2-\3-\4-\5')), s."project", s."http_method", s."route_pattern",
            s."permission_code", s."action", 'Active', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

MERGE INTO "rbac_api_permission_map" t
USING (
    SELECT
        '__global__' AS "project",
        m."http_method",
        m."route_pattern",
        m."permission_code",
        m."action"
    FROM (
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
    UNION ALL SELECT 'GET',    '/api/group/index',                   'auth.group',              'read'   FROM dual
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
WHEN MATCHED THEN
    UPDATE SET
        t."permission_code" = s."permission_code",
        t."action" = s."action",
        t."status" = 'Active',
        t."updated_at" = CURRENT_TIMESTAMP
WHEN NOT MATCHED THEN
    INSERT ("id", "project", "http_method", "route_pattern", "permission_code",
            "action", "status", "created_at", "updated_at")
    VALUES (LOWER(REGEXP_REPLACE(GUID(), '([0-9A-F]{8})([0-9A-F]{4})([0-9A-F]{4})([0-9A-F]{4})([0-9A-F]{12})', '\1-\2-\3-\4-\5')), s."project", s."http_method", s."route_pattern",
            s."permission_code", s."action", 'Active', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);

COMMIT;

SELECT 'global_project_grant' AS table_name, COUNT(*) AS row_count
FROM "rbac_project_grant"
WHERE "userid" = (SELECT "userid" FROM "rbac_global_bootstrap_config")
  AND "project" = '__global__'
UNION ALL
SELECT 'global_group', COUNT(*)
FROM "rbac_group"
WHERE "group_code" = 'global_admins'
  AND "project" = '__global__'
UNION ALL
SELECT 'global_group_member', COUNT(*)
FROM "rbac_group_member"
WHERE "userid" = (SELECT "userid" FROM "rbac_global_bootstrap_config")
  AND "group_code" = 'global_admins'
  AND "project" = '__global__'
UNION ALL
SELECT 'global_base_rule', COUNT(*)
FROM "rbac_rule"
WHERE "project" = '__global__'
  AND ("rule_code" = 'auth' OR "rule_code" LIKE 'auth/%'
       OR "rule_code" = 'dashboard' OR "rule_code" = 'dashboard/index')
UNION ALL
SELECT 'global_api_permission_map', COUNT(*)
FROM "rbac_api_permission_map"
WHERE "project" = '__global__'
  AND "route_pattern" LIKE '/api/global/%'
UNION ALL
SELECT 'global_base_api_permission_map', COUNT(*)
FROM "rbac_api_permission_map"
WHERE "project" = '__global__'
  AND "route_pattern" NOT LIKE '/api/global/%';

DROP TABLE "rbac_global_bootstrap_config";
