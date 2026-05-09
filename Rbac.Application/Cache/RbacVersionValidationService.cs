using Microsoft.Extensions.Logging;
using Rbac.Application.Snapshots;

namespace Rbac.Application.Cache;

/// <summary>
/// 版本懒失效验证服务。
/// 通過 IVersionStore 接口讀取版本，不直接引用 StackExchange.Redis。
/// </summary>
public sealed class RbacVersionValidationService
{
    private readonly IVersionStore _versionStore;
    private readonly ILogger<RbacVersionValidationService> _logger;

    public RbacVersionValidationService(IVersionStore versionStore, ILogger<RbacVersionValidationService> logger)
    {
        _versionStore = versionStore;
        _logger = logger;
    }

    public async Task<VersionCheckResult> CheckSnapshotAsync(
        UserPermissionSnapshot snapshot, CancellationToken ct = default)
    {
        var project = snapshot.Project;
        var userid = snapshot.Userid;

        var projectVerTask = _versionStore.ReadProjectVersionAsync(project);
        var userVerTask = _versionStore.ReadUserVersionAsync(project, userid);
        var policyVerTask = _versionStore.ReadPolicyVersionAsync(project);

        await Task.WhenAll(projectVerTask, userVerTask, policyVerTask);

        var staleReasons = new List<string>();

        if (snapshot.Versions.Project < projectVerTask.Result)
            staleReasons.Add($"project version stale: snap={snapshot.Versions.Project} current={projectVerTask.Result}");
        if (snapshot.Versions.User < userVerTask.Result)
            staleReasons.Add($"user version stale: snap={snapshot.Versions.User} current={userVerTask.Result}");
        if (snapshot.Versions.Policy < policyVerTask.Result)
            staleReasons.Add($"policy version stale: snap={snapshot.Versions.Policy} current={policyVerTask.Result}");

        if (staleReasons.Count > 0)
        {
            _logger.LogDebug("Snapshot stale userid={U} project={P} reasons={R}",
                userid, project, string.Join("; ", staleReasons));
            return VersionCheckResult.Stale(staleReasons);
        }

        return VersionCheckResult.Fresh();
    }

    public async Task<VersionSnapshot> ReadAllVersionsAsync(
        string project, string userid, string? groupCode = null)
    {
        var projectVer = await _versionStore.ReadProjectVersionAsync(project);
        var userVer = await _versionStore.ReadUserVersionAsync(project, userid);
        var policyVer = await _versionStore.ReadPolicyVersionAsync(project);
        var groupVer = groupCode is null ? 0L
            : await _versionStore.ReadGroupVersionAsync(project, groupCode);

        return new VersionSnapshot
        {
            ProjectVersion = projectVer,
            UserVersion = userVer,
            PolicyVersion = policyVer,
            GroupVersion = groupVer,
        };
    }
}

public sealed class VersionCheckResult
{
    public bool IsStale { get; private init; }
    public IReadOnlyList<string> StaleReasons { get; private init; } = Array.Empty<string>();

    public static VersionCheckResult Fresh() => new() { IsStale = false };
    public static VersionCheckResult Stale(IReadOnlyList<string> reasons) =>
        new() { IsStale = true, StaleReasons = reasons };
}

public sealed class VersionSnapshot
{
    public long ProjectVersion { get; init; }
    public long UserVersion { get; init; }
    public long PolicyVersion { get; init; }
    public long GroupVersion { get; init; }
}
