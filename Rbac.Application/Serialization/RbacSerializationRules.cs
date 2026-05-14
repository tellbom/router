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
    /// </summary>
    public static readonly JsonSerializerOptions ApiResponseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
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
    };
}
