namespace Rbac.Domain.ValueObjects;

/// <summary>
/// 项目/系统标识。对应请求中的 X-Project header 值。
/// 不允许为空，不允许包含空格。
/// </summary>
public sealed record ProjectCode
{
    public string Value { get; }

    public ProjectCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("ProjectCode cannot be empty.", nameof(value));
        Value = value.Trim();
    }

    public override string ToString() => Value;
    public static implicit operator string(ProjectCode code) => code.Value;
}

/// <summary>
/// 用户业务标识。来自 JWT claims（如 sub / employee_id）。
/// 不允许为空。
/// </summary>
public sealed record UserId
{
    public string Value { get; }

    public UserId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("UserId cannot be empty.", nameof(value));
        Value = value.Trim();
    }

    public override string ToString() => Value;
    public static implicit operator string(UserId userId) => userId.Value;
}

/// <summary>
/// 权限码。格式约定：{resourceType}:{scope}.{action}，例如 api:system.user.create。
/// 长期权限判断依赖此值，不使用 DxE_id。
/// </summary>
public sealed record PermissionCode
{
    public string Value { get; }

    public PermissionCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("PermissionCode cannot be empty.", nameof(value));
        Value = value.Trim();
    }

    public override string ToString() => Value;
    public static implicit operator string(PermissionCode code) => code.Value;
}

/// <summary>
/// 规则码。菜单规则和按钮规则的唯一标识，用于菜单树构建。
/// </summary>
public sealed record RuleCode
{
    public string Value { get; }

    public RuleCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("RuleCode cannot be empty.", nameof(value));
        Value = value.Trim();
    }

    public override string ToString() => Value;
    public static implicit operator string(RuleCode code) => code.Value;
}

/// <summary>
/// 权限组编码。唯一标识一个权限组，在 Casbin policy 中作为 role/group 使用。
/// </summary>
public sealed record GroupCode
{
    public string Value { get; }

    public GroupCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("GroupCode cannot be empty.", nameof(value));
        Value = value.Trim();
    }

    public override string ToString() => Value;
    public static implicit operator string(GroupCode code) => code.Value;
}
