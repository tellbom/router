using Microsoft.Extensions.Logging;
using Rbac.Application.Management;
using Rbac.Application.Repositories;
using Rbac.Domain.Groups;
using Rbac.Domain.Projects;
using Rbac.Domain.Users;
using Rbac.Domain.ValueObjects;

namespace Rbac.Application.Global;

/// <summary>
/// IGlobalManagementService 实现。
///
/// 核心设计：per-project 逐项 fan-out，每个 project 独立事务。
/// 所有写操作委托 RbacManagementWriteGuard（前置 MySQL 加载）+
/// IRbacManagementWriteService（写入 + 同事务 Outbox），不引入新写路径。
///
/// G005 compat-blocker：每个目标 project 都通过 RbacGlobalConstants.IsReservedProject()
/// 检查，保留系统 __global__ 始终被排除在跨 project 操作之外。
/// </summary>
public sealed class GlobalManagementService : IGlobalManagementService
{
    private readonly IAdministratorRepository   _adminRepo;
    private readonly IProjectGrantRepository    _grantRepo;
    private readonly IGroupRepository           _groupRepo;
    private readonly IGroupMemberRepository     _memberRepo;
    private readonly IRbacManagementWriteService _write;
    private readonly RbacManagementWriteGuard   _guard;
    private readonly ILogger<GlobalManagementService> _logger;

    public GlobalManagementService(
        IAdministratorRepository   adminRepo,
        IProjectGrantRepository    grantRepo,
        IGroupRepository           groupRepo,
        IGroupMemberRepository     memberRepo,
        IRbacManagementWriteService write,
        RbacManagementWriteGuard   guard,
        ILogger<GlobalManagementService> logger)
    {
        _adminRepo  = adminRepo;
        _grantRepo  = grantRepo;
        _groupRepo  = groupRepo;
        _memberRepo = memberRepo;
        _write      = write;
        _guard      = guard;
        _logger     = logger;
    }

    // ── 1. 用户项目授权 fan-out ────────────────────────────────────

    public async Task<PerProjectResultReport> GrantUserToProjectsAsync(
        string userid,
        string? username,
        IReadOnlyList<string> targetProjects,
        bool isSuper,
        string operatorUserid,
        CancellationToken ct = default)
    {
        var results = new List<ProjectOperationResult>();

        // 确保管理员账号存在（全局唯一，只需创建一次）
        var admin = await _adminRepo.FindByUseridAsync(new UserId(userid), ct);

        if (admin is null)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                // 无用户名无法自动创建：对每个目标 project 均记录失败
                foreach (var p in targetProjects)
                {
                    if (RbacGlobalConstants.IsReservedProject(p)) continue;
                    results.Add(Fail(p, $"用户 {userid} 不存在于 rbac_administrator 且未提供 username，无法自动创建"));
                }
                return Report(results);
            }

            admin = RbacAdministrator.Create(Guid.NewGuid(), new UserId(userid), username!);
            try
            {
                await _write.SaveAdministratorAsync(
                    admin,
                    changedFields: new[] { "created" },
                    oldStatus: null,
                    affectedGroupCodes: Array.Empty<string>(),
                    operatorUserid: operatorUserid,
                    ct: ct);
                _logger.LogInformation(
                    "GlobalManagementService: auto-created admin userid={U} operator={Op}",
                    userid, operatorUserid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GlobalManagementService: failed to create admin userid={U}", userid);
                foreach (var p in targetProjects)
                {
                    if (RbacGlobalConstants.IsReservedProject(p)) continue;
                    results.Add(Fail(p, $"创建管理员账号失败: {ex.Message}"));
                }
                return Report(results);
            }
        }

        // 预加载当前已有的 project 授权集合（用于计算 newProjects 字段）
        var currentGrants = (await _grantRepo.FindByUseridAsync(admin.Userid, ct))
            .Select(g => g.Project.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var targetProject in targetProjects)
        {
            // G005 compat-blocker：排除保留系统
            if (RbacGlobalConstants.IsReservedProject(targetProject))
            {
                _logger.LogDebug("GrantUserToProjects: skipping reserved project={P}", targetProject);
                continue;
            }

            try
            {
                // 幂等检查：已有授权则跳过
                if (currentGrants.Contains(targetProject))
                {
                    results.Add(Skip(targetProject));
                    continue;
                }

                var grantKind = isSuper ? "SuperGranted" : "Granted";
                var grant = RbacProjectGrant.Create(
                    Guid.NewGuid(),
                    admin.Userid,
                    new ProjectCode(targetProject),
                    grantedBy: operatorUserid,
                    isSuper: isSuper);

                var newProjects = currentGrants.Append(targetProject).ToList();

                await _write.SaveProjectGrantAsync(
                    grant,
                    grantKind,
                    oldProjects: Array.Empty<string>(),
                    newProjects: newProjects,
                    oldSuper: false,
                    operatorUserid: operatorUserid,
                    ct: ct);

                currentGrants.Add(targetProject); // 保存成功后再追踪，避免后续结果包含失败项目。
                results.Add(Ok(targetProject));
                _logger.LogInformation(
                    "GlobalManagementService: granted userid={U} project={P} super={S} operator={Op}",
                    userid, targetProject, isSuper, operatorUserid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "GlobalManagementService: grant failed userid={U} project={P}", userid, targetProject);
                results.Add(Fail(targetProject, ex.Message));
            }
        }

