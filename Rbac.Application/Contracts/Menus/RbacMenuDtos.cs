using System.Text.Json.Serialization;

namespace Rbac.Application.Contracts.Menus;

/// <summary>
/// 前端菜单节点 DTO。
///
/// 对应前端 menus → authNode → auth() / v-auth 机制。
/// 字段完整性要求：前端从此结构构建菜单树、按钮权限节点，
/// 用于编辑、删除、排序等管理操作。
///
/// 约束：
/// - children 递归，前端从树结构生成 authNode。
/// - 按钮节点（type=button）的 children 为空数组。
/// - 前端 auth("name") / v-auth="name" 匹配 name 字段。
/// </summary>
public sealed class MenuNodeDto
{
    /// <summary>
    /// 前端兼容业务 ID。JSON 字段固定为 "id"，类型必须为 string。
    /// 用于前端 edit/delete/sort 操作，不作为权限判断依据。
    /// </summary>
    /// <summary>父节点规则编码，根节点为 "0" 或空字符串。</summary>
    [JsonPropertyName("pid")]
    public string Pid { get; init; } = "0";

    /// <summary>菜单/按钮显示标题（管理端和前端展示用）。</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// 前端路由 name，用于 auth("name") / v-auth="name" 匹配。
    /// 按钮节点此字段为权限标识（如 add / edit / del / sortable）。
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>前端路由 path。按钮节点为空字符串。</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("icon")]
    public string Icon { get; init; } = string.Empty;

    /// <summary>节点类型：menu_dir / menu / button。小写。</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// 菜单渲染类型：tab / link / iframe。
    /// menu 类型节点有效，button / menu_dir 节点为空字符串。
    /// </summary>
    [JsonPropertyName("menu_type")]
    public string MenuType { get; init; } = string.Empty;

    /// <summary>外链或 iframe URL。link / iframe 类型节点使用。</summary>
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    /// <summary>前端组件路径，例如 /src/views/system/user/index.vue。</summary>
    [JsonPropertyName("component")]
    public string Component { get; init; } = string.Empty;

    /// <summary>扩展行为标记（前端自定义扩展字段）。</summary>
    [JsonPropertyName("extend")]
    public string Extend { get; init; } = string.Empty;

    [JsonPropertyName("remark")]
    public string Remark { get; init; } = string.Empty;

    /// <summary>是否开启路由缓存（keep-alive）。</summary>
    [JsonPropertyName("keepalive")]
    public bool Keepalive { get; init; }

    /// <summary>
    /// 子节点列表（递归结构）。
    /// 按钮节点为空数组，前端通过遍历此结构生成 authNode。
    /// </summary>
    [JsonPropertyName("children")]
    public IReadOnlyList<MenuNodeDto> Children { get; init; } = Array.Empty<MenuNodeDto>();

    /// <summary>
    /// 权限码。服务端鉴权依据，不直接暴露给前端鉴权机制。
    /// 前端感知 name / ruleCode，不感知 permissionCode。
    /// </summary>
    [JsonPropertyName("permissionCode")]
    public string PermissionCode { get; init; } = string.Empty;

    /// <summary>
    /// 规则码。前端 authNode 匹配依据，用于菜单树构建。
    /// </summary>
    [JsonPropertyName("ruleCode")]
    public string RuleCode { get; init; } = string.Empty;
}
