using Microsoft.AspNetCore.Http;

namespace Rbac.Application.Security;

/// <summary>
/// 从 HTTP 请求中提取原始 project 值的契约。
/// 仅负责读取，不做校验。校验由 <see cref="IRbacProjectResolver"/> 负责。
/// 实现由 Rbac.Api 层注册（依赖 IHttpContextAccessor 和 RbacProjectOptions）。
/// </summary>
public interface IProjectRequestReader
{
    /// <summary>
    /// 从当前 HTTP 请求中读取前端传入的原始 project 值。
    /// 按 RbacProjectOptions.AllowedSources 顺序依次尝试，返回第一个非空值。
    /// </summary>
    /// <param name="context">当前 HTTP 请求上下文。</param>
    /// <returns>原始 project 字符串；未携带时返回 null。</returns>
    string? ReadProject(HttpContext context);
}
