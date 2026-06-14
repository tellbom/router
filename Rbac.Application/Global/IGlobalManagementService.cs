namespace Rbac.Application.Global;

// ── 结果报告 ──────────────────────────────────────────────────────────

/// <summary>
/// 跨 project 操作的逐 project 结果报告。
/// 非原子语义：各 project 独立事务，部分失败不回滚已成功的写入（§A.8 #4）。
/// 调用方可对 FailureCount > 0 的 project 单独重试（幂等）。
/// </summary>
public sealed class PerProjectResultReport
{
    public IReadOnlyList<ProjectOperationResult> Results { get; init; } = Array.Empty<ProjectOperationResult>();

    public int SuccessCount => Results.Count(r => r.Success);
    public int FailureCount => Results.Count(r => !r.Success);
}

/// <summary>单个 project 的操作结果。</summary>
public sealed class ProjectOperationResult
{
    public string Project { get; init; } = string.Empty;
    public bool Success { get; init; }

    /// <summary>操作被跳过（幂等：目标状态已满足）。</summary>
    public bool Skipped { get; init; }

    /// <summary>失败时的错误消息；成功/跳过时为 null。</summary>
    public string? ErrorMessage { get; init; }
}

// ── 服务契约 ──────────────────────────────────────────────────────────

/// <summary>
/// 统一权限中心全局管理服务契约。
///
/// 设计约束（unified-permission-center-plan-2.md §B.2、§B.3）：
/// - 全部写操作仅委托给现有 RbacManagementWriteGuard + IRbacManagementWriteService。
/// - 不引入新的 Outbox 事件类型，不引入新的写路径。
/// - G005 compat-blocker：所有目标 project 列表必须通过 RbacGlobalConstants.IsReservedProject()
///   排除 __global__，防止全局操作递归写入保留系统。
/// - 非原子语义：每个 project 在独立事务中执行；部分失败返回 PerProjectResultReport。
/// </summary>
public interface IGlobalManagementService
{
    /// <summary>
    /// 将用户授权到指定 project 列表（fan-out）。
    /// 若用户不存在于 rbac_administrator：当 username 非空时自动创建，否则对该 project 记录失败。
    /// 已有授权的 project 跳过（幂等）。
    /// 产生事件：UserChanged（仅新建账号时）+ N×ProjectGrantChanged。
    /// </summary>
    Task<PerProjectResultReport> GrantUserToProjectsAsync(
        string userid,
        string? username,
        IReadOnlyList<string> targetProjects,
        bool isSuper,
        string operatorUserid,
        CancellationToken ct = default);

    /// <summary>
    /// 撤销用户在指定 project 列表的授权（fan-out）。
    /// 未授权的 project 跳过（幂等）。
    /// 产生事件：N×ProjectGrantChanged(Revoked)。
    /// </summary>
    Task<PerProjectResultReport> RevokeUserFromProjectsAsync(
        string userid,
        IReadOnlyList<string> targetProjects,
        string operatorUserid,
        CancellationToken ct = default);

    /// <summary>
    /// 将用户加入指定 project 内的指定权限组。
    /// 用户必须已存在于 rbac_administrator；已是成员则跳过（幂等）。
    /// 产生事件：PolicyChanged + GroupChanged。
    /// </summary>
    Task<PerProjectResultReport> AddUserToGroupAsync(
        string userid,
        string groupCode,
        string targetProject,
        string operatorUserid,
        CancellationToken ct = default);

    /// <summary>
    /// 将用户从指定 project 内的指定权限组移除。
    /// 不是成员则跳过（幂等）。
    /// 产生事件：PolicyChanged + GroupChanged。
    /// </summary>
    Task<PerProjectResultReport> RemoveUserFromGroupAsync(
        string userid,
        string groupCode,
        string targetProject,
        string operatorUserid,
        CancellationToken ct = default);
}
