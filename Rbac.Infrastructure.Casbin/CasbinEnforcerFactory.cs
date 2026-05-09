using NetCasbin;
using Microsoft.Extensions.Logging;
using Rbac.Application.Policies;
using Rbac.Domain.ValueObjects;

namespace Rbac.Infrastructure.Casbin;

/// <summary>
/// Casbin Enforcer 工厂。
///
/// 约束（设计文档 §6.9 / §0.2）：
/// - 每次调用创建全新 Enforcer 实例，不复用共享实例。
/// - policy 数据只从 MySQL 真相表加载（通过 ICasbinGroupingPolicyReader / ICasbinPermissionPolicyReader）。
/// - 禁止从 Redis permset 或 ES 反向加载 policy。
/// - 工厂不持有 Enforcer 引用，生命周期由 CasbinEnforcerProvider 管理。
/// </summary>
public sealed class CasbinEnforcerFactory
{
    private readonly RbacCasbinModelProvider _modelProvider;
    private readonly ICasbinGroupingPolicyReader _groupingReader;
    private readonly ICasbinPermissionPolicyReader _permissionReader;
    private readonly ILogger<CasbinEnforcerFactory> _logger;

    public CasbinEnforcerFactory(
        RbacCasbinModelProvider modelProvider,
        ICasbinGroupingPolicyReader groupingReader,
        ICasbinPermissionPolicyReader permissionReader,
        ILogger<CasbinEnforcerFactory> logger)
    {
        _modelProvider = modelProvider;
        _groupingReader = groupingReader;
        _permissionReader = permissionReader;
        _logger = logger;
    }

    /// <summary>
    /// 从 MySQL 真相表构建新的 Enforcer 实例。
    /// 调用方负责在成功后原子替换引用，失败时保留旧引用（见 CasbinEnforcerProvider）。
    /// </summary>
    public async Task<Enforcer> BuildAsync(ProjectCode project, CancellationToken ct = default)
    {
        _logger.LogDebug("Building new Enforcer for project={P}", project.Value);

        // 从 MySQL 加载 g / p policy（唯一合法数据来源）
        var grouping = await _groupingReader.LoadAsync(project, ct);
        var permission = await _permissionReader.LoadAsync(project, ct);

        // 构建 Enforcer（不污染任何共享实例）
        var enforcer = _modelProvider.BuildEnforcer(grouping, permission);

        _logger.LogDebug(
            "Enforcer built project={P} g={G} p={P2}",
            project.Value, grouping.Count, permission.Count);

        return enforcer;
    }
}
