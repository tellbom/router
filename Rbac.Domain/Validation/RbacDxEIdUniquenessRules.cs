namespace Rbac.Domain.Validation;

/// <summary>
/// DxE_id 唯一性验证规则。
///
/// 唯一范围优先级：
/// 1. 推荐全局唯一（跨所有 entityType 和 project）。
/// 2. 历史数据无法全局唯一时，最低要求为 project + entityType + DxE_id 联合唯一。
///
/// 禁止：使用 DxE_id 作为权限判断依据（见 RbacIdentityValidationRules）。
/// </summary>
public static class RbacDxEIdUniquenessRules
{
    /// <summary>支持的 entityType 常量。</summary>
    public static class EntityTypes
    {
        public const string Administrator = "administrator";
        public const string Group = "group";
        public const string Rule = "rule";
        public const string ProjectGrant = "project_grant";
        public const string ApiPermissionMap = "api_permission_map";
    }

    /// <summary>
    /// 验证 DxE_id 的格式是否合法（不做唯一性数据库查询，只验证格式）。
    /// </summary>
    public static ValidationResult ValidateFormat(string? dxeId)
    {
        if (string.IsNullOrWhiteSpace(dxeId))
            return ValidationResult.Fail("DxEId cannot be empty.");
        if (dxeId.Length > 64)
            return ValidationResult.Fail($"DxEId exceeds max length 64.");
        if (dxeId.Contains(' ') || dxeId.Contains('\t'))
            return ValidationResult.Fail("DxEId cannot contain whitespace.");
        return ValidationResult.Ok();
    }

    /// <summary>
    /// 构建全局唯一性查询 key（用于幂等检查）。
    /// </summary>
    public static string BuildGlobalKey(string dxeId) => dxeId;

    /// <summary>
    /// 构建 project + entityType 范围的唯一性查询 key（历史数据兼容模式）。
    /// </summary>
    public static string BuildScopedKey(string project, string entityType, string dxeId) =>
        $"{project}:{entityType}:{dxeId}";

    /// <summary>
    /// 验证 entityType 是否为已知类型。
    /// </summary>
    public static ValidationResult ValidateEntityType(string? entityType)
    {
        var known = new[]
        {
            EntityTypes.Administrator, EntityTypes.Group,
            EntityTypes.Rule, EntityTypes.ProjectGrant, EntityTypes.ApiPermissionMap,
        };
        if (string.IsNullOrWhiteSpace(entityType) || !known.Contains(entityType))
            return ValidationResult.Fail(
                $"Unknown entityType '{entityType}'. Known: {string.Join(", ", known)}.");
        return ValidationResult.Ok();
    }
}
