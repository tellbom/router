namespace Rbac.Application.Security;

/// <summary>
/// project 校验的唯一入口。负责：
/// 1. 从请求中解析 project 值。
/// 2. 校验 userid 是否被授权访问该 project。
/// 3. 构建并返回 <see cref="CurrentRbacContext"/>。
///
/// 业务 Service 禁止绕过此接口自行读取 project。
/// </summary>
public interface IRbacProjectResolver
{
    /// <summary>
    /// 解析并校验当前请求的 project，返回已验证的上下文。
    /// </summary>
    /// <param name="userid">已从 JWT 提取的用户 ID。</param>
    /// <param name="requestedProject">前端请求携带的原始 project 值。</param>
    /// <param name="traceId">请求链路 ID，写入审计日志。</param>
    /// <param name="cancellationToken"></param>
    /// <returns>
    /// 始终返回 <see cref="CurrentRbacContext"/>。
    /// 若 project 不存在或用户无授权，<see cref="CurrentRbacContext.IsProjectAuthorized"/> 为 false，
    /// 调用方负责据此返回 403。
    /// </returns>
    Task<CurrentRbacContext> ResolveAsync(
        string userid,
        string requestedProject,
        string traceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// project 校验结果枚举，供审计日志使用。
/// </summary>
public enum ProjectResolveResult
{
    /// <summary>project 合法且用户已授权。</summary>
    Authorized,

    /// <summary>请求未携带 project 参数。</summary>
    MissingProject,

    /// <summary>project 存在但用户未被授权访问。</summary>
    Unauthorized,

    /// <summary>project 值不存在于系统中。</summary>
    ProjectNotFound,

    /// <summary>用户账号已被禁用。</summary>
    UserDisabled,
}
