-- =============================================================
-- RBAC 权限中心 — 数据库初始化 SQL
--
-- 说明：
--   字段名、长度、索引名均来自 EF Core Mapping 配置，与代码严格对齐。
--   项目约定禁止使用 dotnet ef migrations，Schema 由本脚本管理。
--   执行顺序：按脚本顺序依次执行，不可颠倒（无外键约束，顺序无依赖）。
--
-- 数据库要求：MySQL 8.0+ 或 MariaDB 10.6+
-- 字符集：utf8mb4 / utf8mb4_unicode_ci（支持 emoji）
-- 存储引擎：InnoDB
-- =============================================================

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

-- =============================================================
-- 1. rbac_administrator — 管理员账号
--    Domain:  RbacAdministrator
--    Mapping: AdministratorMapping
-- =============================================================
CREATE TABLE IF NOT EXISTS `rbac_administrator` (
    `id`         CHAR(36)     NOT NULL                 COMMENT '内部主键 (Guid，不对外暴露)',
    `dxe_id`     VARCHAR(64)  NOT NULL                 COMMENT '前端兼容业务 ID（雪花 ID，始终作为 string 返回）',
    `userid`     VARCHAR(128) NOT NULL                 COMMENT '用户业务 ID（来自 JWT / 公司门户）',
    `username`   VARCHAR(128) NOT NULL                 COMMENT '显示名称',
    `status`     VARCHAR(16)  NOT NULL DEFAULT 'Active' COMMENT '账号状态：Active / Disabled',
    `created_at` DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) COMMENT '创建时间（UTC）',
    `updated_at` DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
                              ON UPDATE CURRENT_TIMESTAMP(6) COMMENT '最近更新时间（UTC）',

    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_admin_dxe_id` (`dxe_id`),
    UNIQUE KEY `ux_admin_userid` (`userid`)

) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci
  COMMENT = '管理员账号（RBAC 主体）';


-- =============================================================
-- 2. rbac_group — 权限组
--    Domain:  RbacGroup
--    Mapping: GroupMapping
--    注意：rule_codes / permission_codes 存储为 JSON 数组字符串
-- =============================================================
CREATE TABLE IF NOT EXISTS `rbac_group` (
    `id`                CHAR(36)      NOT NULL                  COMMENT '内部主键 (Guid)',
    `dxe_id`            VARCHAR(64)   NOT NULL                  COMMENT '前端兼容业务 ID',
    `group_code`        VARCHAR(128)  NOT NULL                  COMMENT '权限组编码（project 内唯一）',
    `project`           VARCHAR(64)   NOT NULL                  COMMENT '所属 project',
    `group_name`        VARCHAR(128)  NOT NULL                  COMMENT '权限组显示名称',
    `parent_group_code` VARCHAR(128)  NULL                      COMMENT '父级权限组编码（根组为 NULL）',
    `rule_codes`        TEXT          NOT NULL                  COMMENT '规则码列表（JSON 数组，如 ["system.user","system.group"]）',
    `permission_codes`  TEXT          NOT NULL                  COMMENT '权限码列表（JSON 数组，Casbin p policy 来源）',
    `status`            VARCHAR(16)   NOT NULL DEFAULT 'Active' COMMENT '状态：Active / Disabled',
    `created_at`        DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `updated_at`        DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
                                      ON UPDATE CURRENT_TIMESTAMP(6),

    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_group_dxe_id` (`dxe_id`),
    UNIQUE KEY `ux_group_code_project` (`group_code`, `project`)

) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci
  COMMENT = '权限组（用户-权限码的中间层）';


-- =============================================================
-- 3. rbac_group_member — 用户-权限组关联（Casbin g policy 真相表）
--    Domain:  RbacGroupMember（wave-grouping 补丁新增）
--    Mapping: GroupMemberMapping
-- =============================================================
CREATE TABLE IF NOT EXISTS `rbac_group_member` (
    `id`          CHAR(36)     NOT NULL COMMENT '内部主键 (Guid)',
    `userid`      VARCHAR(128) NOT NULL COMMENT '用户业务 ID',
    `group_code`  VARCHAR(128) NOT NULL COMMENT '权限组编码',
    `project`     VARCHAR(64)  NOT NULL COMMENT '所属 project',
    `granted_by`  VARCHAR(128) NOT NULL COMMENT '授权操作人 userid',
    `created_at`  DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `updated_at`  DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
                               ON UPDATE CURRENT_TIMESTAMP(6),

    PRIMARY KEY (`id`),
    -- 一个用户在同一 project 下同一个组只能出现一次
    UNIQUE KEY `ux_group_member_userid_group_project` (`userid`, `group_code`, `project`),
    -- 按 project + groupCode 查全组成员（高频查询）
    KEY `ix_group_member_project_group` (`project`, `group_code`),
    -- 按 userid 查用户所属全部组（snapshot 重建）
    KEY `ix_group_member_userid` (`userid`)

) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci
  COMMENT = '用户-权限组关联（Casbin g policy 真相来源）';


