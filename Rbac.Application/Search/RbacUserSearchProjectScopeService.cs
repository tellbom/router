using Rbac.Application.Contracts.Common;
using Rbac.Application.Repositories;
using Rbac.Domain.ValueObjects;

namespace Rbac.Application.Search;

/// <summary>
/// Replaces project-scoped user fields with relational truth.
/// The ES user document intentionally aggregates data from every project, so
/// non-global endpoints must not return those arrays without scoping them.
/// </summary>
public sealed class RbacUserSearchProjectScopeService
{
    private readonly IGroupMemberRepository _memberRepository;
    private readonly IGroupRepository _groupRepository;

    public RbacUserSearchProjectScopeService(
        IGroupMemberRepository memberRepository,
        IGroupRepository groupRepository)
    {
        _memberRepository = memberRepository;
        _groupRepository = groupRepository;
    }

    public async Task<PagedData<UserSearchResult>> ScopeAsync(
        PagedData<UserSearchResult> data,
        string project,
        bool preserveCrossProjectGrants,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(project) || data.List.Count == 0)
            return data;

        var membersTask = _memberRepository.FindByProjectAsync(project, ct);
        var groupsTask = _groupRepository.FindByProjectAsync(new ProjectCode(project), ct);
        await Task.WhenAll(membersTask, groupsTask);

        var groupNames = groupsTask.Result.ToDictionary(
            group => group.GroupCode.Value,
            group => group.GroupName,
            StringComparer.OrdinalIgnoreCase);
        var memberships = membersTask.Result
            .GroupBy(member => member.Userid.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(member => member.GroupCode.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        return new PagedData<UserSearchResult>
        {
            Total = data.Total,
            List = data.List.Select(user =>
            {
                var codes = memberships.TryGetValue(user.Userid, out var scopedCodes)
                    ? scopedCodes
                    : new List<string>();
                var projectCodes = preserveCrossProjectGrants
                    ? user.ProjectCodes
                    : user.ProjectCodes
                        .Where(code => string.Equals(code, project, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                var superProjects = preserveCrossProjectGrants
                    ? user.SuperProjects
                    : user.SuperProjects
                        .Where(code => string.Equals(code, project, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                return new UserSearchResult
                {
                    Userid = user.Userid,
                    Username = user.Username,
                    Status = user.Status,
                    ProjectCodes = projectCodes,
                    GroupCodes = codes,
                    GroupNames = codes
                        .Where(groupNames.ContainsKey)
                        .Select(code => groupNames[code])
                        .ToList(),
                    SuperProjects = superProjects,
                    IsSuper = superProjects.Contains(project, StringComparer.OrdinalIgnoreCase),
                };
            }).ToList(),
        };
    }

    public static PagedData<UserSearchResult> SetIsSuperForProject(
        PagedData<UserSearchResult> data,
        string project)
    {
        return new PagedData<UserSearchResult>
        {
            Total = data.Total,
            List = data.List.Select(user => new UserSearchResult
            {
                Userid = user.Userid,
                Username = user.Username,
                Status = user.Status,
                ProjectCodes = user.ProjectCodes,
                GroupCodes = user.GroupCodes,
                GroupNames = user.GroupNames,
                SuperProjects = user.SuperProjects,
                IsSuper = user.SuperProjects.Contains(project, StringComparer.OrdinalIgnoreCase),
            }).ToList(),
        };
    }
}
