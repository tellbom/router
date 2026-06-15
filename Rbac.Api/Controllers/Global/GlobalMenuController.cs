using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Global;
using Rbac.Application.Management;
using Rbac.Application.Mapping;
using Rbac.Application.Repositories;
using Rbac.Application.Search;
using Rbac.Application.Security;
using Rbac.Domain.Rules;
using Rbac.Domain.ValueObjects;

namespace Rbac.Api.Controllers.Global;

/// <summary>
/// 跨 project 规则管理接口。
/// 调用方必须携带 X-Project: __global__ header。
///
/// GA2 仅实现读接口；规则写操作通过现有 RuleController（指定目标 project）完成。
/// </summary>
[ApiController]
[Route("api/global/menu")]
public sealed class GlobalMenuController : ControllerBase
{
    private readonly ICurrentRbacContextAccessor _ctx;
    private readonly IRbacManagementSearchService _search;
    private readonly IRbacManagementWriteService _write;
    private readonly RbacManagementWriteGuard _guard;
    private readonly IRuleRepository _ruleRepo;

    public GlobalMenuController(
        ICurrentRbacContextAccessor ctx,
        IRbacManagementSearchService search,
        IRbacManagementWriteService write,
        RbacManagementWriteGuard guard,
        IRuleRepository ruleRepo)
    {
        _ctx      = ctx;
        _search   = search;
        _write    = write;
        _guard    = guard;
        _ruleRepo = ruleRepo;
    }

    /// <summary>
    /// GET /api/global/menu/list — 跨项目规则搜索。
    /// query.Project 来自调用方，null 时搜索所有项目。
    /// 权限码：rbac.global.menu.manage : access
    /// </summary>
    [HttpGet("list")]
    public async Task<ApiResponse<PagedData<RuleSearchResult>>> List(
        [FromQuery] RuleSearchQuery query, CancellationToken ct)
    {
        var data = await _search.SearchRulesAsync(query, ct);
        return ApiResponse<PagedData<RuleSearchResult>>.Ok(data);
    }

    [HttpPost]
    public async Task<ApiResponse<object>> Create(
        [FromBody] GlobalCreateRuleRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(req.TargetProject)) return Fail(40001, "targetProject 涓嶈兘涓虹┖");
        if (RbacGlobalConstants.IsReservedProject(req.TargetProject)) return Fail(40009, "涓嶅厑璁告搷浣滀繚鐣欑郴缁?project");
        if (string.IsNullOrWhiteSpace(req.RuleCode)) return Fail(40001, "ruleCode 涓嶈兘涓虹┖");
        if (string.IsNullOrWhiteSpace(req.PermissionCode)) return Fail(40001, "permissionCode 涓嶈兘涓虹┖");
        if (string.IsNullOrWhiteSpace(req.Title)) return Fail(40001, "title 涓嶈兘涓虹┖");
        if (!RbacCompatibilityMappers.TryParseRuleType(req.Type, out var parsedType))
            return Fail(40001, $"鏃犳晥鐨?type: {req.Type}");
        if (parsedType == RuleType.Button && string.IsNullOrWhiteSpace(req.ParentRuleCode))
            return Fail(40001, "Button 绫诲瀷蹇呴』鎸囧畾 parentRuleCode");

        var rule = CreateRule(req);

        await _write.SaveRuleAsync(
            rule,
            changeKind: "Created",
            affectedPermissionCodes: new[] { rule.PermissionCode.Value },
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(new { ruleCode = rule.RuleCode.Value, weigh = rule.Weigh });
    }

    [HttpPut("{ruleCode}")]
    public async Task<ApiResponse<object>> Update(
        string ruleCode, [FromBody] GlobalUpdateRuleRequest req, CancellationToken ct)
    {
        ruleCode = DecodeRouteRuleCode(ruleCode);
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(req.TargetProject)) return Fail(40001, "targetProject 涓嶈兘涓虹┖");
        if (RbacGlobalConstants.IsReservedProject(req.TargetProject)) return Fail(40009, "涓嶅厑璁告搷浣滀繚鐣欑郴缁?project");

        var rule = await _guard.LoadRuleByCodeAsync(ruleCode, req.TargetProject, ct);
        if (rule is null) return Fail(40400, "瑙勫垯涓嶅瓨鍦?");

        var oldPermCode = rule.PermissionCode.Value;

        MenuType? menuType = null;
        if (req.MenuType is not null &&
            RbacCompatibilityMappers.TryParseMenuType(req.MenuType, out var mt))
            menuType = mt;

        RuleStatus? status = null;
        if (req.Status is not null &&
            Enum.TryParse<RuleStatus>(req.Status, ignoreCase: true, out var rs))
            status = rs;

        RuleCode? parentRuleCode = req.ParentRuleCode is not null
            ? (string.IsNullOrWhiteSpace(req.ParentRuleCode) ? null : new RuleCode(req.ParentRuleCode))
            : null;
        PermissionCode? permissionCode = req.PermissionCode is not null
            ? new PermissionCode(req.PermissionCode)
            : null;

