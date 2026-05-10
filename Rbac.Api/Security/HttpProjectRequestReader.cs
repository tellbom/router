using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Rbac.Api.Options;
using Rbac.Application.Security;

namespace Rbac.Api.Security;

/// <summary>
/// PATCH-03: IProjectRequestReader 的 HTTP 实现。
///
/// 按 RbacProjectOptions.AllowedSources 顺序依次尝试，返回第一个非空 project 值。
/// 支持来源：Header（默认 X-Project）、Route、Query。
/// Body 来源（需要读取请求体）不在热路径实现范围，有需要可扩展。
/// </summary>
public sealed class HttpProjectRequestReader : IProjectRequestReader
{
    private readonly RbacProjectOptions _options;

    public HttpProjectRequestReader(IOptions<RbacProjectOptions> options)
        => _options = options.Value;

    public string? ReadProject(HttpContext context)
    {
        foreach (var source in _options.AllowedSources)
        {
            var val = source switch
            {
                ProjectSource.Header => context.Request.Headers[_options.HeaderName].FirstOrDefault(),
                ProjectSource.Route  => context.GetRouteValue(_options.RouteParamName)?.ToString(),
                ProjectSource.Query  => context.Request.Query[_options.QueryParamName].FirstOrDefault(),
                _                    => null,
            };

            if (!string.IsNullOrWhiteSpace(val)) return val;
        }

        return null;
    }
}
