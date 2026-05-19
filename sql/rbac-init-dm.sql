-- =============================================================
-- RBAC schema for Dameng DM8.
--
-- Run after connecting as the target schema owner, for example:
-- E:\DM\bin\disql.exe SYSDBA/"""1q2w3e4R"""@192.168.124.2:5236
--
-- Notes:
-- - Identifiers are quoted and lowercase to match EF Core HasColumnName/ToTable.
-- - Guid keys are stored as varchar2(36); EF mapping converts Guid <-> string.
-- - DateTimeOffset columns use timestamp(6) with time zone.
-- - MySQL ON UPDATE behavior is implemented with triggers.
-- =============================================================

CREATE TABLE IF NOT EXISTS "rbac_administrator" (
    "id"         VARCHAR2(36) NOT NULL,
    "userid"     VARCHAR2(128) NOT NULL,
    "username"   VARCHAR2(128) NOT NULL,
    "status"     VARCHAR2(16) DEFAULT 'Active' NOT NULL,
    "created_at" TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "updated_at" TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT "pk_rbac_administrator" PRIMARY KEY ("id"),
    CONSTRAINT "ux_admin_userid" UNIQUE ("userid")
);

CREATE TABLE IF NOT EXISTS "rbac_group" (
    "id"                VARCHAR2(36) NOT NULL,
    "group_code"        VARCHAR2(128) NOT NULL,
    "project"           VARCHAR2(64) NOT NULL,
    "group_name"        VARCHAR2(128) NOT NULL,
    "parent_group_code" VARCHAR2(128) NULL,
    "rule_codes"        CLOB NOT NULL,
    "permission_codes"  CLOB NOT NULL,
    "status"            VARCHAR2(16) DEFAULT 'Active' NOT NULL,
    "created_at"        TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "updated_at"        TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT "pk_rbac_group" PRIMARY KEY ("id"),
    CONSTRAINT "ux_group_code_project" UNIQUE ("group_code", "project")
);

CREATE TABLE IF NOT EXISTS "rbac_group_member" (
    "id"         VARCHAR2(36) NOT NULL,
    "userid"     VARCHAR2(128) NOT NULL,
    "group_code" VARCHAR2(128) NOT NULL,
    "project"    VARCHAR2(64) NOT NULL,
    "granted_by" VARCHAR2(128) NOT NULL,
    "created_at" TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "updated_at" TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT "pk_rbac_group_member" PRIMARY KEY ("id"),
    CONSTRAINT "ux_group_member_userid_group_project" UNIQUE ("userid", "group_code", "project")
);

CREATE INDEX IF NOT EXISTS "ix_group_member_project_group"
    ON "rbac_group_member" ("project", "group_code");

CREATE INDEX IF NOT EXISTS "ix_group_member_userid"
    ON "rbac_group_member" ("userid");

CREATE TABLE IF NOT EXISTS "rbac_rule" (
    "id"               VARCHAR2(36) NOT NULL,
    "project"          VARCHAR2(64) NOT NULL,
    "rule_code"        VARCHAR2(128) NOT NULL,
    "permission_code"  VARCHAR2(256) NOT NULL,
    "parent_rule_code" VARCHAR2(128) NULL,
    "type"             VARCHAR2(16) NOT NULL,
    "title"            VARCHAR2(128) NOT NULL,
    "name"             VARCHAR2(128) NULL,
    "path"             VARCHAR2(256) NULL,
    "icon"             VARCHAR2(128) DEFAULT '' NULL,
    "menu_type"        VARCHAR2(16) NULL,
    "url"              VARCHAR2(512) NULL,
    "component"        VARCHAR2(256) NULL,
    "extend"           VARCHAR2(64) NULL,
    "remark"           VARCHAR2(512) DEFAULT '' NULL,
    "keepalive"        NUMBER(1) DEFAULT 0 NOT NULL,
    "weigh"            INT DEFAULT 0 NOT NULL,
    "status"           VARCHAR2(16) DEFAULT 'Active' NOT NULL,
    "created_at"       TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "updated_at"       TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT "pk_rbac_rule" PRIMARY KEY ("id"),
    CONSTRAINT "ux_rule_code_project" UNIQUE ("rule_code", "project")
);

CREATE INDEX IF NOT EXISTS "ix_rule_project_status"
    ON "rbac_rule" ("project", "status");

CREATE INDEX IF NOT EXISTS "ix_rule_parent_code"
    ON "rbac_rule" ("parent_rule_code");

