-- =============================================================
-- Unified Permission Center bootstrap seed data for Dameng DM8.
--
-- Purpose:
--   Initialize the reserved RBAC project "__global__". This project protects
--   the Unified Permission Center itself and owns the /api/global/* API
--   permission mappings.
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
        UUID() AS "id",
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
        UUID() AS "id",
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
        UUID() AS "id",
        'global_admins' AS "group_code",
        '__global__' AS "project",
        'Global Administrators' AS "group_name",
        '["global.console","global.project","global.user","global.group","global.menu"]' AS "rule_codes",
        '["rbac.global.admin","rbac.global.user.manage","rbac.global.group.manage","rbac.global.menu.manage"]' AS "permission_codes",
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
        UUID() AS "id",
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
    SELECT 'global.console' AS "rule_code", 'rbac.global.admin' AS "permission_code", CAST(NULL AS VARCHAR2(128)) AS "parent_rule_code",
           'MenuDir' AS "type", 'Unified Permission Center' AS "title", 'GlobalConsole' AS "name", '/global' AS "path",
           'Monitor' AS "icon", CAST(NULL AS VARCHAR2(16)) AS "menu_type", CAST(NULL AS VARCHAR2(512)) AS "url",
           'LAYOUT' AS "component", CAST(NULL AS VARCHAR2(64)) AS "extend", CAST(NULL AS VARCHAR2(512)) AS "remark",
           0 AS "keepalive", 0 AS "weigh", 'Active' AS "status" FROM dual
    UNION ALL SELECT 'global.project', 'rbac.global.admin', 'global.console',
           'Menu', 'Project List', 'GlobalProject', '/global/project',
           'Grid', 'Tab', NULL,
           'views/global/project/index', NULL, NULL,
           0, 5, 'Active' FROM dual
    UNION ALL SELECT 'global.user', 'rbac.global.user.manage', 'global.console',
           'Menu', 'Cross-Project Users', 'GlobalUser', '/global/user',
           'User', 'Tab', NULL,
           'views/global/user/index', NULL, NULL,
           0, 10, 'Active' FROM dual
    UNION ALL SELECT 'global.group', 'rbac.global.group.manage', 'global.console',
           'Menu', 'Cross-Project Groups', 'GlobalGroup', '/global/group',
           'UserGroup', 'Tab', NULL,
           'views/global/group/index', NULL, NULL,
           0, 20, 'Active' FROM dual
    UNION ALL SELECT 'global.menu', 'rbac.global.menu.manage', 'global.console',
           'Menu', 'Cross-Project Rules', 'GlobalMenu', '/global/menu',
           'Menu', 'Tab', NULL,
           'views/global/menu/index', NULL, NULL,
           0, 30, 'Active' FROM dual
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
    SELECT
        '__global__' AS "project",
        m."http_method",
        m."route_pattern",
        m."permission_code",
        m."action"
    FROM (
    SELECT 'GET' AS "http_method", '/api/global/project/list' AS "route_pattern", 'rbac.global.admin' AS "permission_code", 'access' AS "action" FROM dual
    UNION ALL SELECT 'GET', '/api/global/user/list', 'rbac.global.user.manage', 'access' FROM dual
    UNION ALL SELECT 'PUT', '/api/global/user/{userid}/status', 'rbac.global.user.manage', 'write' FROM dual
    UNION ALL SELECT 'POST', '/api/global/user/{userid}/project-grants', 'rbac.global.user.manage', 'write' FROM dual
    UNION ALL SELECT 'DELETE', '/api/global/user/{userid}/project-grants/{project}', 'rbac.global.user.manage', 'write' FROM dual
    UNION ALL SELECT 'GET', '/api/global/group/list', 'rbac.global.group.manage', 'access' FROM dual
    UNION ALL SELECT 'POST', '/api/global/group/{groupCode}/members', 'rbac.global.group.manage', 'write' FROM dual
    UNION ALL SELECT 'DELETE', '/api/global/group/{groupCode}/members/{userid}', 'rbac.global.group.manage', 'write' FROM dual
    UNION ALL SELECT 'GET', '/api/global/menu/list', 'rbac.global.menu.manage', 'access' FROM dual
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
    VALUES (UUID(), s."project", s."http_method", s."route_pattern",
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
SELECT 'global_rule', COUNT(*)
FROM "rbac_rule"
WHERE "project" = '__global__'
  AND "rule_code" LIKE 'global.%'
UNION ALL
SELECT 'global_api_permission_map', COUNT(*)
FROM "rbac_api_permission_map"
WHERE "project" = '__global__';

DROP TABLE "rbac_global_bootstrap_config";
