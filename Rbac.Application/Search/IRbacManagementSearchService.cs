using Rbac.Application.Contracts.Common;

namespace Rbac.Application.Search;

/// <summary>
/// 管理端 ES 查詢服務接口（Application 層定義，Infrastructure.Elasticsearch 實現）。
/// </summary>
public interface IRbacManagementSearchService
{
    Task<PagedData<UserSearchResult>> SearchUsersAsync(UserSearchQuery query, CancellationToken ct = default);
    Task<PagedData<GroupSearchResult>> SearchGroupsAsync(GroupSearchQuery query, CancellationToken ct = default);
    Task<PagedData<RuleSearchResult>> SearchRulesAsync(RuleSearchQuery query, CancellationToken ct = default);
    Task<PagedData<PermissionViewSearchResult>> SearchPermissionViewAsync(PermissionViewSearchQuery query, CancellationToken ct = default);
    Task<PagedData<AuditLogSearchResult>> SearchAuditLogsAsync(AuditLogSearchQuery query, CancellationToken ct = default);
}

public sealed class UserSearchResult { public string DxEId { get; init; } = string.Empty; public string Userid { get; init; } = string.Empty; public string Username { get; init; } = string.Empty; public string Status { get; init; } = string.Empty; public IList<string> ProjectCodes { get; init; } = new List<string>(); public IList<string> GroupCodes { get; init; } = new List<string>(); public IList<string> GroupNames { get; init; } = new List<string>(); }
public sealed class GroupSearchResult { public string DxEId { get; init; } = string.Empty; public string GroupCode { get; init; } = string.Empty; public string GroupName { get; init; } = string.Empty; public string Project { get; init; } = string.Empty; public string Status { get; init; } = string.Empty; public IList<string> PermissionCodes { get; init; } = new List<string>(); }
public sealed class RuleSearchResult { public string DxEId { get; init; } = string.Empty; public string RuleCode { get; init; } = string.Empty; public string PermissionCode { get; init; } = string.Empty; public string Title { get; init; } = string.Empty; public string Type { get; init; } = string.Empty; public string Status { get; init; } = string.Empty; }
public sealed class PermissionViewSearchResult { public string PermissionCode { get; init; } = string.Empty; public string Action { get; init; } = string.Empty; public string ResourceType { get; init; } = string.Empty; public string Title { get; init; } = string.Empty; }
public sealed class AuditLogSearchResult { public string AuditId { get; init; } = string.Empty; public string Userid { get; init; } = string.Empty; public string Project { get; init; } = string.Empty; public string PermissionCode { get; init; } = string.Empty; public string Result { get; init; } = string.Empty; public string Reason { get; init; } = string.Empty; public DateTimeOffset CreatedAt { get; init; } }
