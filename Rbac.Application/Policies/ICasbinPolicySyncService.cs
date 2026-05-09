using Rbac.Domain.ValueObjects;

namespace Rbac.Application.Policies;

/// <summary>
/// Casbin policy 同步服务契约。
/// 触发从 MySQL 真相表重新加载 policy 并替换 Enforcer 实例。
///
/// 调用方：
/// - CasbinPolicyVersionWatcher（检测 version 变化时后台触发）。
/// - RbacCasbinOutboxProcessor（消费 PolicyChanged 事件时触发）。
///
/// 实现要求（见设计文档 §6.9）：
/// - 不在共享 Enforcer 实例上直接 AddPolicy/RemovePolicy。
/// - 后台创建新 Enforcer → 加载成功后原子替换引用 → 失败时保留旧引用。
/// - reload 不阻塞正在进行的 Enforce 请求。
/// </summary>
public interface ICasbinPolicySyncService
{
    /// <summary>
    /// 触发指定 project 的 Casbin policy reload。
    /// 从 MySQL 真相表重新加载 g / p policy，原子替换 Enforcer 引用。
    /// </summary>
    Task SyncAsync(ProjectCode project, CancellationToken ct = default);
}
