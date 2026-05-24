namespace Rbac.Application.Snapshots;

/// <summary>
/// permset 构建契约。
///
/// 核心约束：
/// - 输入只能来自 DM 真相库中的用户-组关系、组-权限关系，以及 Casbin policy。
/// - 禁止接受来自前端、ES 查询结果或 Redis 已有 permset 的输入。
/// - 构建结果写入 Redis 前必须执行 version compare-before-write。
///   如果写入期间 version 已变化，本次结果必须丢弃。
/// </summary>
public interface IRbacPermsetBuilder
{
    /// <summary>
    /// 根据 DM/Casbin 派生的 policy 输入构建 permset，并写入 Redis。
    /// </summary>
    /// <param name="input">只接受从 DM 真相库派生的策略输入。</param>
    /// <param name="ct"></param>
    /// <returns>写入成功返回 true；版本冲突丢弃时返回 false。</returns>
    Task<bool> BuildAndWriteAsync(PermsetBuildInput input, CancellationToken ct = default);
}

/// <summary>
/// permset 构建输入。只能由 DM/Casbin 策略数据填充，禁止来自前端或 ES。
/// </summary>
public sealed class PermsetBuildInput
{
    public string Userid { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;

    /// <summary>
    /// permset 成员列表。格式：permissionCode:action，例如 api:system.user.create:execute。
    /// 此列表只能来自 DM 组-权限关系表 + Casbin policy 合并计算。
    /// </summary>
    public IReadOnlyList<string> Members { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 构建时读取的当前版本号（用于 compare-before-write）。
    /// 写入 Redis 前再次读取版本，若已变化则丢弃本次结果。
    /// </summary>
    public long VersionAtBuildTime { get; init; }

    /// <summary>数据来源标记，强制要求调用方明确说明输入来源。</summary>
    public PermsetInputSource Source { get; init; }
}

/// <summary>
/// permset 输入来源枚举，防止非法来源混入构建链路。
/// </summary>
public enum PermsetInputSource
{
    /// <summary>来自 DM 组-权限关系表 + Casbin policy 计算。（唯一合法来源）</summary>
    DMCasbinDerived,

    // 以下来源禁止使用，枚举值仅用于文档说明
    // FrontendRequest  -- 禁止
    // ElasticsearchResult -- 禁止
    // RedisPermset -- 禁止（不允许从 Redis 读取后重写）
}