-- =============================================================
-- 4. rbac_rule — 菜单/按钮规则
--    Domain:  RbacRule
--    Mapping: RuleMapping
-- =============================================================
CREATE TABLE IF NOT EXISTS `rbac_rule` (
    `id`               CHAR(36)     NOT NULL                  COMMENT '内部主键 (Guid)',
    `dxe_id`           VARCHAR(64)  NOT NULL                  COMMENT '前端兼容业务 ID',
    `project`          VARCHAR(64)  NOT NULL                  COMMENT '所属 project',
    `rule_code`        VARCHAR(128) NOT NULL                  COMMENT '规则码（project 内唯一）',
    `permission_code`  VARCHAR(256) NOT NULL                  COMMENT '权限码（服务端鉴权依据）',
    `parent_rule_code` VARCHAR(128) NULL                      COMMENT '父级规则码（根节点为 NULL）',
    `type`             VARCHAR(16)  NOT NULL                  COMMENT '节点类型：MenuDir / Menu / Button',
    `title`            VARCHAR(128) NOT NULL                  COMMENT '菜单显示标题',
    `name`             VARCHAR(128) NULL                      COMMENT '前端路由 name（v-auth / auth() 匹配）',
    `path`             VARCHAR(256) NULL                      COMMENT '前端路由 path',
    `icon`             VARCHAR(128) NULL DEFAULT ''           COMMENT '菜单图标',
    `menu_type`        VARCHAR(16)  NULL                      COMMENT '菜单渲染类型：Tab / Link / Iframe（Button 节点为 NULL）',
    `url`              VARCHAR(512) NULL                      COMMENT '外链或 iframe URL',
    `component`        VARCHAR(256) NULL                      COMMENT '前端组件路径',
    `extend`           VARCHAR(64)  NULL                      COMMENT '扩展行为标记',
    `remark`           VARCHAR(512) NULL DEFAULT ''           COMMENT '备注',
    `keepalive`        TINYINT(1)   NOT NULL DEFAULT 0        COMMENT '是否开启路由缓存（keep-alive）',
    `weigh`            INT          NOT NULL DEFAULT 0        COMMENT '排序权重（数值越小越靠前）',
    `status`           VARCHAR(16)  NOT NULL DEFAULT 'Active' COMMENT '状态：Active / Disabled',
    `created_at`       DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `updated_at`       DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
                                    ON UPDATE CURRENT_TIMESTAMP(6),

    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_rule_dxe_id` (`dxe_id`),
    UNIQUE KEY `ux_rule_code_project` (`rule_code`, `project`),
    -- 菜单树构建按 project + status 查询
    KEY `ix_rule_project_status` (`project`, `status`),
    -- 父子关系查询
    KEY `ix_rule_parent_code` (`parent_rule_code`)

) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci
  COMMENT = '菜单/按钮规则（权限体系基础单元）';


-- =============================================================
-- 5. rbac_project_grant — 用户-Project 授权
--    Domain:  RbacProjectGrant
--    Mapping: ProjectGrantMapping
-- =============================================================
CREATE TABLE IF NOT EXISTS `rbac_project_grant` (
    `id`         CHAR(36)     NOT NULL          COMMENT '内部主键 (Guid)',
    `userid`     VARCHAR(128) NOT NULL          COMMENT '被授权的用户 ID',
    `project`    VARCHAR(64)  NOT NULL          COMMENT '被授权访问的 project',
    `is_super`   TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '是否为该 project 的超级管理员（跳过 permset 判断）',
    `granted_by` VARCHAR(128) NOT NULL          COMMENT '授权操作人 userid',
    `granted_at` DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) COMMENT '授权创建时间（UTC）',
    `updated_at` DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
                              ON UPDATE CURRENT_TIMESTAMP(6),

    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_grant_userid_project` (`userid`, `project`),
    -- 按 userid 查用户所有 project（ProjectGrantController 使用）
    KEY `ix_grant_userid` (`userid`),
    -- 按 project 查所有授权用户
    KEY `ix_grant_project` (`project`)

) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci
  COMMENT = '用户-Project 授权（project 级别 super 控制）';


