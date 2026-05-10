using Microsoft.EntityFrameworkCore;
using Rbac.Application.Repositories;
using Rbac.Domain.Groups;
using Rbac.Infrastructure.MySql.Mapping;

namespace Rbac.Infrastructure.MySql.Repositories;

/// <summary>
/// IGroupMemberRepository 的 EF Core 实现。
/// 依赖 wave-grouping 补丁引入的 RbacDbContext.GroupMembers DbSet。
/// 只读接口：写操作由 RbacManagementWriteService 直接操作 DbContext 保证事务。
/// </summary>
public sealed class GroupMemberRepository : IGroupMemberRepository
{
    private readonly RbacDbContext _db;

    public GroupMemberRepository(RbacDbContext db) => _db = db;

    public async Task<IReadOnlyList<RbacGroupMember>> FindByUseridAndProjectAsync(
        string userid, string project, CancellationToken ct = default)
    {
        return await _db.GroupMembers
            .Where(m => m.Userid.Value == userid && m.Project.Value == project)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RbacGroupMember>> FindByGroupCodeAndProjectAsync(
        string groupCode, string project, CancellationToken ct = default)
    {
        return await _db.GroupMembers
            .Where(m => m.GroupCode.Value == groupCode && m.Project.Value == project)
            .ToListAsync(ct);
    }
}
