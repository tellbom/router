using Microsoft.Extensions.Logging;
using Rbac.Application.Cache;
using Rbac.Application.Policies;
using Rbac.Application.Repositories;
using Rbac.Domain.ValueObjects;

namespace Rbac.Application.Snapshots;

/// <summary>
/// 請求鏈路 permset 懒重建協調器。
/// 通過 IPermsetOperations / IVersionStore 接口操作緩存，不直接引用 Infrastructure。
/// </summary>
public sealed class RbacPermsetLazyRebuildCoordinator
{
    private readonly IVersionStore _versionStore;
    private readonly IRbacPermsetBuilder _permsetBuilder;
    private readonly ICasbinGroupingPolicyReader _groupingReader;
    private readonly ICasbinPermissionPolicyReader _permissionReader;
    private readonly ILogger<RbacPermsetLazyRebuildCoordinator> _logger;

    public RbacPermsetLazyRebuildCoordinator(
        IVersionStore versionStore,
        IRbacPermsetBuilder permsetBuilder,
        ICasbinGroupingPolicyReader groupingReader,
        ICasbinPermissionPolicyReader permissionReader,
        ILogger<RbacPermsetLazyRebuildCoordinator> logger)
    {
        _versionStore = versionStore;
        _permsetBuilder = permsetBuilder;
        _groupingReader = groupingReader;
        _permissionReader = permissionReader;
        _logger = logger;
    }

    public async Task RebuildAsync(string userid, string project, CancellationToken ct = default)
    {
        try
        {
            await RebuildInternalAsync(userid, project, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Permset lazy rebuild failed userid={U} project={P}", userid, project);
        }
    }

    private async Task RebuildInternalAsync(string userid, string project, CancellationToken ct)
    {
        var versionAtStart = await _versionStore.ReadUserVersionAsync(project, userid);

        var projectCode = new ProjectCode(project);
        var grouping = await _groupingReader.LoadAsync(projectCode, ct);
        var permission = await _permissionReader.LoadAsync(projectCode, ct);

        var userGroups = grouping
            .Where(g => string.Equals(g.Userid, userid, StringComparison.OrdinalIgnoreCase))
            .Select(g => g.GroupCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var members = permission
            .Where(p => userGroups.Contains(p.GroupCode))
            .Select(p => $"{p.PermissionCode}:{p.Action}")
            .Distinct()
            .ToList();

        var input = new PermsetBuildInput
        {
            Userid = userid,
            Project = project,
            Members = members,
            VersionAtBuildTime = versionAtStart,
            Source = PermsetInputSource.DMCasbinDerived,
        };

        var written = await _permsetBuilder.BuildAndWriteAsync(input, ct);

        if (written)
            _logger.LogDebug("Permset lazy rebuild succeeded userid={U} project={P} members={C}", userid, project, members.Count);
        else
            _logger.LogDebug("Permset lazy rebuild discarded (version conflict) userid={U} project={P}", userid, project);
    }
}