-- =============================================================
-- 6. rbac_api_permission_map — API 路由权限映射
--    Domain:  RbacApiPermissionMap
--    Mapping: ApiPermissionMapMapping
--    允许的 action 值：read / create / update / delete / execute / access
--    允许的 http_method 值：GET / POST / PUT / DELETE / PATCH
-- =============================================================
CREATE TABLE IF NOT EXISTS `rbac_api_permission_map` (
    `id`               CHAR(36)     NOT NULL                  COMMENT '内部主键 (Guid)',
    `project`          VARCHAR(64)  NOT NULL                  COMMENT '所属 project',
    `http_method`      VARCHAR(8)   NOT NULL                  COMMENT 'HTTP 方法（大写）：GET / POST / PUT / DELETE / PATCH',
    `route_pattern`    VARCHAR(512) NOT NULL                  COMMENT '路由模板（ASP.NET Core template 语法，支持 {param}）',
    `permission_code`  VARCHAR(256) NOT NULL                  COMMENT '对应权限码（服务端鉴权依据）',
    `action`           VARCHAR(16)  NOT NULL                  COMMENT '动作：read / create / update / delete / execute / access',
    `status`           VARCHAR(16)  NOT NULL DEFAULT 'Active' COMMENT '状态：Active / Disabled',
    `created_at`       DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `updated_at`       DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
                                    ON UPDATE CURRENT_TIMESTAMP(6),

    PRIMARY KEY (`id`),
    -- 同一 project + method + route 唯一
    UNIQUE KEY `ux_api_map_project_method_route` (`project`, `http_method`, `route_pattern`(191)),
    -- 运行时按 project + status 批量加载（缓存预热）
    KEY `ix_api_map_project_status` (`project`, `status`)

) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci
  COMMENT = 'API 路由 → permissionCode 映射（运行时鉴权路由匹配来源）';


-- =============================================================
-- 7. rbac_outbox — Outbox 事件表
--    Entity:  OutboxEventEntity
--    Mapping: OutboxEventMapping
--    Worker:  RbacOutboxPollingWorker（轮询消费）
-- =============================================================
CREATE TABLE IF NOT EXISTS `rbac_outbox` (
    `event_id`     VARCHAR(64)  NOT NULL           COMMENT '事件唯一 ID（UUID v4）',
    `event_type`   VARCHAR(64)  NOT NULL           COMMENT '事件类型：UserChanged / GroupChanged / MenuChanged / PolicyChanged / ProjectGrantChanged / ApiMapChanged',
    `project`      VARCHAR(64)  NOT NULL           COMMENT '事件所属 project',
    `userid`       VARCHAR(128) NULL               COMMENT '相关用户 ID（可选）',
    `group_code`   VARCHAR(128) NULL               COMMENT '相关权限组编码（可选）',
    `payload`      LONGTEXT     NOT NULL           COMMENT '事件 payload（JSON，结构见 RbacOutboxEvents.cs）',
    `status`       VARCHAR(16)  NOT NULL DEFAULT 'Pending' COMMENT '状态：Pending / Processing / Succeeded / Failed',
    `retry_count`  INT          NOT NULL DEFAULT 0 COMMENT '已重试次数（上限 5 次，超过后 status=Failed 进入 DLQ）',
    `next_retry_at` DATETIME(6) NULL               COMMENT '下次重试时间（指数退避：5/10/20/40/80s；NULL=无需重试）',
    `created_at`   DATETIME(6)  NOT NULL DEFAULT CURRENT_TIMESTAMP(6) COMMENT '事件产生时间',
    `processed_at` DATETIME(6)  NULL               COMMENT '处理完成时间（Succeeded / Failed 时写入）',

    PRIMARY KEY (`event_id`),
    -- Worker 轮询查询：status=Pending AND next_retry_at <= now
    KEY `ix_outbox_status_retry` (`status`, `next_retry_at`),
    -- 按状态过滤（监控 DLQ 使用）
    KEY `ix_outbox_status` (`status`),
    -- 按时间排序（Worker 按 created_at 升序消费）
    KEY `ix_outbox_created_at` (`created_at`)

) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci
  COMMENT = 'Outbox 事件表（权限变更异步传播，与业务同事务提交）';


SET FOREIGN_KEY_CHECKS = 1;

-- =============================================================
-- 初始化种子数据
-- 说明：仅插入系统能运行所需的最小数据集。
--       正式业务数据通过管理 API 写入。
-- =============================================================

-- 默认 project 的超级管理员（需替换 userid / granted_by 为真实值）
-- 首次部署后通过 PUT /api/project-grant/{userid}/super 管理后续变更
INSERT IGNORE INTO `rbac_project_grant`
    (`id`, `userid`, `project`, `is_super`, `granted_by`, `granted_at`, `updated_at`)
VALUES
    (UUID(), 'system_admin', 'default', 1, 'system', NOW(6), NOW(6));

-- =============================================================
-- 表结构验证（可选，用于 CI 检查）
-- SHOW TABLES LIKE 'rbac_%';
-- SHOW CREATE TABLE rbac_administrator;
-- SHOW CREATE TABLE rbac_group;
-- SHOW CREATE TABLE rbac_group_member;
-- SHOW CREATE TABLE rbac_rule;
-- SHOW CREATE TABLE rbac_project_grant;
-- SHOW CREATE TABLE rbac_api_permission_map;
-- SHOW CREATE TABLE rbac_outbox;
-- =============================================================
