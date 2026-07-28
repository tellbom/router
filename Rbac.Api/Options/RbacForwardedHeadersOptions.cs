using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace Rbac.Api.Options;

/// <summary>
/// Trusted reverse-proxy settings used to resolve the original client IP.
/// Only explicitly configured proxies or networks are trusted in addition to
/// ASP.NET Core's loopback defaults.
/// </summary>
public sealed class RbacForwardedHeadersOptions
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Maximum number of trusted proxy hops consumed from the right-hand side
    /// of X-Forwarded-For. A direct Traefik-to-API deployment should use 1.
    /// </summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>Exact trusted proxy IP addresses.</summary>
    public IList<string> KnownProxies { get; set; } = new List<string>();

    /// <summary>Trusted proxy networks in CIDR notation.</summary>
    public IList<string> KnownNetworks { get; set; } = new List<string>();
}

internal static class RbacForwardedHeadersConfiguration
{
    public static void Apply(
        ForwardedHeadersOptions target,
        IConfiguration configuration)
    {
        var configured = configuration
            .GetSection(RbacForwardedHeadersOptions.SectionName)
            .Get<RbacForwardedHeadersOptions>() ?? new RbacForwardedHeadersOptions();

        if (configured.ForwardLimit <= 0)
        {
            throw new InvalidOperationException(
                "ForwardedHeaders:ForwardLimit must be greater than zero.");
        }

        target.ForwardedHeaders =
            Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
            | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        target.ForwardLimit = configured.ForwardLimit;

        foreach (var value in configured.KnownProxies)
        {
            if (!IPAddress.TryParse(value, out var address))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownProxies contains invalid IP address '{value}'.");
            }

            target.KnownProxies.Add(address);
        }

        foreach (var value in configured.KnownNetworks)
        {
            target.KnownNetworks.Add(ParseNetwork(value));
        }
    }

    private static IPNetwork ParseNetwork(string value)
    {
        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var prefix)
            || !int.TryParse(parts[1], out var prefixLength))
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders:KnownNetworks contains invalid CIDR '{value}'.");
        }

        var maxPrefixLength = prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? 32
            : 128;
        if (prefixLength < 0 || prefixLength > maxPrefixLength)
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders:KnownNetworks contains invalid CIDR '{value}'.");
        }

        return new IPNetwork(prefix, prefixLength);
    }
}
