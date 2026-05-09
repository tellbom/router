using StackExchange.Redis;
using Rbac.Application.Cache;
using Rbac.Application.Snapshots;

namespace Rbac.Infrastructure.Redis;

/// <summary>
/// IVersionStore 的 Redis 實現。
/// 讓 Application 層的 RbacVersionValidationService 和 RbacPermsetLazyRebuildCoordinator
/// 通過接口讀取版本，不直接依賴 StackExchange.Redis。
/// </summary>
public sealed class RedisVersionStore : IVersionStore
{
    private readonly IDatabase _db;

    public RedisVersionStore(IDatabase db) => _db = db;

    public async Task<long> ReadProjectVersionAsync(string project) =>
        await ReadAsync(RbacRedisKeys.VersionProject(project));

    public async Task<long> ReadUserVersionAsync(string project, string userid) =>
        await ReadAsync(RbacRedisKeys.VersionUser(project, userid));

    public async Task<long> ReadPolicyVersionAsync(string project) =>
        await ReadAsync(RbacRedisKeys.PolicyVersion(project));

    public async Task<long> ReadGroupVersionAsync(string project, string groupCode) =>
        await ReadAsync(RbacRedisKeys.VersionGroup(project, groupCode));

    private async Task<long> ReadAsync(string key)
    {
        try { return (long?)await _db.StringGetAsync(key) ?? 0L; }
        catch { return 0L; }
    }
}

/// <summary>
/// IPermsetOperations 的 Redis 實現。
/// RbacPermissionChecker 通過此接口調用 SISMEMBER，不直接引用 StackExchange.Redis。
/// </summary>
public sealed class RedisPermsetOperations : IPermsetOperations
{
    private readonly IDatabase _db;

    public RedisPermsetOperations(IDatabase db) => _db = db;

    public async Task<bool> IsMemberAsync(
        string project, string userid, string permissionCode, string action,
        CancellationToken ct = default)
    {
        var key = RbacRedisKeys.Permset(project, userid);
        var member = $"{permissionCode}:{action}";
        try { return await _db.SetContainsAsync(key, member); }
        catch { return false; }
    }

    public async Task<bool> ExistsAsync(string project, string userid, CancellationToken ct = default)
    {
        try { return await _db.KeyExistsAsync(RbacRedisKeys.Permset(project, userid)); }
        catch { return false; }
    }

    public Task DeleteAsync(string project, string userid, CancellationToken ct = default) =>
        _db.KeyDeleteAsync(RbacRedisKeys.Permset(project, userid));
}
