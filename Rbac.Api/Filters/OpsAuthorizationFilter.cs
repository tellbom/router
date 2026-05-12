using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Rbac.Api.Options;
using System.Security.Cryptography;
using System.Text;

namespace Rbac.Api.Filters;

/// <summary>
/// Independent API-key guard for /ops endpoints.
/// </summary>
public sealed class OpsAuthorizationFilter : IAsyncActionFilter
{
    private const string OpsKeyHeader = "X-Ops-Key";

    private readonly RbacOpsOptions _options;
    private readonly ILogger<OpsAuthorizationFilter> _logger;

    public OpsAuthorizationFilter(
        IOptions<RbacOpsOptions> options,
        ILogger<OpsAuthorizationFilter> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Ops endpoint rejected: ApiKey is not configured.");
            context.Result = Forbidden("Ops API not configured.");
            return;
        }

        var incoming = context.HttpContext.Request.Headers[OpsKeyHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(incoming) || !FixedTimeEquals(_options.ApiKey, incoming))
        {
            _logger.LogWarning(
                "Ops endpoint rejected: invalid key from {IP}",
                context.HttpContext.Connection.RemoteIpAddress);
            context.Result = Forbidden("Invalid ops key.");
            return;
        }

        await next();
    }

    private static ObjectResult Forbidden(string message) =>
        new(new { code = 40300, msg = message })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
