namespace Rbac.Infrastructure.Redis;

/// <summary>
/// Redis key 命名规范。
/// 所有 key 必须通过此类的静态方法生成，禁止在业务代码中硬编码 key 字符串。
///
/// 命名约定：rbac:{keyType}:{project}[:{userid}]
/// 原则：
/// - 按 project + userid 拆分，禁止单 key 存储所有用户数据。
/// - 版本 key 不过期（或设超长 TTL），只递增，不删除。
/// - permset key 高频 SISMEMBER 判断直接走 StackExchange.Redis，不经过 FusionCache。
/// </summary>
public static class RbacRedisKeys
{
    // ── 用户权限快照 ────────────────────────────────────────────
    /// <summary>
    /// 用户完整权限快照（groups、super、permissionCodes、versions）。
    /// TTL: 30-60 min。FusionCache 适合。版本校验必须。
    /// </summary>
    public static string Snapshot(string project, string userid) =>
        $"rbac:snapshot:{project}:{userid}";

    // ── 用户菜单快照 ─────────────────────────────────────────────
    /// <summary>
    /// 裁剪后的前端 menus 树（含 DxE_id string）。
    /// TTL: 30-60 min。FusionCache 适合。版本校验必须。
    /// </summary>
    public static string Menus(string project, string userid) =>
        $"rbac:menus:{project}:{userid}";

    // ── 权限码 Set ───────────────────────────────────────────────
    /// <summary>
    /// 用户 permissionCode:action 集合，仅由 MySQL/Casbin 策略构建。
    /// TTL: 30-60 min。高频 SISMEMBER 必须直接走 StackExchange.Redis，禁止 FusionCache 包装。
    /// </summary>
    public static string Permset(string project, string userid) =>
        $"rbac:permset:{project}:{userid}";

    // ── 用户-项目授权 ────────────────────────────────────────────
    /// <summary>
    /// 用户可访问的 project 列表（Set）。
    /// TTL: 10-30 min。FusionCache 适合。
    /// </summary>
    public static string UserProjects(string userid) =>
        $"rbac:user-projects:{userid}";

    /// <summary>
    /// project 下授权用户列表（Set），仅管理/统计使用，不走热路径。
    /// TTL: 10-30 min。
    /// </summary>
    public static string ProjectUsers(string project) =>
        $"rbac:project-users:{project}";

    // ── 版本号（不过期，只递增）──────────────────────────────────
    /// <summary>
    /// project 全局权限版本。菜单/规则/组/policy 变更时递增。
    /// 不过期或超长 TTL，不删除，只递增。
    /// </summary>
    public static string VersionProject(string project) =>
        $"rbac:version:{project}";

    /// <summary>
    /// 用户级权限版本。用户组、状态、授权变更时递增。
    /// </summary>
    public static string VersionUser(string project, string userid) =>
        $"rbac:version:{project}:{userid}";

    /// <summary>
    /// 权限组版本。组规则变更、组状态变更时递增。
    /// </summary>
    public static string VersionGroup(string project, string groupCode) =>
        $"rbac:version:{project}:group:{groupCode}";

    // ── API 映射 ─────────────────────────────────────────────────
    /// <summary>
    /// project 下 API route/method → permissionCode/action 映射。
    /// TTL: 60 min。FusionCache 适合。
    /// </summary>
    public static string ApiMap(string project) =>
        $"rbac:api-map:{project}";

    // ── 菜单树 ───────────────────────────────────────────────────
    /// <summary>
    /// project 全量启用菜单规则树（不含用户裁剪）。
    /// TTL: 60 min。FusionCache 适合。版本校验必须。
    /// </summary>
    public static string MenuTree(string project) =>
        $"rbac:menu-tree:{project}";

    // ── Casbin policy 版本 ───────────────────────────────────────
    /// <summary>
    /// Casbin policy 版本。policy 变更、权限组关系变更时递增。
    /// 不过期或超长 TTL，不删除，只递增。FusionCache 可短缓存。
    /// </summary>
    public static string PolicyVersion(string project) =>
        $"rbac:policy-version:{project}";

    // ── 缓存失效 Pub/Sub channel ─────────────────────────────────
    /// <summary>
    /// Redis Pub/Sub 频道，用于通知各 API 实例驱逐本地 L1 缓存。
    /// 发布端：IRbacCacheInvalidator。订阅端：每个 API 实例启动时注册。
    /// </summary>
    public const string CacheInvalidateChannel = "rbac.cache.invalidate";
}
