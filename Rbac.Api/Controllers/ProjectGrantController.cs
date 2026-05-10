using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Management;
using Rbac.Application.Repositories;
using Rbac.Application.Security;
using Rbac.Domain.Projects;
using Rbac.Domain.ValueObjects;

namespace Rbac.Api.Controllers;

/// <summary>
/// 用户-project 授权管理接口。
///
/// 写操作属于高风险（super 变更、授权变更会立即触发 Redis 缓存主动删除）。
/// 所有写操作通过 IProjectGrantRepository 加载已有记录（EditGuard 语义），
/// 再通过 IRbacManagementWriteService 保证同事务 Outbox。
/// project 来自 CurrentRbacContext，操作范围限定在当前 project 下。
/// </summary>
[ApiController]
[Route("api/project-grant")]
public sealed class ProjectGrantController : ControllerBase
{
    private readonly ICurrentRbacContextAccessor _ctx;
    private readonly IRbacManagementWriteService _write;
    private readonly IProjectGrantRepository _grantRepo;

    public ProjectGrantController(
        ICurrentRbacContextAccessor ctx,
        IRbacManagementWriteService write,
        IProjectGrantRepository grantRepo)
    {
        _ctx = ctx;
        _write = write;
        _grantRepo = grantRepo;
    }

    // ── 授权用户到当前 project ─────────────────────────────────────

    /// <summary>POST /api/project-grant — 将用户授权到当前 project。</summary>
    [HttpPost]
    public async Task<ApiResponse<object>> Grant(
        [FromBody] GrantProjectRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(req.Userid)) return Fail(40001, "userid 不能为空");

        var userid = new UserId(req.Userid);
        var project = new ProjectCode(ctx.Project);

        // 检查是否已存在（已存在则变为更新）
        var existing = await _grantRepo.FindAsync(userid, project, ct);

        RbacProjectGrant grant;
        string grantKind;
        IReadOnlyList<string> oldProjects;

        if (existing is null)
        {
            grant = RbacProjectGrant.Create(Guid.NewGuid(), userid, project,
                grantedBy: ctx.Userid, isSuper: req.IsSuper);
            grantKind = req.IsSuper ? "SuperGranted" : "Granted";
            oldProjects = Array.Empty<string>();
        }
        else
        {
            // 已有授权，仅更新 super 标志
            grant = existing;
            var oldSuper = existing.IsSuper;
            if (req.IsSuper) grant.GrantSuper(); else grant.RevokeSuper();
            grantKind = req.IsSuper ? "SuperGranted" : "SuperRevoked";
            oldProjects = new[] { ctx.Project };

            await _write.SaveProjectGrantAsync(
                grant, grantKind,
                oldProjects,
                newProjects: new[] { ctx.Project },
                oldSuper,
                operatorUserid: ctx.Userid,
                ct);
            return ApiResponse<object>.Ok(null!);
        }

        // 查询该用户当前已有的其他 project（用于 NewProjects 字段）
        var currentGrants = await _grantRepo.FindByUseridAsync(userid, ct);
        var newProjects = currentGrants.Select(g => g.Project.Value)
            .Append(ctx.Project).Distinct().ToList();

        await _write.SaveProjectGrantAsync(
            grant, grantKind,
            oldProjects,
            newProjects,
            oldSuper: false,
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // ── 撤销用户的当前 project 授权 ─────────────────────────────────

    /// <summary>DELETE /api/project-grant/{userid} — 撤销指定用户在当前 project 的授权。</summary>
    [HttpDelete("{userid}")]
    public async Task<ApiResponse<object>> Revoke(string userid, CancellationToken ct)
    {
        var ctx = RequireContext();

        var uid = new UserId(userid);
        var project = new ProjectCode(ctx.Project);

        var grant = await _grantRepo.FindAsync(uid, project, ct);
        if (grant is null) return Fail(40400, "未找到授权记录");

        // 撤销后该用户剩余的 project 列表
        var currentGrants = await _grantRepo.FindByUseridAsync(uid, ct);
        var remainingProjects = currentGrants
            .Where(g => g.Project.Value != ctx.Project)
            .Select(g => g.Project.Value)
            .ToList();

        await _write.RevokeProjectGrantAsync(
            grant,
            remainingProjects,
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // ── super 快捷切换 ──────────────────────────────────────────────

    /// <summary>PUT /api/project-grant/{userid}/super — 快捷切换 super 状态。</summary>
    [HttpPut("{userid}/super")]
    public async Task<ApiResponse<object>> ToggleSuper(
        string userid, [FromBody] ToggleSuperRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        var uid = new UserId(userid);
        var project = new ProjectCode(ctx.Project);

        var grant = await _grantRepo.FindAsync(uid, project, ct);
        if (grant is null) return Fail(40400, "未找到授权记录，请先授权用户到此 project");

        var oldSuper = grant.IsSuper;
        string grantKind;
        if (req.IsSuper) { grant.GrantSuper(); grantKind = "SuperGranted"; }
        else { grant.RevokeSuper(); grantKind = "SuperRevoked"; }

        await _write.SaveProjectGrantAsync(
            grant, grantKind,
            oldProjects: new[] { ctx.Project },
            newProjects: new[] { ctx.Project },
            oldSuper,
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private CurrentRbacContext RequireContext() =>
        _ctx.Context ?? throw new InvalidOperationException("RbacContext missing");

    private static ApiResponse<object> Fail(int code, string msg) =>
        ApiResponse<object>.Fail(code, msg);
}

// ── Request DTOs ───────────────────────────────────────────────────

public sealed record GrantProjectRequest(string Userid, bool IsSuper = false);
public sealed record ToggleSuperRequest(bool IsSuper);
