namespace Rbac.Infrastructure.Redis;

/// <summary>
/// Redis key 合法性守卫。
///
/// 防止以下违规 key 模式（设计文档 §11 反模式）：
/// - 单 key 存储所有用户权限（如 rbac:all-user-permissions）。
/// - key 不含 project 或 userid（可能导致跨 project 数据混用）。
/// - permset key 通过 FusionCache 访问（应直接走 IDatabase）。
///
/// 在 key 写入前调用 AssertKeyIsValid，违规时抛出 InvalidOperationException。
/// 用于 CI 检查或运行时断言，不用于热路径。
/// </summary>
public static class RbacRedisKeyGuard
{
    // 禁止的 key 模式（精确匹配或前缀）
    private static readonly string[] ForbiddenPrefixes =
    {
        "rbac:all-",
        "rbac:global-",
        "rbac:everyone-",
    };

    // 要求必须包含 project 的 key 前缀
    private static readonly string[] RequireProjectPrefixes =
    {
        "rbac:snapshot:",
        "rbac:menus:",
        "rbac:permset:",
        "rbac:version:",
        "rbac:api-map:",
        "rbac:menu-tree:",
        "rbac:policy-version:",
        "rbac:project-users:",
        "rbac:casbin:",
    };

    /// <summary>
    /// 断言 key 符合分散存储规范。
    /// 违规时抛出 <see cref="InvalidOperationException"/>。
    /// </summary>
    public static void AssertKeyIsValid(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Redis key cannot be empty.");

        // 检查禁止前缀
        foreach (var forbidden in ForbiddenPrefixes)
        {
            if (key.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Redis key '{key}' matches forbidden pattern '{forbidden}'. " +
                    "Keys must not aggregate all users or permissions into a single key. " +
                    "See RBAC design doc §11 anti-patterns.");
        }

        // 检查需要 project 分隔的 key 是否含有 project
        foreach (var prefix in RequireProjectPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // key 格式为 prefix{project}:... ，检查 project 非空
                var remainder = key[prefix.Length..];
                if (string.IsNullOrWhiteSpace(remainder) || remainder.StartsWith(':'))
                    throw new InvalidOperationException(
                        $"Redis key '{key}' must include a non-empty project segment after '{prefix}'.");
                break;
            }
        }
    }

    /// <summary>
    /// 验证 key 是否合法（不抛出，返回结果）。
    /// </summary>
    public static bool IsValid(string key, out string? violation)
    {
        try
        {
            AssertKeyIsValid(key);
            violation = null;
            return true;
        }
        catch (Exception ex)
        {
            violation = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 验证 permset key 格式是否包含 project 和 userid。
    /// permset key 格式必须为：rbac:permset:{project}:{userid}
    /// </summary>
    public static void AssertPermsetKeyHasUserid(string key)
    {
        const string prefix = "rbac:permset:";
        if (!key.StartsWith(prefix))
            throw new InvalidOperationException($"Not a permset key: '{key}'.");

        var parts = key[prefix.Length..].Split(':');
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            throw new InvalidOperationException(
                $"Permset key '{key}' must contain both project and userid segments. " +
                "Format: rbac:permset:{{project}}:{{userid}}");
    }
}
