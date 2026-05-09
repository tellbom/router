using Rbac.Domain.ValueObjects;

namespace Rbac.Domain.Users;

/// <summary>
/// 管理员账号状态。
/// </summary>
public enum AdminStatus
{
    /// <summary>正常，可登录和鉴权。</summary>
    Active,
    /// <summary>已禁用，所有请求应被 403 拒绝，缓存应主动清除。</summary>
    Disabled,
}

/// <summary>
/// 管理员聚合根。
///
/// 内部标识：<see cref="Id"/>（Guid，数据库主键，不对外暴露）。
/// 外部兼容：<see cref="DxEId"/>（string，对前端 API 返回，不作为权限判断依据）。
/// 权限判断依赖：<see cref="Userid"/> + permissionCode / ruleCode，不依赖 DxEId。
/// </summary>
public sealed class RbacAdministrator
{
    /// <summary>内部数据库主键（Guid）。不对前端暴露。</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// 前端兼容业务 ID。API 返回时必须为 string。
    /// 只用于前端编辑、删除、排序和迁移追踪，不作为权限判断依据。
    /// </summary>
    public DxEId DxEId { get; private set; }

    /// <summary>用户业务标识（来自公司门户/JWT）。权限判断的主体。</summary>
    public UserId Userid { get; private set; }

    /// <summary>显示名称。</summary>
    public string Username { get; private set; }

    /// <summary>账号状态。</summary>
    public AdminStatus Status { get; private set; }

    /// <summary>账号创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    // EF Core / Dapper 反序列化用无参构造（private）
    private RbacAdministrator() { }

    /// <summary>创建新管理员。DxEId 由 IRbacDxEIdGenerator 生成后传入。</summary>
    public static RbacAdministrator Create(
        Guid id,
        DxEId dxeId,
        UserId userid,
        string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username cannot be empty.", nameof(username));

        return new RbacAdministrator
        {
            Id = id,
            DxEId = dxeId,
            Userid = userid,
            Username = username.Trim(),
            Status = AdminStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>禁用账号。触发后调用方必须通过 Outbox 清除该用户的所有缓存。</summary>
    public void Disable()
    {
        Status = AdminStatus.Disabled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>启用账号。</summary>
    public void Enable()
    {
        Status = AdminStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>更新显示名称。</summary>
    public void UpdateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username cannot be empty.", nameof(username));
        Username = username.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
