using Rbac.Domain.ValueObjects;

namespace Rbac.Domain.Permissions;

/// <summary>API 映射状态。</summary>
public enum ApiMapStatus
{
    Active,
    Disabled,
}

/// <summary>
/// API 路由到权限码的映射聚合根。
///
/// 记录 project 下每个 HTTP method + route pattern 对应的 permissionCode 和 action。
/// 服务端鉴权时：
///   incoming request → (project, httpMethod, path) → 查此表 → permissionCode:action → permset SISMEMBER
///
/// 路由匹配规则：
///   routePattern 使用 ASP.NET Core route template 语法（如 /api/users/{id}）。
///   匹配算法使用 RouteTemplate.TryParse + TemplateMatcher，不允许手写正则或字符串前缀匹配。
///   同一 project + httpMethod + routePattern 只能映射一个 permissionCode + action。
/// </summary>
public sealed class RbacApiPermissionMap
{
    /// <summary>内部数据库主键（Guid）。</summary>
    public Guid Id { get; private set; }

    /// <summary>所属项目。</summary>
    public ProjectCode Project { get; private set; }

    /// <summary>HTTP 方法（大写）：GET / POST / PUT / DELETE / PATCH。</summary>
    public string HttpMethod { get; private set; }

    /// <summary>
    /// 规范化路由模板。使用 ASP.NET Core route template 语法。
    /// 示例：/api/users/{id}、/api/{project}/menus。
    /// </summary>
    public string RoutePattern { get; private set; }

    /// <summary>
    /// 对应的权限码。服务端鉴权的判断依据。
    /// 示例：api:system.user.create。
    /// </summary>
    public PermissionCode PermissionCode { get; private set; }

    /// <summary>
    /// 对应的操作类型。
    /// 允许值：read / create / update / delete / execute / access。
    /// </summary>
    public string Action { get; private set; }

    /// <summary>映射状态。Disabled 时该路由按 allowlist 以外的规则处理（默认拒绝）。</summary>
    public ApiMapStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private RbacApiPermissionMap() { }

    /// <summary>创建新的 API 权限映射。</summary>
    public static RbacApiPermissionMap Create(
        Guid id,
        ProjectCode project,
        string httpMethod,
        string routePattern,
        PermissionCode permissionCode,
        string action)
    {
        ValidateHttpMethod(httpMethod);
        ValidateAction(action);

        if (string.IsNullOrWhiteSpace(routePattern))
            throw new ArgumentException("RoutePattern cannot be empty.", nameof(routePattern));

        return new RbacApiPermissionMap
        {
            Id = id,
            Project = project,
            HttpMethod = httpMethod.ToUpperInvariant(),
            RoutePattern = routePattern.Trim(),
            PermissionCode = permissionCode,
            Action = action.ToLowerInvariant(),
            Status = ApiMapStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>更新权限码和操作类型。触发后调用方必须通过 Outbox 发布 ApiMapChanged 事件。</summary>
    public void UpdatePermission(PermissionCode permissionCode, string action)
    {
        ValidateAction(action);
        PermissionCode = permissionCode;
        Action = action.ToLowerInvariant();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Disable() { Status = ApiMapStatus.Disabled; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Enable() { Status = ApiMapStatus.Active; UpdatedAt = DateTimeOffset.UtcNow; }

    // ── 验证 ──────────────────────────────────────────────────────

    private static readonly HashSet<string> ValidHttpMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "POST", "PUT", "DELETE", "PATCH" };

    private static readonly HashSet<string> ValidActions =
        new(StringComparer.OrdinalIgnoreCase) { "read", "create", "update", "delete", "execute", "access" };

    private static void ValidateHttpMethod(string method)
    {
        if (!ValidHttpMethods.Contains(method))
            throw new ArgumentException($"Invalid HTTP method '{method}'. Allowed: {string.Join(", ", ValidHttpMethods)}.", nameof(method));
    }

    private static void ValidateAction(string action)
    {
        if (!ValidActions.Contains(action))
            throw new ArgumentException($"Invalid action '{action}'. Allowed: {string.Join(", ", ValidActions)}.", nameof(action));
    }
}
