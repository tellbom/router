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
           'MenuDir' AS "type", UNISTR('\6743\9650\7BA1\7406') AS "title", 'auth' AS "name", 'auth' AS "path",
           '' AS "icon", CAST(NULL AS VARCHAR2(16)) AS "menu_type", CAST(NULL AS VARCHAR2(512)) AS "url",
           CAST(NULL AS VARCHAR2(256)) AS "component", 'none' AS "extend", '' AS "remark",
           0 AS "keepalive", 10 AS "weigh", 'Active' AS "status" FROM dual
    UNION ALL SELECT 'auth/apiMap', 'menu:auth/apiMap', 'auth',
           'Menu', UNISTR('\7AEF\70B9\6388\6743'), 'auth/apiMap', 'auth/apiMap',
           NULL, 'Tab', NULL,
           '/src/views/backend/auth/apiMap/index.vue', NULL, NULL,
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/projectGrant', 'menu:auth/projectGrant', 'auth',
           'Menu', UNISTR('\8D85\7BA1\6388\6743'), 'auth/projectGrant', 'auth/projectGrant',
           NULL, 'Tab', NULL,
           '/src/views/backend/auth/projectGrant/index.vue', NULL, NULL,
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule', 'menu:auth/rule', 'auth',
           'Menu', UNISTR('\83DC\5355\89C4\5219\7BA1\7406'), 'auth/rule', 'auth/rule',
           '', 'Tab', NULL,
           '/src/views/backend/auth/rule/index.vue', 'none', '',
           0, 97, 'Active' FROM dual
    UNION ALL SELECT 'auth/admin', 'menu:auth/admin', 'auth',
           'Menu', UNISTR('\7BA1\7406\5458\7BA1\7406'), 'auth/admin', 'auth/admin',
           '', 'Tab', NULL,
           '/src/views/backend/auth/admin/index.vue', 'none', '',
           0, 98, 'Active' FROM dual
    UNION ALL SELECT 'auth/group', 'menu:auth/group', 'auth',
           'Menu', UNISTR('\89D2\8272\7EC4\7BA1\7406'), 'auth/group', 'auth/group',
           '', 'Tab', NULL,
           '/src/views/backend/auth/group/index.vue', 'none', '',
           0, 99, 'Active' FROM dual
    UNION ALL SELECT 'auth/admin/add', 'button:auth/admin/add', 'auth/admin',
           'Button', UNISTR('\6DFB\52A0'), 'auth/admin/add', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/admin/del', 'button:auth/admin/del', 'auth/admin',
           'Button', UNISTR('\5220\9664'), 'auth/admin/del', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/admin/edit', 'button:auth/admin/edit', 'auth/admin',
           'Button', UNISTR('\7F16\8F91'), 'auth/admin/edit', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/admin/index', 'button:auth/admin/index', 'auth/admin',
           'Button', UNISTR('\67E5\770B'), 'auth/admin/index', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/group/add', 'button:auth/group/add', 'auth/group',
           'Button', UNISTR('\6DFB\52A0'), 'auth/group/add', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/group/del', 'button:auth/group/del', 'auth/group',
           'Button', UNISTR('\5220\9664'), 'auth/group/del', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/group/edit', 'button:auth/group/edit', 'auth/group',
           'Button', UNISTR('\7F16\8F91'), 'auth/group/edit', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/group/index', 'button:auth/group/index', 'auth/group',
           'Button', UNISTR('\67E5\770B'), 'auth/group/index', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule/add', 'button:auth/rule/add', 'auth/rule',
           'Button', UNISTR('\6DFB\52A0'), 'auth/rule/add', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule/del', 'button:auth/rule/del', 'auth/rule',
           'Button', UNISTR('\5220\9664'), 'auth/rule/del', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule/edit', 'button:auth/rule/edit', 'auth/rule',
           'Button', UNISTR('\7F16\8F91'), 'auth/rule/edit', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule/index', 'button:auth/rule/index', 'auth/rule',
           'Button', UNISTR('\67E5\770B'), 'auth/rule/index', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'auth/rule/sortable', 'button:auth/rule/sortable', 'auth/rule',
           'Button', UNISTR('\5FEB\901F\6392\5E8F'), 'auth/rule/sortable', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'buildadmin', 'menu:buildadmin', CAST(NULL AS VARCHAR2(128)),
           'Menu', 'BuildAdmin', 'buildadmin', 'buildadmin',
           '', 'Link', 'https://doc.buildadmin.com',
           CAST(NULL AS VARCHAR2(256)), 'none', '',
           0, 0, 'Disabled' FROM dual
    UNION ALL SELECT 'dashboard/index', 'button:dashboard/index', 'dashboard',
           'Button', UNISTR('\67E5\770B'), 'dashboard/index', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'inspection/briefing', 'menu:inspection/briefing', 'inspection',
           'Menu', UNISTR('\5DE1\524D\901A\62A5'), 'inspection/briefing', 'inspection/briefing',
           '', 'Tab', NULL,
           '/src/views/backend/inspection/briefing/PreInspectionBriefingPage.vue', 'none', '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'inspection/plan', 'menu:inspection/plan', 'inspection',
           'Menu', UNISTR('\5DE1\5BDF\8BA1\5212'), 'inspection/plan', 'inspection/plan',
           '', 'Tab', NULL,
           '/src/views/backend/inspection/plan/Inspectionplan.vue', 'none', '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'inspection/secondment', 'menu:inspection/secondment', 'inspection',
           'Menu', UNISTR('\4EBA\5458\9009\8C03'), 'inspection/secondment', 'inspection/secondment',
           '', 'Tab', NULL,
           '/src/views/backend/inspection/secondment/SecondmentView.vue', 'none', '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'inspectionprep/lnspectionpreppage', 'menu:inspectionprep/lnspectionpreppage', 'inspection',
           'Menu', UNISTR('\5DE1\5BDF\51C6\5907'), 'inspectionprep/lnspectionpreppage', 'inspectionprep/lnspectionpreppage',
           '', 'Tab', NULL,
           '/src/views/backend/inspection/inspectionprep/Inspectionpreppage.vue', 'none', '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'issue/page/add', 'button:issue/page/add', 'issue/page',
           'Button', UNISTR('\95EE\9898\5165\5E93'), 'issue/page/add', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'issue/page/view', 'button:issue/page/view', 'issue/page',
           'Button', UNISTR('\95EE\9898\67E5\770B'), 'issue/page/view', '',
           '', NULL, NULL,
           NULL, NULL, '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'talentpool/pool', 'menu:talentpool/pool', 'talent',
           'Menu', UNISTR('\5DE1\5BDF\961F\4F0D'), 'talentpool/pool', 'talentpool/pool',
           '', 'Tab', NULL,
           '/src/views/backend/talentpool/pool/TalentPoolPage.vue', 'none', '',
           0, 0, 'Active' FROM dual
    UNION ALL SELECT 'dashboard', 'menu:dashboard', CAST(NULL AS VARCHAR2(128)),
           'Menu', UNISTR('\9996\9875'), 'dashboard', 'dashboard',
           '', 'Tab', NULL,
           '/src/views/backend/dashboard.vue', 'none', '',
           0, 1, 'Active' FROM dual
    UNION ALL SELECT 'dailybulletin/dailybulletinpage', 'menu:dailybulletin/dailybulletinpage', CAST(NULL AS VARCHAR2(128)),
           'Menu', UNISTR('\65E5\5E38\901A\62A5'), 'dailybulletin/dailybulletinpage', 'dailybulletin/dailybulletinpage',
           '', 'Tab', NULL,
           '/src/views/backend/dailybulletin/DailyBulletinPage.vue', 'none', '',
           0, 20, 'Active' FROM dual
    UNION ALL SELECT 'supervisionissue/page', 'menu:supervisionissue/page', CAST(NULL AS VARCHAR2(128)),
           'Menu', UNISTR('\4E0A\7EA7\5DE1\89C6'), 'supervisionissue/page', 'supervisionissue/page',
           '', 'Tab', NULL,
           '/src/views/backend/supervisionissue/Supervisionissuepage.vue', 'none', '',
           0, 30, 'Active' FROM dual
    UNION ALL SELECT 'inspection', 'menu:inspection', CAST(NULL AS VARCHAR2(128)),
           'MenuDir', UNISTR('\5DE1\5BDF\8BA1\5212\7BA1\7406'), 'inspection', '',
           '', 'Tab', NULL,
           NULL, 'none', '',
           0, 80, 'Active' FROM dual
    UNION ALL SELECT 'issue/page', 'menu:issue/page', CAST(NULL AS VARCHAR2(128)),
           'Menu', UNISTR('\95EE\9898\6574\6539\5E93'), 'issue/page', 'issue/page',
           '', 'Tab', NULL,
           '/src/views/backend/issue/issuepage.vue', 'none', '',
           0, 80, 'Active' FROM dual
    UNION ALL SELECT 'inspectionprocess/page', 'menu:inspectionprocess/page', CAST(NULL AS VARCHAR2(128)),
           'Menu', UNISTR('\5DE1\5BDF\8FC7\7A0B\7BA1\63A7'), 'inspectionprocess/page', 'inspectionprocess/page',
           '', 'Tab', NULL,
           '/src/views/backend/inspectionprocess/Inspectionprocesspage.vue', 'none', '',
           0, 90, 'Active' FROM dual
    UNION ALL SELECT 'process', 'menu:process', CAST(NULL AS VARCHAR2(128)),
           'MenuDir', UNISTR('\5F85\529E\4E2D\5FC3'), 'process', 'process',
           '', 'Tab', NULL,
           NULL, 'none', '',
           0, 90, 'Active' FROM dual
    UNION ALL SELECT 'process/myapplication', 'menu:process/myapplication', 'process',
           'Menu', UNISTR('\6211\7684\7533\8BF7'), 'process/myapplication', 'process/myapplication',
           '', 'Tab', NULL,
           '/src/views/backend/todo/MyApplication.vue', 'none', '',
           0, 91, 'Active' FROM dual
    UNION ALL SELECT 'process/mytodo', 'menu:process/mytodo', 'process',
           'Menu', UNISTR('\6211\7684\5F85\529E'), 'process/mytodo', 'process/mytodo',
           '', 'Tab', NULL,
           '/src/views/backend/todo/MyTodo.vue', 'none', '',
           0, 94, 'Active' FROM dual
    UNION ALL SELECT 'talent', 'menu:talent', CAST(NULL AS VARCHAR2(128)),
           'MenuDir', UNISTR('\4EBA\624D\961F\4F0D'), 'talent', 'talent',
           '', 'Tab', NULL,
           NULL, 'none', '',
           0, 100, 'Active' FROM dual
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
UNION ALL
SELECT 'api_permission_map', COUNT(*)
FROM "rbac_api_permission_map"
WHERE "project" = (SELECT "project" FROM "rbac_bootstrap_config");

DROP TABLE "rbac_bootstrap_config";
