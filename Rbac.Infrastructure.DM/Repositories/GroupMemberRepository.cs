using Microsoft.EntityFrameworkCore;
using Rbac.Application.Repositories;
using Rbac.Domain.Groups;
using Rbac.Domain.ValueObjects;
using Rbac.Infrastructure.DM.Mapping;

namespace Rbac.Infrastructure.DM.Repositories;

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
        var userId = new UserId(userid);
        var projectCode = new ProjectCode(project);

        return await _db.GroupMembers
            .Where(m => m.Userid == userId && m.Project == projectCode)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RbacGroupMember>> FindByGroupCodeAndProjectAsync(
        string groupCode, string project, CancellationToken ct = default)
    {
        var group = new GroupCode(groupCode);
        var projectCode = new ProjectCode(project);

        return await _db.GroupMembers
            .Where(m => m.GroupCode == group && m.Project == projectCode)
            .ToListAsync(ct);
    }
}
