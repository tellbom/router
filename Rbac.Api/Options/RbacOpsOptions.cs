namespace Rbac.Api.Options;

/// <summary>
/// Options for ops endpoints under /ops.
/// </summary>
public sealed class RbacOpsOptions
{
    public const string SectionName = "Ops";

    /// <summary>
    /// Required value for the X-Ops-Key header. Empty means all ops requests are denied.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
