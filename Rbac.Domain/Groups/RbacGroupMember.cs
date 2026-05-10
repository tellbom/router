using Rbac.Domain.ValueObjects;

namespace Rbac.Domain.Groups;

/// <summary>
/// 用户-权限组关联聚合根。
///
/// 存储 Casbin `g` policy 的 MySQL 真相：(userid, groupCode, project) 三元组。
/// 对应设计文档 §6.4 "用户-组关系 → 对应 Casbin g"。
///
/// 约束（tasks.md T093 MVP-Blocker）：
/// - 该实体是 g policy 的唯一真相来源。
/// - 禁止从 Redis permset 或 ES 反向推导用户所属组。
/// - 管理端授权写操作必须先写此表，再通过 Outbox 触发 Casbin reload。
///
/// 对应数据库表：rbac_group_member
/// </summary>
public sealed class RbacGroupMember
{
    /// <summary>内部主键（Guid）。</summary>
    public Guid Id { get; private set; }

    /// <summary>用户 ID（来自 JWT / 公司门户）。</summary>
    public UserId Userid { get; private set; } = new UserId("_");

    /// <summary>权限组编码。</summary>
    public GroupCode GroupCode { get; private set; } = new GroupCode("_");

    /// <summary>所属 project（权限组在 project 内隔离）。</summary>
    public ProjectCode Project { get; private set; } = new ProjectCode("_");

    /// <summary>授权操作人。</summary>
    public string GrantedBy { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // EF Core 反射实例化用
    private RbacGroupMember() { }

    /// <summary>创建新的用户-组关联记录。</summary>
    public static RbacGroupMember Create(
        Guid id,
        UserId userid,
        GroupCode groupCode,
        ProjectCode project,
        string grantedBy)
    {
        if (string.IsNullOrWhiteSpace(grantedBy))
            throw new ArgumentException("GrantedBy cannot be empty.", nameof(grantedBy));

        return new RbacGroupMember
        {
            Id        = id,
            Userid    = userid,
            GroupCode = groupCode,
            Project   = project,
            GrantedBy = grantedBy.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>更新操作人（重新授权场景）。</summary>
    public void UpdateGrantedBy(string grantedBy)
    {
        if (string.IsNullOrWhiteSpace(grantedBy))
            throw new ArgumentException("GrantedBy cannot be empty.", nameof(grantedBy));
        GrantedBy = grantedBy.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
