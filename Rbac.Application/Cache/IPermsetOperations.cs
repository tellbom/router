namespace Rbac.Application.Cache;

/// <summary>
/// permset Redis 操作抽象接口。
/// Application 層只依賴此接口，不直接引用 StackExchange.Redis 或 Infrastructure.Redis。
/// 由 Rbac.Infrastructure.Redis.RbacPermsetStore 實現。
/// </summary>
public interface IPermsetOperations
{
    Task<bool> IsMemberAsync(string project, string userid, string permissionCode, string action, CancellationToken ct = default);
    Task<bool> ExistsAsync(string project, string userid, CancellationToken ct = default);
    Task DeleteAsync(string project, string userid, CancellationToken ct = default);
}

/// <summary>
/// 版本號操作抽象接口。
/// Application 層只依賴此接口，不直接引用 StackExchange.Redis。
/// 由 Rbac.Infrastructure.Redis 實現。
/// </summary>
public interface IVersionOperations
{
    Task<long> ReadVersionAsync(string key);
}
