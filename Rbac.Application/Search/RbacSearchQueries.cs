using Rbac.Application.Contracts.Common;

namespace Rbac.Application.Search;

/// <summary>
/// 管理端查詢入參（Application 層定義，不引用 Infrastructure）。
/// </summary>
public abstract class EsManagementQuery : PagedQuery
{
    public string? Project { get; init; }
    public string? Status { get; init; }
    public string? Keyword { get; init; }
}

public sealed class UserSearchQuery : EsManagementQuery
{
    public string? Userid { get; init; }
    public string? GroupCode { get; init; }
}

public sealed class GroupSearchQuery : EsManagementQuery
{
    public string? GroupCode { get; init; }
    public string? PermissionCode { get; init; }
}

public sealed class RuleSearchQuery : EsManagementQuery
{
    public string? RuleCode { get; init; }
    public string? PermissionCode { get; init; }
    public string? Type { get; init; }
    public string? MenuType { get; init; }
}

public sealed class PermissionViewSearchQuery : EsManagementQuery
{
    public string? PermissionCode { get; init; }
    public string? Action { get; init; }
    public string? ResourceType { get; init; }
}

public sealed class AuditLogSearchQuery : EsManagementQuery
{
    public string? Userid { get; init; }
    public string? PermissionCode { get; init; }
    public string? Result { get; init; }
    public string? HttpMethod { get; init; }
    public DateTimeOffset? CreatedAtFrom { get; init; }
    public DateTimeOffset? CreatedAtTo { get; init; }
}
