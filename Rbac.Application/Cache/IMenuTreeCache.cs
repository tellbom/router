using Rbac.Application.Contracts.Menus;

namespace Rbac.Application.Cache;

/// <summary>
/// 菜單樹緩存操作抽象接口。
/// Application 層通過此接口訪問緩存，不直接引用 Infrastructure.Redis。
/// 由 Rbac.Infrastructure.Redis.RbacFusionCacheFacade 實現。
/// </summary>
public interface IMenuTreeCache
{
    Task<IReadOnlyList<MenuNodeDto>?> GetMenuTreeAsync(
        string project,
        Func<CancellationToken, Task<IReadOnlyList<MenuNodeDto>?>> factory,
        CancellationToken ct = default);

    Task EvictMenuTreeAsync(string project);
}
