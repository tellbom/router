using Rbac.Domain.ValueObjects;

namespace Rbac.Domain.Projects;

/// <summary>
/// 用户-Project 授权聚合根。
///
/// 记录 userid 被授权访问哪些 project，以及在各 project 下是否为超级管理员。
/// super 必须是 project 级别，不允许全局 super 绕过所有 project。
///
/// 变更（授权新增/移除、super 变更）必须：
/// 1. 写 MySQL 真相表（此聚合）。
/// 2. 通过 Outbox 触发 ProjectGrantChanged 事件。
/// 3. 事件处理器主动删除对应用户的 snapshot 和 permset（高风险场景）。
/// </summary>
public sealed class RbacProjectGrant
{
    /// <summary>内部数据库主键（Guid）。</summary>
    public Guid Id { get; private set; }

    /// <summary>被授权的用户 ID。</summary>
    public UserId Userid { get; private set; }

    /// <summary>被授权访问的项目标识。</summary>
    public ProjectCode Project { get; private set; }

    /// <summary>
    /// 该用户在此 project 下是否为超级管理员。
    /// super 用户跳过 permset 判断，但仍写审计日志。
    /// 禁止实现全局 super（跨所有 project）。
    /// </summary>
    public bool IsSuper { get; private set; }

    /// <summary>授权创建时间（UTC）。</summary>
    public DateTimeOffset GrantedAt { get; private set; }

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>授权操作人 userid。</summary>
    public string GrantedBy { get; private set; }

    private RbacProjectGrant() { }

    /// <summary>创建新的 project 授权记录。</summary>
    public static RbacProjectGrant Create(
        Guid id,
        UserId userid,
        ProjectCode project,
        string grantedBy,
        bool isSuper = false)
    {
        if (string.IsNullOrWhiteSpace(grantedBy))
            throw new ArgumentException("GrantedBy cannot be empty.", nameof(grantedBy));

        return new RbacProjectGrant
        {
            Id = id,
            Userid = userid,
            Project = project,
            IsSuper = isSuper,
            GrantedBy = grantedBy,
            GrantedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// 授予 super 权限。
    /// 调用方必须通过 Outbox 触发 ProjectGrantChanged 事件并主动清除用户缓存。
    /// </summary>
    public void GrantSuper()
    {
        IsSuper = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 撤销 super 权限。
    /// 调用方必须通过 Outbox 触发 ProjectGrantChanged 事件并主动清除用户缓存。
    /// </summary>
    public void RevokeSuper()
    {
        IsSuper = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
