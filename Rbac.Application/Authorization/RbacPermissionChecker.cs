using Microsoft.Extensions.Logging;
using Rbac.Application.Auditing;
using Rbac.Application.Snapshots;
using Rbac.Infrastructure.Casbin;
using Rbac.Infrastructure.Redis;

namespace Rbac.Application.Authorization;

/// <summary>
/// <see cref="IRbacPermissionChecker"/> 实现。
///
/// 鉴权判定顺序（按设计文档 §2.2）：
/// 1. IsProjectSuper → allow（仍写审计）
/// 2. Redis permset SISMEMBER（直接调用 StackExchange.Redis，不经过 FusionCache）
/// 3. permset miss 或版本 stale → NetCasbin Enforce 兜底
/// 4. Casbin allow 后可选触发 permset 懒重建
/// 5. 全部不可用 → deny（安全降级）
///
/// 每次判断结果均异步发射审计事件，不阻塞主请求。
/// </summary>
public sealed class RbacPermissionChecker : IRbacPermissionChecker
{
    private readonly RbacPermsetStore _permsetStore;
    private readonly CasbinPolicyVersionWatcher _casbinWatcher;
    private readonly IRbacSnapshotService _snapshotService;
    private readonly IAuditEventEmitter _auditEmitter;
    private readonly ILogger<RbacPermissionChecker> _logger;

    public RbacPermissionChecker(
        RbacPermsetStore permsetStore,
        CasbinPolicyVersionWatcher casbinWatcher,
        IRbacSnapshotService snapshotService,
        IAuditEventEmitter auditEmitter,
        ILogger<RbacPermissionChecker> logger)
    {
        _permsetStore = permsetStore;
        _casbinWatcher = casbinWatcher;
        _snapshotService = snapshotService;
        _auditEmitter = auditEmitter;
        _logger = logger;
    }

    public async Task<PermissionCheckResult> CheckAsync(
        PermissionCheckRequest request,
        CancellationToken ct = default)
    {
        var ctx = request.Context;
        var permCode = request.PermissionCode;
        var action = request.Action;

        // 1. project-scoped super → 直接放行（仍审计）
        if (ctx.IsProjectSuper)
        {
            _logger.LogDebug("SuperAllow userid={U} project={P}", ctx.Userid, ctx.Project);
            await EmitAuditAsync(ctx, permCode, action, "allow", PermissionCheckSource.ProjectSuper);
            return PermissionCheckResult.Allow(PermissionCheckSource.ProjectSuper);
        }

        // 2. 版本校验：获取快照，检查 policy version 是否仍有效
        var snapshot = await _snapshotService.GetSnapshotAsync(ctx.Userid, ctx.Project, ct);
        var isVersionStale = snapshot is null
            || snapshot.Versions.Policy != ctx.PolicyVersion;

        // 3. Redis permset SISMEMBER（直接 StackExchange.Redis，非 FusionCache）
        if (!isVersionStale)
        {
            try
            {
                var inPermset = await _permsetStore.IsMemberAsync(
                    ctx.Project, ctx.Userid, permCode, action, ct);

                if (inPermset)
                {
                    await EmitAuditAsync(ctx, permCode, action, "allow", PermissionCheckSource.RedisPermset);
                    return PermissionCheckResult.Allow(PermissionCheckSource.RedisPermset);
                }

                // permset 存在但不含此权限码 → deny（不走 Casbin，版本有效说明 permset 准确）
                var permsetExists = await _permsetStore.ExistsAsync(ctx.Project, ctx.Userid, ct);
                if (permsetExists)
                {
                    await EmitAuditAsync(ctx, permCode, action, "deny", PermissionCheckSource.RedisPermset,
                        "PermissionNotInPermset");
                    return PermissionCheckResult.Deny(PermissionCheckSource.RedisPermset, "PermissionNotInPermset");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis permset check failed, falling back to Casbin.");
            }
        }

        // 4. NetCasbin Enforce 兜底（permset miss 或版本 stale）
        try
        {
            // 后台检测 policy version 变化（不阻塞）
            _ = _casbinWatcher.CheckAndReloadIfNeededAsync(ctx.Project, ct);

            var enforcer = await _casbinWatcher.GetEnforcerAsync(ctx.Project, ct);
            var casbinResult = enforcer.Enforce(ctx.Userid, ctx.Project, permCode, action);

            if (casbinResult)
            {
                // Casbin allow 后异步触发 permset 懒重建（不阻塞当前请求）
                _ = Task.Run(() => _snapshotService.RebuildSnapshotAsync(ctx.Userid, ctx.Project, ct), ct);

                await EmitAuditAsync(ctx, permCode, action, "allow", PermissionCheckSource.NetCasbin);
                return PermissionCheckResult.Allow(PermissionCheckSource.NetCasbin);
            }

            await EmitAuditAsync(ctx, permCode, action, "deny", PermissionCheckSource.NetCasbin,
                "CasbinDenied");
            return PermissionCheckResult.Deny(PermissionCheckSource.NetCasbin, "CasbinDenied");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Casbin Enforce failed userid={U} project={P} permCode={PC} action={A}",
                ctx.Userid, ctx.Project, permCode, action);

            // 5. 全部不可用 → 安全降级拒绝
            await EmitAuditAsync(ctx, permCode, action, "error", PermissionCheckSource.Fallback,
                ex.Message);
            return PermissionCheckResult.Deny(PermissionCheckSource.Fallback, "ServiceUnavailable");
        }
    }

    // ── 审计 ──────────────────────────────────────────────────────

    private Task EmitAuditAsync(
        Application.Security.CurrentRbacContext ctx,
        string permCode, string action,
        string result, PermissionCheckSource source,
        string? reason = null)
    {
        return _auditEmitter.EmitAsync(new AuthorizationAuditEvent
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
}
