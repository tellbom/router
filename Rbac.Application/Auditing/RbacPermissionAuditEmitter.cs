using Rbac.Application.Auditing;
using Rbac.Application.Authorization;
using Rbac.Application.Security;

namespace Rbac.Application.Auditing;

/// <summary>
/// 权限判断审计事件发射器。
///
/// 由 <see cref="Rbac.Application.Authorization.RbacPermissionChecker"/> 调用，
/// 记录每次鉴权判断的来源和结果（Redis / Casbin / Fallback）。
///
/// 约束：
/// - 调用方使用 fire-and-forget（_ = EmitAsync(...)），不 await。
/// - 不阻塞鉴权热路径。
/// - 审计写入失败不改变鉴权结果。
/// </summary>
public sealed class RbacPermissionAuditEmitter
{
    private readonly IAuditEventEmitter _emitter;

    public RbacPermissionAuditEmitter(IAuditEventEmitter emitter)
    {
        _emitter = emitter;
    }

    /// <summary>
    /// 发射权限判断结果审计事件。
    /// </summary>
    public void Emit(
        CurrentRbacContext ctx,
        string permissionCode,
        string action,
        string result,
        PermissionCheckSource source,
        string? reason = null)
    {
        var evt = new AuthorizationAuditEvent
        {
            Userid = ctx.Userid,
            Project = ctx.Project,
            RequestedProject = ctx.RequestedProject,
            TraceId = ctx.TraceId,
            PermissionCode = permissionCode,
            Action = action,
            Result = result,
            Reason = reason ?? source.ToString(),
        };

        // fire-and-forget，不阻塞鉴权路径
        _ = _emitter.EmitAsync(evt);
    }
}
