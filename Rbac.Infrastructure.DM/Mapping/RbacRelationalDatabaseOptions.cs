using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rbac.Infrastructure.DM.Mapping;

public static class RbacRelationalDatabaseOptions
{
    public const string DmProvider = "DM";
    public const string MySqlProvider = "MySql";

    public static void UseRbacRelationalDatabase(
        this DbContextOptionsBuilder options,
        string? provider,
        string connectionString)
    {
        var normalizedProvider = NormalizeProvider(provider);
        if (normalizedProvider == DmProvider)
        {
            options.UseDm(connectionString);
            return;
        }

        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
    }

    public static string NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return DmProvider;

        return provider.Trim().ToLowerInvariant() switch
        {
            "dm" or "dameng" or "damengdb" => DmProvider,
            "mysql" => MySqlProvider,
            _ => DmProvider,
        };
    }

    public static string OutboxPayloadColumnType(string? providerName)
    {
        if (!string.IsNullOrWhiteSpace(providerName)
            && providerName.Contains("DM", StringComparison.OrdinalIgnoreCase))
        {
            return "CLOB";
        }

        return "longtext";
    }

    public static PropertyBuilder<DateTimeOffset> HasUtcDateTimeOffsetConversion(
        this PropertyBuilder<DateTimeOffset> property)
    {
        return property.HasConversion(
            v => v.UtcDateTime,
            v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
    }

    public static PropertyBuilder<DateTimeOffset?> HasUtcDateTimeOffsetConversion(
        this PropertyBuilder<DateTimeOffset?> property)
    {
        var converter = new ValueConverter<DateTimeOffset?, DateTime?>(
            v => v.HasValue ? v.Value.UtcDateTime : null,
            v => v.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
                : null);

        return property.HasConversion(converter);
    }
}
