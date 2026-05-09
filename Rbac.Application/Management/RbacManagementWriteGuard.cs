using Microsoft.Extensions.Logging;
using Rbac.Application.Repositories;
using Rbac.Domain.Groups;
using Rbac.Domain.Permissions;
using Rbac.Domain.Rules;
using Rbac.Domain.Users;
using Rbac.Domain.ValueObjects;

namespace Rbac.Application.Management;

/// <summary>
/// 管理端写操作安全守卫。
///
/// 约束（设计文档 §3.1）：
/// ES 搜索结果不能直接作为编辑/删除的真相。
/// 任何写操作前必须从 MySQL 重新加载聚合根（按 project + DxEId 或 Guid），
/// 确认记录存在后才允许继续。
///
/// 防止以下危险模式：
/// - 前端将 ES 返回的 DxEId 直接用于删除，但 ES 数据已过期（记录已被删）。
/// - 前端修改了 ES 返回的字段值直接保存，绕过 MySQL 真相校验。
/// </summary>
public sealed class RbacManagementWriteGuard
{
    private readonly IAdministratorRepository _adminRepo;
    private readonly IGroupRepository _groupRepo;
    private readonly IRuleRepository _ruleRepo;
    private readonly IApiPermissionMapRepository _apiMapRepo;
    private readonly ILogger<RbacManagementWriteGuard> _logger;

    public RbacManagementWriteGuard(
        IAdministratorRepository adminRepo,
        IGroupRepository groupRepo,
        IRuleRepository ruleRepo,
        IApiPermissionMapRepository apiMapRepo,
        ILogger<RbacManagementWriteGuard> logger)
    {
        _adminRepo = adminRepo;
        _groupRepo = groupRepo;
        _ruleRepo = ruleRepo;
        _apiMapRepo = apiMapRepo;
        _logger = logger;
    }

    // ── 管理员 ────────────────────────────────────────────────────

    /// <summary>
    /// 按 DxEId 加载管理员，确认记录存在。
    /// 不存在时返回 null（调用方应返回 404）。
    /// </summary>
    public async Task<RbacAdministrator?> LoadAdminByDxEIdAsync(
        string dxeId, CancellationToken ct = default)
    {
        var admin = await _adminRepo.FindByDxEIdAsync(new DxEId(dxeId), ct);
        LogIfNotFound("Administrator", dxeId, admin is null);
        return admin;
    }

    public async Task<RbacAdministrator?> LoadAdminByGuidAsync(
        Guid id, CancellationToken ct = default)
    {
        var admin = await _adminRepo.FindByGuidAsync(id, ct);
        LogIfNotFound("Administrator", id.ToString(), admin is null);
        return admin;
    }

    // ── 权限组 ────────────────────────────────────────────────────

    /// <summary>按 DxEId 加载权限组，校验其 project 与请求 project 一致。</summary>
    public async Task<RbacGroup?> LoadGroupByDxEIdAsync(
        string dxeId, string project, CancellationToken ct = default)
    {
        var group = await _groupRepo.FindByDxEIdAsync(new DxEId(dxeId), ct);
        if (group is null) { LogIfNotFound("Group", dxeId, true); return null; }

        if (!string.Equals(group.Project.Value, project, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Group project mismatch dxeId={DxEId} groupProject={GP} requestProject={RP}",
                dxeId, group.Project.Value, project);
            return null;
        }

        return group;
    }

    public async Task<RbacGroup?> LoadGroupByGuidAsync(
        Guid id, CancellationToken ct = default)
    {
        var group = await _groupRepo.FindByGuidAsync(id, ct);
        LogIfNotFound("Group", id.ToString(), group is null);
        return group;
    }

    // ── 规则 ──────────────────────────────────────────────────────

    /// <summary>按 DxEId 加载规则，校验 project 一致。</summary>
    public async Task<RbacRule?> LoadRuleByDxEIdAsync(
        string dxeId, string project, CancellationToken ct = default)
    {
        var rule = await _ruleRepo.FindByDxEIdAsync(new DxEId(dxeId), ct);
        if (rule is null) { LogIfNotFound("Rule", dxeId, true); return null; }

        if (!string.Equals(rule.Project.Value, project, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Rule project mismatch dxeId={DxEId} ruleProject={RP} requestProject={ReqP}",
                dxeId, rule.Project.Value, project);
            return null;
        }

        return rule;
    }

    public async Task<RbacRule?> LoadRuleByGuidAsync(
        Guid id, CancellationToken ct = default)
    {
        var rule = await _ruleRepo.FindByGuidAsync(id, ct);
        LogIfNotFound("Rule", id.ToString(), rule is null);
        return rule;
    }

    // ── API 权限映射 ──────────────────────────────────────────────

    public async Task<RbacApiPermissionMap?> LoadApiMapByGuidAsync(
        Guid id, CancellationToken ct = default)
    {
        var map = await _apiMapRepo.FindByGuidAsync(id, ct);
        LogIfNotFound("ApiPermissionMap", id.ToString(), map is null);
        return map;
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private void LogIfNotFound(string resourceType, string key, bool notFound)
    {
        if (notFound)
            _logger.LogWarning(
                "WriteGuard: {Resource} not found in MySQL key={Key}. ES result may be stale.",
                resourceType, key);
    }
}