        return Report(results);
    }

    public async Task<PerProjectResultReport> RevokeUserFromProjectsAsync(
        string userid,
        IReadOnlyList<string> targetProjects,
        string operatorUserid,
        CancellationToken ct = default)
    {
        var results = new List<ProjectOperationResult>();

        // 预加载当前所有授权（用于计算 remainingProjects）
        var allGrants = (await _grantRepo.FindByUseridAsync(new UserId(userid), ct))
            .ToDictionary(g => g.Project.Value, StringComparer.OrdinalIgnoreCase);

        var remainingProjects = allGrants.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var targetProject in targetProjects)
        {
            // G005 compat-blocker：排除保留系统
            if (RbacGlobalConstants.IsReservedProject(targetProject))
            {
                _logger.LogDebug("RevokeUserFromProjects: skipping reserved project={P}", targetProject);
                continue;
            }

            try
            {
                if (!allGrants.TryGetValue(targetProject, out var grant))
                {
                    results.Add(Skip(targetProject)); // 未授权，幂等跳过
                    continue;
                }

                remainingProjects.Remove(targetProject);

                await _write.RevokeProjectGrantAsync(
                    grant,
                    remainingProjects: remainingProjects.ToList(),
                    operatorUserid: operatorUserid,
                    ct: ct);

                results.Add(Ok(targetProject));
                _logger.LogInformation(
                    "GlobalManagementService: revoked userid={U} project={P} operator={Op}",
                    userid, targetProject, operatorUserid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "GlobalManagementService: revoke failed userid={U} project={P}", userid, targetProject);
                results.Add(Fail(targetProject, ex.Message));
            }
        }

        return Report(results);
    }

    // ── 2. 权限组成员 (单 project) ─────────────────────────────────

    public async Task<PerProjectResultReport> AddUserToGroupAsync(
        string userid,
        string groupCode,
        string targetProject,
        string operatorUserid,
        CancellationToken ct = default)
    {
        // G005 compat-blocker
        if (RbacGlobalConstants.IsReservedProject(targetProject))
            return Report(new[] { Fail(targetProject, "不允许操作保留系统 project") });

        try
        {
            // 从 MySQL 加载权限组（WriteGuard 语义）
            var group = await _guard.LoadGroupByCodeAsync(groupCode, targetProject, ct);
            if (group is null)
                return Report(new[] { Fail(targetProject, $"权限组 {groupCode} 在 project {targetProject} 中不存在") });

            // 验证用户存在
            var admin = await _adminRepo.FindByUseridAsync(new UserId(userid), ct);
            if (admin is null)
                return Report(new[] { Fail(targetProject, $"用户 {userid} 不存在于 rbac_administrator") });

            // 幂等检查：是否已是成员
            var members = await _memberRepo.FindByUseridAndProjectAsync(userid, targetProject, ct);
            if (members.Any(m => m.GroupCode.Value.Equals(groupCode, StringComparison.OrdinalIgnoreCase)))
                return Report(new[] { Skip(targetProject) });

            var member = RbacGroupMember.Create(
                Guid.NewGuid(),
                admin.Userid,
                group.GroupCode,
                group.Project,
                grantedBy: operatorUserid);

            await _write.SaveGroupMemberAsync(
                member,
                affectedUserids: new[] { userid },
                groupPermissionCodes: group.PermissionCodes.Select(p => p.Value).ToList(),
                operatorUserid: operatorUserid,
                ct: ct);

            _logger.LogInformation(
                "GlobalManagementService: addUserToGroup userid={U} group={G} project={P} operator={Op}",
                userid, groupCode, targetProject, operatorUserid);

            return Report(new[] { Ok(targetProject) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GlobalManagementService: addUserToGroup failed userid={U} group={G} project={P}",
                userid, groupCode, targetProject);
            return Report(new[] { Fail(targetProject, ex.Message) });
        }
    }

    public async Task<PerProjectResultReport> RemoveUserFromGroupAsync(
        string userid,
        string groupCode,
        string targetProject,
        string operatorUserid,
        CancellationToken ct = default)
    {
        // G005 compat-blocker
        if (RbacGlobalConstants.IsReservedProject(targetProject))
            return Report(new[] { Fail(targetProject, "不允许操作保留系统 project") });

        try
        {
            var group = await _guard.LoadGroupByCodeAsync(groupCode, targetProject, ct);
            if (group is null)
                return Report(new[] { Fail(targetProject, $"权限组 {groupCode} 在 project {targetProject} 中不存在") });

            // 查找成员记录（由同一 DbContext 追踪，可直接传给 DeleteGroupMemberAsync）
            var members = await _memberRepo.FindByUseridAndProjectAsync(userid, targetProject, ct);
            var member = members.FirstOrDefault(
                m => m.GroupCode.Value.Equals(groupCode, StringComparison.OrdinalIgnoreCase));

            if (member is null)
                return Report(new[] { Skip(targetProject) }); // 不是成员，幂等跳过

            await _write.DeleteGroupMemberAsync(
                member,
                affectedUserids: new[] { userid },
                groupPermissionCodes: group.PermissionCodes.Select(p => p.Value).ToList(),
                operatorUserid: operatorUserid,
                ct: ct);

            _logger.LogInformation(
                "GlobalManagementService: removeUserFromGroup userid={U} group={G} project={P} operator={Op}",
                userid, groupCode, targetProject, operatorUserid);

            return Report(new[] { Ok(targetProject) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GlobalManagementService: removeUserFromGroup failed userid={U} group={G} project={P}",
                userid, groupCode, targetProject);
            return Report(new[] { Fail(targetProject, ex.Message) });
        }
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private static PerProjectResultReport Report(IEnumerable<ProjectOperationResult> results) =>
        new() { Results = results.ToList() };

    private static ProjectOperationResult Ok(string project) =>
        new() { Project = project, Success = true };

    private static ProjectOperationResult Skip(string project) =>
        new() { Project = project, Success = true, Skipped = true };

    private static ProjectOperationResult Fail(string project, string error) =>
        new() { Project = project, Success = false, ErrorMessage = error };
}
