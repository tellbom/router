using Casbin;
using Casbin.Model;
using Microsoft.Extensions.Logging;

namespace Rbac.Infrastructure.Casbin;

/// <summary>
/// NetCasbin model.conf 加载器。
///
/// model.conf 使用 RBAC with domains 模型（g = _, _, _）：
/// - sub: userid 或 groupCode
/// - dom: project
/// - obj: permissionCode（不使用 DxE_id）
/// - act: read / create / update / delete / execute / access
///
/// Enforcer 生命周期由 CasbinEnforcerProvider 管理（不可变引用 + 原子替换），
/// 本类只负责从内嵌字符串加载 Model，不持有 Enforcer 实例。
/// </summary>
public sealed class RbacCasbinModelProvider
{
    private readonly ILogger<RbacCasbinModelProvider> _logger;

    /// <summary>
    /// RBAC with domains model.conf 内容。
    /// g = _, _, _ 表示三元组（用户/组, 组/角色, domain）。
    /// </summary>
    private const string ModelConf = @"
[request_definition]
r = sub, dom, obj, act

[policy_definition]
p = sub, dom, obj, act

[role_definition]
g = _, _, _

[policy_effect]
e = some(where (p.eft == allow))

[matchers]
m = g(r.sub, p.sub, r.dom) && r.dom == p.dom && r.obj == p.obj && r.act == p.act
";

    public RbacCasbinModelProvider(ILogger<RbacCasbinModelProvider> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 加载并返回 RBAC with domains Model 实例。
    /// 每次调用返回新实例，由 CasbinEnforcerFactory 组装 Enforcer。
    /// </summary>
    public IModel LoadModel()
    {
        _logger.LogDebug("Loading Casbin RBAC with domains model.");
        var model = DefaultModel.CreateFromText(ModelConf);
        return model;
    }

    /// <summary>
    /// 构建内存 policy adapter 并加载 g / p rules，返回可用的 Enforcer。
    /// policy 数据必须来自 MySQL 真相库，不允许从 Redis 或 ES 反向加载。
    /// </summary>
    public Enforcer BuildEnforcer(
        IReadOnlyList<(string Userid, string GroupCode, string Project)> groupingPolicies,
        IReadOnlyList<(string GroupCode, string Project, string PermissionCode, string Action)> permissionPolicies)
    {
        var model = LoadModel();
        var enforcer = new Enforcer(model);

        // 加载 g policy（用户-组关系）
        foreach (var (userid, groupCode, project) in groupingPolicies)
        {
            enforcer.AddGroupingPolicy(userid, groupCode, project);
        }

        // 加载 p policy（组-权限码-action）
        foreach (var (groupCode, project, permissionCode, action) in permissionPolicies)
        {
            enforcer.AddPolicy(groupCode, project, permissionCode, action);
        }

        _logger.LogDebug(
            "Casbin Enforcer built. GroupingPolicies={G} PermissionPolicies={P}",
            groupingPolicies.Count, permissionPolicies.Count);

        return enforcer;
    }
}
