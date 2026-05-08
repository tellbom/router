namespace Rbac.Application.Security;

/// <summary>
/// 当前请求的 RBAC 上下文。由 <see cref="IRbacProjectResolver"/> 构建，
/// 在中间件中写入后，业务 Service 只读此对象，禁止重新解析原始 project。
/// </summary>
public sealed class CurrentRbacContext
{
    /// <summary>已验证的用户 ID，来自 JWT claims。</summary>
    public string Userid { get; init; } = string.Empty;

    /// <summary>已验证的项目标识，来自请求 X-Project header（经过服务端授权校验）。</summary>
    public string Project { get; init; } = string.Empty;

    /// <summary>userid 是否已被授权访问该 project。false 时请求应被 403 拒绝。</summary>
    public bool IsProjectAuthorized { get; init; }

    /// <summary>userid 在该 project 下是否为超级管理员。</summary>
    public bool IsProjectSuper { get; init; }

    /// <summary>请求链路追踪 ID，用于审计日志关联。</summary>
    public string TraceId { get; init; } = string.Empty;

    /// <summary>当前已知的 Casbin policy 版本号，用于 permset 版本校验。</summary>
    public long PolicyVersion { get; init; }

    /// <summary>前端原始传入的 project 值（校验前），用于审计日志记录伪造行为。</summary>
    public string RequestedProject { get; init; } = string.Empty;
}

/// <summary>
/// 在请求生命周期内访问 <see cref="CurrentRbacContext"/> 的接口。
/// 由中间件在管道早期写入，业务 Service 通过此接口读取，不直接依赖 IHttpContextAccessor。
/// </summary>
public interface ICurrentRbacContextAccessor
{
    /// <summary>
    /// 获取当前请求的 RBAC 上下文。
    /// 如果中间件尚未写入（如匿名路由），返回 null。
    /// </summary>
    CurrentRbacContext? Context { get; }

    /// <summary>设置当前请求的上下文（仅由中间件调用）。</summary>
    void Set(CurrentRbacContext context);
}
