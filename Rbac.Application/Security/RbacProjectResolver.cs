using Microsoft.Extensions.Logging;
using Rbac.Application.Auditing;

namespace Rbac.Application.Security;

/// <summary>
/// <see cref="IRbacProjectResolver"/> 的实现。
/// project 校验的唯一入口，不允许业务 Service 绕过。
///
/// 校验流程：
/// 1. requestedProject 为空 → MissingProject
/// 2. 检查用户状态（FusionCache → Redis → MySQL）
/// 3. 检查 userid-project 授权（FusionCache → Redis → MySQL）
/// 4. 读取 isProjectSuper、policyVersion 填入 Context
/// 5. 写审计日志（异步，不阻塞）
/// </summary>
public sealed class RbacProjectResolver : IRbacProjectResolver
{
    private readonly IProjectGrantStore _grantStore;
    private readonly IAuditEventEmitter _auditEmitter;
    private readonly ILogger<RbacProjectResolver> _logger;

    public RbacProjectResolver(
        IProjectGrantStore grantStore,
        IAuditEventEmitter auditEmitter,
        ILogger<RbacProjectResolver> logger)
    {
        _grantStore = grantStore;
        _auditEmitter = auditEmitter;
        _logger = logger;
    }

    public async Task<CurrentRbacContext> ResolveAsync(
        string userid,
        string requestedProject,
        string traceId,
        CancellationToken cancellationToken = default)
    {
        // 1. project 为空
        if (string.IsNullOrWhiteSpace(requestedProject))
        {
            _logger.LogWarning("MissingProject userid={Userid} traceId={TraceId}", userid, traceId);
            await EmitAuditAsync(userid, requestedProject, traceId, ProjectResolveResult.MissingProject);
            return BuildContext(userid, requestedProject, traceId, authorized: false, super: false, policyVersion: 0);
        }

        // 2. 查询授权关系（含 super 标志和 policyVersion）
        var grant = await _grantStore.GetGrantAsync(userid, requestedProject, cancellationToken);

        if (grant is null)
        {
            _logger.LogWarning("Unauthorized userid={Userid} project={Project} traceId={TraceId}",
                userid, requestedProject, traceId);
            await EmitAuditAsync(userid, requestedProject, traceId, ProjectResolveResult.Unauthorized);
            return BuildContext(userid, requestedProject, traceId, authorized: false, super: false, policyVersion: 0);
        }

        _logger.LogDebug("Authorized userid={Userid} project={Project} super={Super} traceId={TraceId}",
            userid, requestedProject, grant.IsSuper, traceId);
        await EmitAuditAsync(userid, requestedProject, traceId, ProjectResolveResult.Authorized);

        return BuildContext(userid, requestedProject, traceId,
            authorized: true, super: grant.IsSuper, policyVersion: grant.PolicyVersion);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private static CurrentRbacContext BuildContext(
        string userid, string project, string traceId,
        bool authorized, bool super, long policyVersion) =>
        new()
        {
            Userid = userid,
            Project = project,
            RequestedProject = project,
            TraceId = traceId,
            IsProjectAuthorized = authorized,
            IsProjectSuper = super,
            PolicyVersion = policyVersion,
        };

    private Task EmitAuditAsync(string userid, string project, string traceId, ProjectResolveResult result)
    {
        var evt = new AuthorizationAuditEvent
        {
            Userid = userid,
            Project = project,
            TraceId = traceId,
            Result = result == ProjectResolveResult.Authorized ? "allow" : "deny",
            Reason = result.ToString(),
        };
        return _auditEmitter.EmitAsync(evt);
    }
}

/// <summary>
/// project 授权查询结果。由缓存层（FusionCache → Redis → MySQL）返回。
/// </summary>
public sealed class ProjectGrantInfo
{
    public bool IsSuper { get; init; }
    public long PolicyVersion { get; init; }
}

/// <summary>
/// project 授权数据访问契约。由 Infrastructure.Redis 实现（含 FusionCache 包装和 MySQL 兜底）。
/// </summary>
public interface IProjectGrantStore
{
    /// <summary>
    /// 查询 userid 是否被授权访问 project。
    /// 返回 null 表示未授权或用户禁用。
    /// </summary>
    Task<ProjectGrantInfo?> GetGrantAsync(string userid, string project, CancellationToken ct = default);
}

/// <summary>
/// 审计事件发射契约。实现必须异步非阻塞（写入内存队列或 Channel）。
/// </summary>
public interface IAuditEventEmitter
{
    Task EmitAsync(RbacAuditEvent auditEvent);
}
