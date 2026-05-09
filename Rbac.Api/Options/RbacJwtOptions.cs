namespace Rbac.Api.Options;

/// <summary>
/// JWT 配置选项。绑定到 appsettings.json "Jwt" 节。
/// 支持公司门户 JWT（自定义 claim）和 Keycloak 标准 JWT 两种场景。
/// </summary>
public sealed class RbacJwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>JWT 签发方（验证 iss claim）。</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>JWT 受众（验证 aud claim）。</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// OIDC / JWT Authority 地址。
    /// Keycloak 场景填写 realm 地址，例如 https://sso.company.com/realms/internal。
    /// 公司门户场景填写 token 签发服务地址。
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>开发环境可关闭 HTTPS 元数据要求，生产必须为 true。</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// 优先使用的 userid claim 名称。
    /// 公司门户场景通常为 "employee_id" 或 "uid"。
    /// Keycloak 标准场景为 "sub"。
    /// </summary>
    public string UseridClaim { get; set; } = "sub";

    /// <summary>
    /// 备选 userid claim 名称列表。
    /// 当 <see cref="UseridClaim"/> 对应的 claim 值为空时，按顺序依次尝试。
    /// 解决不同 JWT 颁发方 claim 名称不统一的兼容问题。
    /// </summary>
    public IList<string> FallbackUseridClaims { get; set; } = new List<string> { "employee_id", "uid", "preferred_username" };

    /// <summary>
    /// JWT 签名验证公钥（非 OIDC 场景使用）。
    /// 如果设置了 Authority，此字段忽略，使用 OIDC discovery 自动获取公钥。
    /// </summary>
    public string? SigningKeyBase64 { get; set; }
}
