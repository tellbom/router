namespace Rbac.Application.Auditing;

/// <summary>
/// Casbin Enforcer reload 审计事件。
/// reload 成功、失败、耗时、版本变更均必须记录。
/// 已在 RbacAuditContracts.cs 中定义，此文件作为专用扩展补充。
/// </summary>
public static class CasbinReloadAuditEventExtensions
{
    /// <summary>记录 reload 成功。</summary>
    public static CasbinReloadAuditEvent Success(
        string project, long oldVersion, long newVersion, long elapsedMs) =>
        new()
        {
            Project = project,
            OldPolicyVersion = oldVersion,
            NewPolicyVersion = newVersion,
            Result = "Succeeded",
            ElapsedMs = elapsedMs,
        };

    /// <summary>记录 reload 失败。</summary>
    public static CasbinReloadAuditEvent Failure(
        string project, long oldVersion, string reason, long elapsedMs) =>
        new()
        {
            Project = project,
            OldPolicyVersion = oldVersion,
            NewPolicyVersion = oldVersion,
            Result = "Failed",
            FailureReason = reason,
            ElapsedMs = elapsedMs,
        };
}
