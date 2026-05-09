namespace Rbac.Application.Migration;

/// <summary>
/// 灰度迁移配置选项。绑定 appsettings.json "GrayMigration" 节。
///
/// 支持按 project 粒度控制迁移阶段，实现以下灰度步骤（设计文档 §9.7）：
/// 1. 双写审计日志，不影响旧系统。
/// 2. 新 RBAC 只读对比 PHP 结果。
/// 3. 对比 menus 和按钮权限差异。
/// 4. 小 project 灰度切换。
/// 5. 扩大 project 范围。
/// 6. 关闭旧权限接口。
/// </summary>
public sealed class RbacGrayMigrationOptions
{
    public const string SectionName = "GrayMigration";

    /// <summary>是否全局启用灰度模式（false 时所有请求走新 RBAC）。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 按 project 配置的迁移阶段。
    /// key: project 标识，value: 迁移阶段配置。
    /// </summary>
    public Dictionary<string, ProjectMigrationConfig> Projects { get; set; } = new();

    /// <summary>获取指定 project 的迁移配置，不存在时返回默认（完全切换到新 RBAC）。</summary>
    public ProjectMigrationConfig GetProjectConfig(string project)
    {
        if (!Enabled) return ProjectMigrationConfig.FullyMigrated;
        return Projects.TryGetValue(project, out var cfg) ? cfg : ProjectMigrationConfig.FullyMigrated;
    }
}

/// <summary>单个 project 的迁移阶段配置。</summary>
public sealed class ProjectMigrationConfig
{
    /// <summary>迁移阶段。</summary>
    public MigrationStage Stage { get; set; } = MigrationStage.FullyMigrated;

    /// <summary>只读对比模式：新 RBAC 执行鉴权但同时对比 PHP 结果，以最终结果为准由 ComparisonWins 控制。</summary>
    public bool ReadOnlyComparisonMode { get; set; } = false;

    /// <summary>对比模式下，是否以新 RBAC 结果为最终授权结果（false 则以 PHP 为准）。</summary>
    public bool NewRbacWins { get; set; } = false;

    /// <summary>完全迁移（仅用新 RBAC）的默认配置。</summary>
    public static ProjectMigrationConfig FullyMigrated => new()
    {
        Stage = MigrationStage.FullyMigrated,
        ReadOnlyComparisonMode = false,
        NewRbacWins = true,
    };
}

/// <summary>迁移阶段枚举。</summary>
public enum MigrationStage
{
    /// <summary>阶段 1：双写审计，请求仍走旧 PHP 系统。</summary>
    DualAudit,

    /// <summary>阶段 2：新 RBAC 只读对比，结果以 PHP 为准。</summary>
    ReadOnlyComparison,

    /// <summary>阶段 3：灰度流量切换，小比例走新 RBAC。</summary>
    GrayTraffic,

    /// <summary>阶段 4：完全切换到新 RBAC，关闭 PHP 权限接口。</summary>
    FullyMigrated,
}
