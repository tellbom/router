namespace Rbac.Api.Options;

/// <summary>
/// Routes that require an authenticated user with project access, but no
/// concrete permissionCode mapping.
/// </summary>
public sealed class RbacProjectAccessAllowlistOptions
{
    public const string SectionName = "ProjectAccessAllowlist";

    public IList<string> Routes { get; set; } = new List<string>
    {
        "/api/admin/index",
    };

    public bool IsAllowed(string path) =>
        Routes.Any(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase));
}
