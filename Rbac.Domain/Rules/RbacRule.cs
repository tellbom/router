using Rbac.Domain.ValueObjects;

namespace Rbac.Domain.Rules;

/// <summary>规则节点类型。</summary>
public enum RuleType
{
    /// <summary>菜单目录（仅用于分组，不对应路由）。</summary>
    MenuDir,
    /// <summary>菜单项（对应一个前端路由页面）。</summary>
    Menu,
    /// <summary>按钮/操作权限节点（对应页面内的操作权限）。</summary>
    Button,
}

/// <summary>菜单渲染类型（仅 Menu 类型有效）。</summary>
public enum MenuType
{
    /// <summary>标签页形式打开。</summary>
    Tab,
    /// <summary>外部链接。</summary>
    Link,
    /// <summary>内嵌 iframe。</summary>
    Iframe,
}

/// <summary>规则状态。</summary>
public enum RuleStatus
{
    Active,
    Disabled,
}

/// <summary>
/// 规则聚合根。对应菜单规则和按钮规则。
///
/// 规则是权限体系的基础单元：
/// - 菜单规则（MenuDir / Menu）：构建前端 menus 树。
/// - 按钮规则（Button）：生成 authNode（add / edit / del / sortable 等）。
///
/// 每条规则对应一个 <see cref="PermissionCode"/>，服务端鉴权依赖 permissionCode，
/// 前端兼容依赖 <see cref="RuleCode"/> 和 <see cref="DxEId"/>（均为 string）。
/// </summary>
public sealed class RbacRule
{
    /// <summary>内部数据库主键（Guid）。</summary>
    public Guid Id { get; private set; }

    /// <summary>前端兼容业务 ID（string）。不作为权限判断依据。</summary>
    public DxEId DxEId { get; private set; } = new DxEId("0");

    /// <summary>所属项目。</summary>
    public ProjectCode Project { get; private set; } = new ProjectCode("_");

    /// <summary>规则码（唯一标识，用于菜单树构建和 authNode 匹配）。</summary>
    public RuleCode RuleCode { get; private set; } = new RuleCode("_");

    /// <summary>
    /// 权限码（服务端鉴权依据）。
    /// 格式：{resourceType}:{scope}，例如 menu:system.user / button:system.user.add。
    /// </summary>
    public PermissionCode PermissionCode { get; private set; } = new PermissionCode("_:_");

    /// <summary>父级规则码（根节点为 null）。</summary>
    public RuleCode? ParentRuleCode { get; private set; }

    /// <summary>规则节点类型：MenuDir / Menu / Button。</summary>
    public RuleType Type { get; private set; }

    // ── 菜单元数据（Menu / MenuDir 有效）─────────────────────────

    /// <summary>菜单显示标题。</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>前端路由 name，用于 auth() / v-auth 匹配。</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>前端路由 path。</summary>
    public string Path { get; private set; } = string.Empty;
    public string? Icon { get; private set; }

    /// <summary>菜单渲染类型（Tab / Link / Iframe），Button 节点为 null。</summary>
    public MenuType? MenuType { get; private set; }

    /// <summary>外链或 iframe URL。</summary>
    public string? Url { get; private set; }

    /// <summary>前端组件路径。</summary>
    public string? Component { get; private set; }

    /// <summary>扩展行为标记。</summary>
    public string? Extend { get; private set; }
    public string? Remark { get; private set; }

    /// <summary>是否开启路由缓存（keep-alive）。</summary>
    public bool Keepalive { get; private set; }

    /// <summary>排序权重，数值越小越靠前。</summary>
    public int Weigh { get; private set; }

    /// <summary>规则状态。</summary>
    public RuleStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private RbacRule() { }

    /// <summary>创建菜单目录或菜单规则。</summary>
    public static RbacRule CreateMenu(
        Guid id, DxEId dxeId, ProjectCode project,
        RuleCode ruleCode, PermissionCode permissionCode,
        RuleType type, string title, string name, string path,
        RuleCode? parentRuleCode = null,
        Rules.MenuType? menuType = null,
        string? url = null, string? component = null,
        string? extend = null, string? icon = null, string? remark = null,
        bool keepalive = false, int weigh = 0)
    {
        if (type == RuleType.Button)
            throw new ArgumentException("Use CreateButton for button rules.", nameof(type));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty.", nameof(title));

        return new RbacRule
        {
            Id = id, DxEId = dxeId, Project = project,
            RuleCode = ruleCode, PermissionCode = permissionCode,
            ParentRuleCode = parentRuleCode,
            Type = type, Title = title.Trim(), Name = name.Trim(), Path = path.Trim(),
            MenuType = menuType, Url = url, Component = component,
            Extend = extend, Icon = icon, Remark = remark,
            Keepalive = keepalive, Weigh = weigh,
            Status = RuleStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>创建按钮规则（button）。</summary>
    public static RbacRule CreateButton(
        Guid id, DxEId dxeId, ProjectCode project,
        RuleCode ruleCode, PermissionCode permissionCode,
        string title, string name, RuleCode parentRuleCode,
        string? icon = null, string? remark = null, int weigh = 0)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title cannot be empty.", nameof(title));

        return new RbacRule
        {
            Id = id, DxEId = dxeId, Project = project,
            RuleCode = ruleCode, PermissionCode = permissionCode,
            ParentRuleCode = parentRuleCode,
            Type = RuleType.Button, Title = title.Trim(), Name = name.Trim(),
            Path = string.Empty, Icon = icon, Remark = remark, Weigh = weigh,
            Status = RuleStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Disable() { Status = RuleStatus.Disabled; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Enable()  { Status = RuleStatus.Active;   UpdatedAt = DateTimeOffset.UtcNow; }

    public void UpdateWeigh(int weigh) { Weigh = weigh; UpdatedAt = DateTimeOffset.UtcNow; }

    /// <summary>
    /// 更新菜单/按钮规则的元数据字段。
    /// 仅更新非 null 参数，null 表示"不变"。
    /// permissionCode 变更后调用方必须通过 Outbox 触发 MenuChanged 事件。
    /// parentRuleCode 变更表示菜单层级调整，同样产生 MenuChanged。
    /// </summary>
    public void UpdateMenuMeta(
        string? title = null,
        string? name = null,
        string? path = null,
        string? icon = null,
        RuleCode? parentRuleCode = null,
        Rules.MenuType? menuType = null,
        string? url = null,
        string? component = null,
        string? extend = null,
        string? remark = null,
        bool? keepalive = null,
        int? weigh = null,
        RuleStatus? status = null,
        PermissionCode? permissionCode = null,
        bool parentRuleCodeSpecified = false)
    {
        if (title is not null)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Title cannot be empty.", nameof(title));
            Title = title.Trim();
        }

        if (name is not null)      Name          = name.Trim();
        if (path is not null)      Path          = path.Trim();
        if (icon is not null)      Icon          = icon.Trim();
        if (parentRuleCodeSpecified) ParentRuleCode = parentRuleCode;
        if (menuType is not null)  MenuType      = menuType;
        if (url is not null)       Url           = url;
        if (component is not null) Component     = component;
        if (extend is not null)    Extend        = extend;
        if (remark is not null)    Remark        = remark.Trim();
        if (keepalive is not null) Keepalive     = keepalive.Value;
        if (weigh is not null)     Weigh         = weigh.Value;
        if (permissionCode is not null) PermissionCode = permissionCode;

        if (status == RuleStatus.Disabled) Disable();
        else if (status == RuleStatus.Active) Enable();
        else UpdatedAt = DateTimeOffset.UtcNow; // 至少更新时间
    }
}
