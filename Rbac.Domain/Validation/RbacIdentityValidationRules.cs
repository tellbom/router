using Rbac.Domain.ValueObjects;

namespace Rbac.Domain.Validation;

/// <summary>
/// RBAC 身份标识验证规则。
///
/// 权限判断必须使用 PermissionCode / RuleCode。
/// </summary>
public static class RbacIdentityValidationRules
{
    // ── PermissionCode 验证 ───────────────────────────────────────

    /// <summary>
    /// 验证 PermissionCode 格式。
    /// 约定格式：{resourceType}:{scope}，例如 api:system.user.create / button:system.user.add。
    /// </summary>
    public static ValidationResult ValidatePermissionCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ValidationResult.Fail("PermissionCode cannot be empty.");

        if (raw.Length > 256)
            return ValidationResult.Fail($"PermissionCode exceeds max length 256: '{raw}'.");

        if (!raw.Contains(':'))
            return ValidationResult.Fail(
                $"PermissionCode '{raw}' is invalid. Expected format: '{{resourceType}}:{{scope}}'.");

        var parts = raw.Split(':', 2);
        var validResourceTypes = new[] { "api", "menu", "button", "menu_dir" };
        if (!validResourceTypes.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
            return ValidationResult.Fail(
                $"PermissionCode resourceType '{parts[0]}' is invalid. Allowed: {string.Join(", ", validResourceTypes)}.");

        return ValidationResult.Ok();
    }

    // ── RuleCode 验证 ─────────────────────────────────────────────

    /// <summary>验证 RuleCode 格式。必须为非空字符串，最大 128 字符。</summary>
    public static ValidationResult ValidateRuleCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ValidationResult.Fail("RuleCode cannot be empty.");

        if (raw.Length > 128)
            return ValidationResult.Fail($"RuleCode exceeds max length 128: '{raw}'.");

        return ValidationResult.Ok();
    }

    // ── Action 验证 ───────────────────────────────────────────────

    private static readonly HashSet<string> ValidActions =
        new(StringComparer.OrdinalIgnoreCase) { "read", "create", "update", "delete", "execute", "access" };

    /// <summary>验证 action 是否为允许的动作类型。</summary>
    public static ValidationResult ValidateAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return ValidationResult.Fail("Action cannot be empty.");

        if (!ValidActions.Contains(action))
            return ValidationResult.Fail(
                $"Action '{action}' is invalid. Allowed: {string.Join(", ", ValidActions)}.");

        return ValidationResult.Ok();
    }
}

/// <summary>验证结果值对象。</summary>
public sealed class ValidationResult
{
    public bool IsValid { get; }
    public string? Error { get; }

    private ValidationResult(bool isValid, string? error)
    {
        IsValid = isValid;
        Error = error;
    }

    public static ValidationResult Ok() => new(true, null);
    public static ValidationResult Fail(string error) => new(false, error);

    /// <summary>验证失败时抛出 ArgumentException。</summary>
    public void ThrowIfInvalid(string paramName)
    {
        if (!IsValid)
            throw new ArgumentException(Error, paramName);
    }
}
