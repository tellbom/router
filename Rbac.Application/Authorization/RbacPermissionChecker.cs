using Microsoft.Extensions.Logging;
using Rbac.Application.Auditing;
using Rbac.Application.Cache;
using Rbac.Application.Snapshots;

namespace Rbac.Application.Authorization;

/// <summary>
/// IRbacPermissionChecker 實現。
/// 依賴接口：IPermsetOperations / ICasbinEnforcer / IRbacSnapshotService。
/// 不直接引用 Infrastructure.Redis / Infrastructure.Casbin。
/// </summary>
public sealed class RbacPermissionChecker : IRbacPermissionChecker
{
    private readonly IPermsetOperations _permsetOps;
    private readonly ICasbinEnforcer _casbinEnforcer;
    private readonly IRbacSnapshotService _snapshotService;
    private readonly IAuditEventEmitter _auditEmitter;
    private readonly ILogger<RbacPermissionChecker> _logger;

    public RbacPermissionChecker(
        IPermsetOperations permsetOps,
        ICasbinEnforcer casbinEnforcer,
        IRbacSnapshotService snapshotService,
        IAuditEventEmitter auditEmitter,
        ILogger<RbacPermissionChecker> logger)
    {
        _permsetOps = permsetOps;
        _casbinEnforcer = casbinEnforcer;
        _snapshotService = snapshotService;
        _auditEmitter = auditEmitter;
        _logger = logger;
    }

    public async Task<PermissionCheckResult> CheckAsync(
        PermissionCheckRequest request, CancellationToken ct = default)
    {
        var ctx = request.Context;
        var permCode = request.PermissionCode;
        var action = request.Action;

        // 1. project-scoped super
        if (ctx.IsProjectSuper)
        {
            _logger.LogDebug("SuperAllow userid={U} project={P}", ctx.Userid, ctx.Project);
            await EmitAsync(ctx, permCode, action, "allow", PermissionCheckSource.ProjectSuper);
            return PermissionCheckResult.Allow(PermissionCheckSource.ProjectSuper);
        }

        // 2. SnapshotService owns version validation and rebuilds stale snapshots.
        var snapshot = await _snapshotService.GetSnapshotAsync(ctx.Userid, ctx.Project, ct);
        var hasFreshSnapshot = snapshot is not null;

        // 3. Redis permset SISMEMBER
        if (hasFreshSnapshot)
        {
            try
            {
                var inPermset = await _permsetOps.IsMemberAsync(ctx.Project, ctx.Userid, permCode, action, ct);
                if (inPermset)
                {
                    await EmitAsync(ctx, permCode, action, "allow", PermissionCheckSource.RedisPermset);
                    return PermissionCheckResult.Allow(PermissionCheckSource.RedisPermset);
                }

                var permsetExists = await _permsetOps.ExistsAsync(ctx.Project, ctx.Userid, ct);
                if (permsetExists)
                {
                    await EmitAsync(ctx, permCode, action, "deny", PermissionCheckSource.RedisPermset, "PermissionNotInPermset");
                    return PermissionCheckResult.Deny(PermissionCheckSource.RedisPermset, "PermissionNotInPermset");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis permset check failed, falling back to Casbin.");
            }
        }

        // 4. NetCasbin Enforce 兜底
        try
        {
            _ = _casbinEnforcer.CheckAndReloadIfNeededAsync(ctx.Project, ct);

            var casbinResult = await _casbinEnforcer.EnforceAsync(ctx.Userid, ctx.Project, permCode, action, ct);
            if (casbinResult)
            {
                _ = Task.Run(() => _snapshotService.RebuildSnapshotAsync(ctx.Userid, ctx.Project, ct), ct);
                await EmitAsync(ctx, permCode, action, "allow", PermissionCheckSource.NetCasbin);
                return PermissionCheckResult.Allow(PermissionCheckSource.NetCasbin);
            }

            await EmitAsync(ctx, permCode, action, "deny", PermissionCheckSource.NetCasbin, "CasbinDenied");
            return PermissionCheckResult.Deny(PermissionCheckSource.NetCasbin, "CasbinDenied");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Casbin Enforce failed userid={U} project={P}", ctx.Userid, ctx.Project);
            await EmitAsync(ctx, permCode, action, "error", PermissionCheckSource.Fallback, ex.Message);
            return PermissionCheckResult.Deny(PermissionCheckSource.Fallback, "ServiceUnavailable");
        }
    }

    private Task EmitAsync(
        Security.CurrentRbacContext ctx, string permCode, string action,
        string result, PermissionCheckSource source, string? reason = null) =>
        _auditEmitter.EmitAsync(new AuthorizationAuditEvent
        {
            Userid = ctx.Userid,
            Project = ctx.Project,
            TraceId = ctx.TraceId,
            PermissionCode = permCode,
            Action = action,
            Result = result,
            Reason = reason ?? source.ToString(),
        });
}

/// <summary>
/// Casbin Enforcer 操作抽象接口。
/// Application 層通過此接口調用 Casbin，不直接引用 Infrastructure.Casbin 或 NetCasbin SDK。
/// 由 Rbac.Infrastructure.Casbin.CasbinEnforcerProvider 實現。
/// </summary>
public interface ICasbinEnforcer
{
    Task<bool> EnforceAsync(string userid, string project, string permissionCode, string action, CancellationToken ct = default);
    Task CheckAndReloadIfNeededAsync(string project, CancellationToken ct = default);
}
