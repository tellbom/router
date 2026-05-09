namespace Rbac.Application.Snapshots;

/// <summary>
/// permset 重建冲突策略。
///
/// 定义当 permset 重建期间检测到 version 变化时的处理规则。
/// 核心规则：version 新者生效，旧版本重建结果必须丢弃。
/// </summary>
public static class RbacPermsetConflictPolicy
{
    /// <summary>
    /// 判断是否应丢弃本次重建结果。
    /// </summary>
    /// <param name="versionAtBuildStart">重建开始时读取的 version。</param>
    /// <param name="currentVersion">写入前再次读取的 version。</param>
    /// <returns>true 表示 version 已变化，必须丢弃；false 表示可以写入。</returns>
    public static bool ShouldDiscard(long versionAtBuildStart, long currentVersion)
        => currentVersion != versionAtBuildStart;

    /// <summary>
    /// 丢弃原因描述（用于日志和审计）。
    /// </summary>
    public static string DiscardReason(long buildVersion, long currentVersion) =>
        $"Permset stale: buildVersion={buildVersion} currentVersion={currentVersion}. " +
        $"Newer version wins. Discarding rebuild result.";

    /// <summary>
    /// 安全写入判断：仅当 version 未变化时允许写入。
    /// 提供统一的决策入口，避免各实现自行判断。
    /// </summary>
    public static PermsetWriteDecision Decide(long versionAtBuildStart, long currentVersion)
    {
        if (ShouldDiscard(versionAtBuildStart, currentVersion))
            return new PermsetWriteDecision(false, DiscardReason(versionAtBuildStart, currentVersion));
        return new PermsetWriteDecision(true, null);
    }
}

/// <summary>permset 写入决策结果。</summary>
public sealed record PermsetWriteDecision(bool ShouldWrite, string? DiscardReason);