CREATE TABLE IF NOT EXISTS "rbac_project_grant" (
    "id"         VARCHAR2(36) NOT NULL,
    "userid"     VARCHAR2(128) NOT NULL,
    "project"    VARCHAR2(64) NOT NULL,
    "is_super"   NUMBER(1) DEFAULT 0 NOT NULL,
    "granted_by" VARCHAR2(128) NOT NULL,
    "granted_at" TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "updated_at" TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT "pk_rbac_project_grant" PRIMARY KEY ("id"),
    CONSTRAINT "ux_grant_userid_project" UNIQUE ("userid", "project")
);

CREATE INDEX IF NOT EXISTS "ix_grant_userid"
    ON "rbac_project_grant" ("userid");

CREATE INDEX IF NOT EXISTS "ix_grant_project"
    ON "rbac_project_grant" ("project");

CREATE TABLE IF NOT EXISTS "rbac_api_permission_map" (
    "id"              VARCHAR2(36) NOT NULL,
    "project"         VARCHAR2(64) NOT NULL,
    "http_method"     VARCHAR2(8) NOT NULL,
    "route_pattern"   VARCHAR2(512) NOT NULL,
    "permission_code" VARCHAR2(256) NOT NULL,
    "action"          VARCHAR2(16) NOT NULL,
    "status"          VARCHAR2(16) DEFAULT 'Active' NOT NULL,
    "created_at"      TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "updated_at"      TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT "pk_rbac_api_permission_map" PRIMARY KEY ("id"),
    CONSTRAINT "ux_api_map_project_method_route" UNIQUE ("project", "http_method", "route_pattern")
);

CREATE INDEX IF NOT EXISTS "ix_api_map_project_status"
    ON "rbac_api_permission_map" ("project", "status");

CREATE TABLE IF NOT EXISTS "rbac_outbox" (
    "event_id"      VARCHAR2(64) NOT NULL,
    "event_type"    VARCHAR2(64) NOT NULL,
    "project"       VARCHAR2(64) NOT NULL,
    "userid"        VARCHAR2(128) NULL,
    "group_code"    VARCHAR2(128) NULL,
    "payload"       CLOB NOT NULL,
    "status"        VARCHAR2(16) DEFAULT 'Pending' NOT NULL,
    "retry_count"   INT DEFAULT 0 NOT NULL,
    "next_retry_at" TIMESTAMP(6) WITH TIME ZONE NULL,
    "created_at"    TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
    "processed_at"  TIMESTAMP(6) WITH TIME ZONE NULL,
    CONSTRAINT "pk_rbac_outbox" PRIMARY KEY ("event_id")
);

CREATE INDEX IF NOT EXISTS "ix_outbox_status_retry"
    ON "rbac_outbox" ("status", "next_retry_at");

CREATE INDEX IF NOT EXISTS "ix_outbox_status"
    ON "rbac_outbox" ("status");

CREATE INDEX IF NOT EXISTS "ix_outbox_created_at"
    ON "rbac_outbox" ("created_at");

CREATE OR REPLACE TRIGGER "trg_rbac_administrator_uat"
BEFORE UPDATE ON "rbac_administrator"
FOR EACH ROW
BEGIN
    :new."updated_at" := CURRENT_TIMESTAMP;
END;
/

CREATE OR REPLACE TRIGGER "trg_rbac_group_uat"
BEFORE UPDATE ON "rbac_group"
FOR EACH ROW
BEGIN
    :new."updated_at" := CURRENT_TIMESTAMP;
END;
/

CREATE OR REPLACE TRIGGER "trg_rbac_group_member_uat"
BEFORE UPDATE ON "rbac_group_member"
FOR EACH ROW
BEGIN
    :new."updated_at" := CURRENT_TIMESTAMP;
END;
/

CREATE OR REPLACE TRIGGER "trg_rbac_rule_uat"
BEFORE UPDATE ON "rbac_rule"
FOR EACH ROW
BEGIN
    :new."updated_at" := CURRENT_TIMESTAMP;
END;
/

CREATE OR REPLACE TRIGGER "trg_rbac_project_grant_uat"
BEFORE UPDATE ON "rbac_project_grant"
FOR EACH ROW
BEGIN
    :new."updated_at" := CURRENT_TIMESTAMP;
END;
/

CREATE OR REPLACE TRIGGER "trg_rbac_api_permission_map_uat"
BEFORE UPDATE ON "rbac_api_permission_map"
FOR EACH ROW
BEGIN
    :new."updated_at" := CURRENT_TIMESTAMP;
END;
/

COMMIT;

SELECT table_name
FROM user_tables
WHERE table_name LIKE 'rbac_%'
ORDER BY table_name;
