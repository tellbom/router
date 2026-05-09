namespace Rbac.Application.Cache;

/// <summary>
/// Redis 版本號讀取抽象接口。
/// Application 層通過此接口讀取版本，不直接引用 StackExchange.Redis。
/// 由 Rbac.Infrastructure.Redis 實現。
/// </summary>
public interface IVersionStore
{
    Task<long> ReadProjectVersionAsync(string project);
    Task<long> ReadUserVersionAsync(string project, string userid);
    Task<long> ReadPolicyVersionAsync(string project);
    Task<long> ReadGroupVersionAsync(string project, string groupCode);
}
