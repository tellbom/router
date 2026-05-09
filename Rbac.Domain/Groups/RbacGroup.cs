using Rbac.Domain.ValueObjects;

namespace Rbac.Domain.Groups;

/// <summary>权限组状态。</summary>
public enum GroupStatus
{
    Active,
    Disabled,
}

/// <summary>
/// 权限组聚合根。
///
/// 权限组是权限分配的中间层：用户 → 组 → 规则/权限码。
/// 组的变更（规则增减、状态变更）必须通过 Outbox 递增 group version，
/// 触发该组下所有用户的 permset 懒失效。
/// </summary>
public sealed class RbacGroup
{
    /// <summary>内部数据库主键（Guid）。</summary>
    public Guid Id { get; private set; }

    /// <summary>前端兼容业务 ID（string）。不作为权限判断依据。</summary>
    public DxEId DxEId { get; private set; }

    /// <summary>权限组编码。唯一标识，用于 Casbin policy 中的 role/group。</summary>
    public GroupCode GroupCode { get; private set; }

    /// <summary>所属项目。权限组在 project 内隔离。</summary>
    public ProjectCode Project { get; private set; }

    /// <summary>权限组名称（显示用）。</summary>
    public string GroupName { get; private set; }

    /// <summary>父级权限组编码，支持层级组织。根组为 null。</summary>
    public GroupCode? ParentGroupCode { get; private set; }

    /// <summary>该组拥有的规则码集合（对应菜单/按钮规则）。</summary>
    public IReadOnlyList<RuleCode> RuleCodes { get; private set; }

    /// <summary>由规则码展开的权限码集合（冗余字段，加速 permset 构建）。</summary>
    public IReadOnlyList<PermissionCode> PermissionCodes { get; private set; }

    /// <summary>权限组状态。</summary>
    public GroupStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private RbacGroup() { }

    /// <summary>创建新权限组。</summary>
    public static RbacGroup Create(
        Guid id,
        DxEId dxeId,
        GroupCode groupCode,
        ProjectCode project,
        string groupName,
        GroupCode? parentGroupCode = null)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            throw new ArgumentException("GroupName cannot be empty.", nameof(groupName));

        return new RbacGroup
        {
            Id = id,
            DxEId = dxeId,
            GroupCode = groupCode,
            Project = project,
            GroupName = groupName.Trim(),
            ParentGroupCode = parentGroupCode,
            RuleCodes = Array.Empty<RuleCode>(),
            PermissionCodes = Array.Empty<PermissionCode>(),
            Status = GroupStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// 更新规则码和权限码。
    /// 调用方必须通过 Outbox 触发 GroupChanged 事件，以递增 group version 并懒失效 permset。
    /// </summary>
    public void UpdateRules(IReadOnlyList<RuleCode> ruleCodes, IReadOnlyList<PermissionCode> permissionCodes)
    {
        RuleCodes = ruleCodes ?? Array.Empty<RuleCode>();
        PermissionCodes = permissionCodes ?? Array.Empty<PermissionCode>();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>更新权限组名称。</summary>
    public void UpdateName(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
            throw new ArgumentException("GroupName cannot be empty.", nameof(groupName));
        GroupName = groupName.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Disable() { Status = GroupStatus.Disabled; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Enable() { Status = GroupStatus.Active; UpdatedAt = DateTimeOffset.UtcNow; }
}
