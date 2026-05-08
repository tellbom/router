using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rbac.Application.Serialization;

/// <summary>
/// 全局序列化规则。
/// 核心约束：DxE_id 类型必须序列化为 JSON string，不允许序列化为 number。
/// 所有对外 API 响应和 ES 文档写入必须使用此配置。
/// </summary>
public static class RbacSerializationRules
{
    /// <summary>
    /// 标准 JSON 序列化选项（对外 API 响应）。
    /// - camelCase 属性名（与前端约定一致）。
    /// - 忽略 null 字段（减少响应体积）。
    /// - 不允许 long 类型直接序列化（强制通过 DxEId string 包装输出）。
    /// </summary>
    public static readonly JsonSerializerOptions ApiResponseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new LongToStringConverter() },
    };

    /// <summary>
    /// 内部序列化选项（Redis snapshot、Outbox payload）。
    /// - 保留 null 字段（便于反序列化完整性校验）。
    /// - 同样强制 long 转 string。
    /// </summary>
    public static readonly JsonSerializerOptions InternalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
        Converters = { new LongToStringConverter() },
    };
}

/// <summary>
/// 将 long 强制序列化为 JSON string，防止 JavaScript 大整数精度丢失。
/// 适用于雪花 ID 等大数值字段（DxE_id 底层可能为雪花 long）。
/// </summary>
public sealed class LongToStringConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // 接受 string 或 number 输入（兼容旧数据）
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            return long.TryParse(s, out var v) ? v : 0L;
        }
        return reader.GetInt64();
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        // 始终输出为 JSON string
        writer.WriteStringValue(value.ToString());
    }
}

/// <summary>
/// 将 nullable long 强制序列化为 JSON string。
/// </summary>
public sealed class NullableLongToStringConverter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            return string.IsNullOrEmpty(s) ? null : long.TryParse(s, out var v) ? v : null;
        }
        return reader.GetInt64();
    }

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value.ToString());
    }
}