        rule.UpdateMenuMeta(
            title: req.Title,
            name: req.Name,
            path: req.Path,
            icon: req.Icon,
            parentRuleCode: parentRuleCode,
            menuType: menuType,
            url: req.Url,
            component: req.Component,
            extend: req.Extend,
            remark: req.Remark,
            keepalive: req.Keepalive,
            weigh: req.Weigh,
            status: status,
            permissionCode: permissionCode,
            parentRuleCodeSpecified: req.ParentRuleCode is not null);

        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { oldPermCode, rule.PermissionCode.Value };

        await _write.SaveRuleAsync(
            rule,
            changeKind: "Updated",
            affectedPermissionCodes: affected.ToList(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    [HttpDelete("{ruleCode}")]
    public async Task<ApiResponse<object>> Delete(
        string ruleCode, [FromQuery] string targetProject, CancellationToken ct)
    {
        ruleCode = DecodeRouteRuleCode(ruleCode);
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(targetProject)) return Fail(40001, "targetProject 涓嶈兘涓虹┖");
        if (RbacGlobalConstants.IsReservedProject(targetProject)) return Fail(40009, "涓嶅厑璁告搷浣滀繚鐣欑郴缁?project");

        var rule = await _guard.LoadRuleByCodeAsync(ruleCode, targetProject, ct);
        if (rule is null) return Fail(40400, "瑙勫垯涓嶅瓨鍦?");

        var children = await _ruleRepo.FindChildrenByParentRuleCodeAsync(
            rule.RuleCode, new ProjectCode(targetProject), ct);
        if (children.Count > 0)
            return Fail(40009, $"璇峰厛鍒犻櫎鎴栬縼绉昏瑙勫垯涓嬬殑 {children.Count} 涓瓙瑙勫垯");

        await _write.DeleteRuleAsync(
            rule,
            affectedPermissionCodes: new[] { rule.PermissionCode.Value },
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    private CurrentRbacContext RequireContext() =>
        _ctx.Context ?? throw new InvalidOperationException("RbacContext missing");

    private static RbacRule CreateRule(GlobalCreateRuleRequest req)
    {
        if (!RbacCompatibilityMappers.TryParseRuleType(req.Type, out var ruleType))
            throw new ArgumentException($"Invalid type: {req.Type}", nameof(req.Type));

        if (ruleType == RuleType.Button)
        {
            if (string.IsNullOrWhiteSpace(req.ParentRuleCode))
                throw new ArgumentException("Button requires parentRuleCode.", nameof(req.ParentRuleCode));

            return RbacRule.CreateButton(
                Guid.NewGuid(),
                new ProjectCode(req.TargetProject),
                new RuleCode(req.RuleCode),
                new PermissionCode(req.PermissionCode),
                req.Title,
                req.Name ?? req.RuleCode,
                new RuleCode(req.ParentRuleCode),
                icon: req.Icon,
                remark: req.Remark,
                weigh: req.Weigh);
        }

        MenuType? menuType = null;
        if (!string.IsNullOrWhiteSpace(req.MenuType) &&
            RbacCompatibilityMappers.TryParseMenuType(req.MenuType, out var mt))
            menuType = mt;

        return RbacRule.CreateMenu(
            Guid.NewGuid(),
            new ProjectCode(req.TargetProject),
            new RuleCode(req.RuleCode),
            new PermissionCode(req.PermissionCode),
            ruleType,
            req.Title,
            req.Name ?? req.RuleCode,
            req.Path ?? string.Empty,
            parentRuleCode: string.IsNullOrWhiteSpace(req.ParentRuleCode)
                ? null
                : new RuleCode(req.ParentRuleCode),
            menuType: menuType,
            url: req.Url,
            component: req.Component,
            extend: req.Extend,
            icon: req.Icon,
            remark: req.Remark,
            keepalive: req.Keepalive,
            weigh: req.Weigh);
    }

    private static string DecodeRouteRuleCode(string ruleCode) =>
        Uri.UnescapeDataString(ruleCode);

    private static ApiResponse<object> Fail(int code, string msg) =>
        ApiResponse<object>.Fail(code, msg);
}

public sealed record GlobalCreateRuleRequest(
    string TargetProject,
    string RuleCode,
    string PermissionCode,
    string Title,
    string Type,
    string? Name = null,
    string? Path = null,
    string? Icon = null,
    string? ParentRuleCode = null,
    string? MenuType = null,
    string? Url = null,
    string? Component = null,
    string? Extend = null,
    string? Remark = null,
    bool Keepalive = false,
    int Weigh = 0);

public sealed record GlobalUpdateRuleRequest(
    string TargetProject,
    string? Title = null,
    string? Name = null,
    string? Path = null,
    string? Icon = null,
    string? ParentRuleCode = null,
    string? MenuType = null,
    string? Url = null,
    string? Component = null,
    string? Extend = null,
    string? Remark = null,
    bool? Keepalive = null,
    int? Weigh = null,
    string? Status = null,
    string? PermissionCode = null);
