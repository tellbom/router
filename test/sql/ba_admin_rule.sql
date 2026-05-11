-- --------------------------------------------------------
-- 主机:                           127.0.0.1
-- 服务器版本:                        5.7.40 - MySQL Community Server (GPL)
-- 服务器操作系统:                      Win64
-- HeidiSQL 版本:                  12.3.0.6589
-- --------------------------------------------------------

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET NAMES utf8 */;
/*!50503 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;

-- 导出  表 test.ba_admin_rule 结构
CREATE TABLE IF NOT EXISTS `ba_admin_rule` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT COMMENT 'ID',
  `pid` int(11) unsigned NOT NULL DEFAULT '0' COMMENT '上级菜单',
  `type` enum('menu_dir','menu','button') COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'menu' COMMENT '类型:menu_dir=菜单目录,menu=菜单项,button=页面按钮',
  `title` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '' COMMENT '标题',
  `name` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '' COMMENT '规则名称',
  `path` varchar(100) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '' COMMENT '路由路径',
  `icon` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '' COMMENT '图标',
  `menu_type` enum('tab','link','iframe') COLLATE utf8mb4_unicode_ci DEFAULT NULL COMMENT '菜单类型:tab=选项卡,link=链接,iframe=Iframe',
  `url` varchar(255) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '' COMMENT 'Url',
  `component` varchar(100) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '' COMMENT '组件路径',
  `keepalive` tinyint(4) unsigned NOT NULL DEFAULT '0' COMMENT '缓存:0=关闭,1=开启',
  `extend` enum('none','add_rules_only','add_menu_only') COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'none' COMMENT '扩展属性:none=无,add_rules_only=只添加为路由,add_menu_only=只添加为菜单',
  `remark` varchar(255) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '' COMMENT '备注',
  `weigh` int(11) NOT NULL DEFAULT '0' COMMENT '权重',
  `status` enum('0','1') COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '1' COMMENT '状态:0=禁用,1=启用',
  `update_time` bigint(16) unsigned DEFAULT NULL COMMENT '更新时间',
  `create_time` bigint(16) unsigned DEFAULT NULL COMMENT '创建时间',
  PRIMARY KEY (`id`),
  KEY `pid` (`pid`)
) ENGINE=InnoDB AUTO_INCREMENT=146 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci ROW_FORMAT=DYNAMIC COMMENT='菜单和权限规则表';

-- 正在导出表  test.ba_admin_rule 的数据：~38 rows (大约)
DELETE FROM `ba_admin_rule`;
INSERT INTO `ba_admin_rule` (`id`, `pid`, `type`, `title`, `name`, `path`, `icon`, `menu_type`, `url`, `component`, `keepalive`, `extend`, `remark`, `weigh`, `status`, `update_time`, `create_time`) VALUES
	(1, 0, 'menu', '首页', 'dashboard', 'dashboard', 'fa fa-dashboard', 'tab', '', '/src/views/backend/dashboard.vue', 0, 'none', 'Remark lang', 999, '1', 1772684931, 1760249844),
	(2, 0, 'menu_dir', '权限管理', 'auth', 'auth', 'fa fa-group', NULL, '', '', 0, 'none', '', 10, '1', 1772695035, 1760249844),
	(3, 2, 'menu', '角色组管理', 'auth/group', 'auth/group', 'fa fa-group', 'tab', '', '/src/views/backend/auth/group/index.vue', 0, 'none', 'Remark lang', 99, '1', 1760249844, 1760249844),
	(4, 3, 'button', '查看', 'auth/group/index', '', '', NULL, '', '', 0, 'none', '', 0, '1', 1760249844, 1760249844),
	(5, 3, 'button', '添加', 'auth/group/add', '', '', NULL, '', '', 0, 'none', '', 0, '1', 1760249844, 1760249844),
	(6, 3, 'button', '编辑', 'auth/group/edit', '', '', NULL, '', '', 0, 'none', '', 0, '1', 1760249844, 1760249844),
	(7, 3, 'button', '删除', 'auth/group/del', '', '', NULL, '', '', 0, 'none', '', 0, '1', 1760249844, 1760249844),
	(8, 2, 'menu', '管理员管理', 'auth/admin', 'auth/admin', 'el-icon-UserFilled', 'tab', '', '/src/views/backend/auth/admin/index.vue', 0, 'none', '', 98, '1', 1760249844, 1760249844),
	(9, 8, 'button', '查看', 'auth/admin/index', '', '', NULL, '', '', 0, 'none', '', 0, '1', 1760249844, 1760249844),
	(10, 8, 'button', '添加', 'auth/admin/add', '', '', NULL, '', '', 0, 'none', '', 0, '1', 1760249844, 1760249844),
	(11, 8, 'button', '编辑', 'auth/admin/edit', '', '', NULL, '', '', 0, 'none', '', 0, '1', 1760249844, 1760249844),
	(12, 8, 'button', '删除', 'auth/admin/del', '', '', NULL, '', '', 0, 'none', '', 0, '1', 1760249844, 1760249844),
	(13, 2, 'menu', '菜单规则管理', 'auth/rule', 'auth/rule', 'el-icon-Grid', 'tab', '', '/src/views/backend/auth/rule/index.vue', 0, 'none', '', 97, '1', 1760249844, 1760249844),
	(14, 13, 'button', '查看', 'auth/rule/index', '', '', NULL, '', '', 0, 'none', '', 0, '1', 1760249844, 1760249844),
	(15, 13, 'button', '添加', 'auth/rule/add', '', '', NULL, '', '', 0, 'none', '', 0, '1', 1760249844, 1760249844),
	(16, 13, 'button', '编辑', 'auth/rule/edit', '', '', NULL, '', '', 0, 'none', '', 0, '1', 1760249844, 1760249844),
	(17, 13, 'button', '删除', 'auth/rule/del', '', '', NULL, '', '', 0, 'none', '', 0, '1', 1760249844, 1760249844),
	(18, 13, 'button', '快速排序', 'auth/rule/sortable', '', '', NULL, '', '', 0, 'none', '', 0, '1', 1760249844, 1760249844),
	(19, 2, 'menu', '管理员日志管理', 'auth/adminLog', 'auth/adminLog', 'el-icon-List', 'tab', '', '/src/views/backend/auth/adminLog/index.vue', 0, 'none', '', 96, '1', 1760249844, 1760249844),
	(20, 19, 'button', '查看', 'auth/adminLog/index', '', '', NULL, '', '', 0, 'none', '', 0, '1', 1760249844, 1760249844),
	(76, 0, 'menu', 'BuildAdmin', 'buildadmin', 'buildadmin', 'local-logo', 'link', 'https://doc.buildadmin.com', '', 0, 'none', '', 0, '0', 1760585855, 1760249844),
	(89, 1, 'button', '查看', 'dashboard/index', '', '', NULL, '', '', 0, 'none', '', 0, '1', 1760249845, 1760249845),
	(130, 0, 'menu_dir', '待办中心', 'process', 'process', 'el-icon-UserFilled', 'tab', '', '', 0, 'none', '', 90, '1', 1772695026, 1772695026),
	(131, 130, 'menu', '我的待办', 'process/mytodo', 'process/mytodo', 'el-icon-DArrowRight', 'tab', '', '/src/views/backend/todo/MyTodo.vue', 0, 'none', '', 94, '1', 1772695167, 1772695167),
	(132, 130, 'menu', '我的申请', 'process/myapplication', 'process/myapplication', 'el-icon-HotWater', 'tab', '', '/src/views/backend/todo/MyApplication.vue', 0, 'none', '', 91, '1', 1772695259, 1772695259),
	(133, 0, 'menu_dir', '巡察计划管理', 'inspection', '', 'el-icon-Burger', 'tab', '', '', 0, 'none', '', 80, '1', 1772785494, 1772783832),
	(134, 133, 'menu', '巡察计划', 'inspection/plan', 'inspection/plan', 'el-icon-Avatar', 'tab', '', '/src/views/backend/inspection/plan/Inspectionplan.vue', 0, 'none', '', 0, '1', 1772785519, 1772783923),
	(135, 133, 'menu', '人员选调', 'inspection/secondment', 'inspection/secondment', 'el-icon-ChromeFilled', 'tab', '', '/src/views/backend/inspection/secondment/SecondmentView.vue', 0, 'none', '', 0, '1', 1774357342, 1773107551),
	(136, 133, 'menu', '巡前通报', 'inspection/briefing', 'inspection/briefing', 'fa fa-circle-o', 'tab', '', '/src/views/backend/inspection/briefing/PreInspectionBriefingPage.vue', 0, 'none', '', 0, '1', 1773300508, 1773280885),
	(137, 133, 'menu', '巡察准备', 'inspectionprep/lnspectionpreppage', 'inspectionprep/lnspectionpreppage', 'fa fa-circle-o', 'tab', '', '/src/views/backend/inspection/inspectionprep/Inspectionpreppage.vue', 0, 'none', '', 0, '1', 1773730929, 1773730598),
	(138, 0, 'menu', '日常通报', 'dailybulletin/dailybulletinpage', 'dailybulletin/dailybulletinpage', 'fa fa-circle-o', 'tab', '', '/src/views/backend/dailybulletin/DailyBulletinPage.vue', 0, 'none', '', 20, '1', 1773887767, 1773738835),
	(139, 0, 'menu', '上级巡视', 'supervisionissue/page', 'supervisionissue/page', 'fa fa-circle-o', 'tab', '', '/src/views/backend/supervisionissue/Supervisionissuepage.vue', 0, 'none', '', 30, '1', 1773911203, 1773911141),
	(140, 0, 'menu', '问题整改库', 'issue/page', 'issue/page', 'fa fa-circle-o', 'tab', '', '/src/views/backend/issue/issuepage.vue', 0, 'none', '', 80, '1', 1774961077, 1774960972),
	(141, 0, 'menu', '巡察过程管控', 'inspectionprocess/page', 'inspectionprocess/page', 'fa fa-circle-o', 'tab', '', '/src/views/backend/inspectionprocess/Inspectionprocesspage.vue', 0, 'none', '', 0, '1', 1775133914, 1775133914),
	(142, 0, 'menu_dir', '人才队伍', 'talent', 'talent', 'fa fa-circle-o', 'tab', '', '', 0, 'none', '', 0, '1', 1776387912, 1776387912),
	(143, 142, 'menu', '巡察队伍', 'talentpool/pool', 'talentpool/pool', 'fa fa-circle-o', 'tab', '', '/src/views/backend/talentpool/pool/TalentPoolPage.vue', 0, 'none', '', 0, '1', 1776388626, 1776388626),
	(144, 140, 'button', '问题入库', 'issue/page/add', '', 'fa fa-circle-o', 'tab', '', '', 0, 'none', '', 0, '1', 1776480671, 1776480671),
	(145, 140, 'button', '问题查看', 'issue/page/view', '', 'fa fa-circle-o', 'tab', '', '', 0, 'none', '', 0, '1', 1776480831, 1776480831);

/*!40103 SET TIME_ZONE=IFNULL(@OLD_TIME_ZONE, 'system') */;
/*!40101 SET SQL_MODE=IFNULL(@OLD_SQL_MODE, '') */;
/*!40014 SET FOREIGN_KEY_CHECKS=IFNULL(@OLD_FOREIGN_KEY_CHECKS, 1) */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40111 SET SQL_NOTES=IFNULL(@OLD_SQL_NOTES, 1) */;
