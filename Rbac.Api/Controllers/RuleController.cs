using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Management;
using Rbac.Application.Menus;
using Rbac.Application.Search;
using Rbac.Application.Security;
using Rbac.Domain.Rules;
using Rbac.Domain.ValueObjects;

namespace Rbac.Api.Controllers;

/// <summary>
/// 菜单/按钮规则管理接口。
///
/// 读：ES 分页查询（列表）+ RbacProjectMenuTreeService（全量树）。
/// 写：WriteGuard 先加载 MySQL 真相，再写入 + Outbox。
/// project 来自 CurrentRbacContext。
/// </summary>
[ApiController]
[Route("api/rule")]
public sealed partial class RuleController : ControllerBase
{
    private readonly ICurrentRbacContextAccessor _ctx;
    private readonly IRbacManagementSearchService _search;
    private readonly IRbacManagementWriteService _write;
    private readonly RbacManagementWriteGuard _guard;
    private readonly RbacProjectMenuTreeService _menuTree;

    public RuleController(
        ICurrentRbacContextAccessor ctx,
        IRbacManagementSearchService search,
        IRbacManagementWriteService write,
        RbacManagementWriteGuard guard,
        RbacProjectMenuTreeService menuTree)
    {
        _ctx = ctx;
        _search = search;
        _write = write;
        _guard = guard;
        _menuTree = menuTree;
    }

    // ── 全量菜单树 ─────────────────────────────────────────────────

    /// <summary>GET /api/rule/tree — 获取 project 下全量菜单树（供管理端配置使用）。</summary>
    [HttpGet("tree")]
    public async Task<ApiResponse<object>> Tree(CancellationToken ct)
    {
        var project = RequireContext().Project;
        var menus = await _menuTree.GetProjectMenuTreeAsync(project, ct);
        return ApiResponse<object>.Ok(menus);
    }

    // ── 列表（ES）─────────────────────────────────────────────────

    /// <summary>GET /api/rule/list — ES 分页查询规则列表。</summary>
    [HttpGet("list")]
    public async Task<ApiResponse<PagedData<RuleSearchResult>>> List(
        [FromQuery] RuleSearchQuery query, CancellationToken ct)
    {
        query.Project = RequireContext().Project;
        return ApiResponse<PagedData<RuleSearchResult>>.Ok(
            await _search.SearchRulesAsync(query, ct));
    }

    // ── 创建菜单/按钮 ─────────────────────────────────────────────

    /// <summary>POST /api/rule — 新建菜单或按钮规则。</summary>
    [HttpPost]
    public async Task<ApiResponse<object>> Create(
        [FromBody] CreateRuleRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();
        if (string.IsNullOrWhiteSpace(req.RuleCode)) return Fail(40001, "ruleCode 不能为空");
        if (string.IsNullOrWhiteSpace(req.PermissionCode)) return Fail(40001, "permissionCode 不能为空");
        if (string.IsNullOrWhiteSpace(req.Title)) return Fail(40001, "title 不能为空");

        if (!Enum.TryParse<RuleType>(req.Type, ignoreCase: true, out var ruleType))
            return Fail(40001, $"无效的 type: {req.Type}");

        RbacRule rule;
        if (ruleType == RuleType.Button)
        {
            if (string.IsNullOrWhiteSpace(req.ParentRuleCode))
                return Fail(40001, "Button 类型必须指定 parentRuleCode");

            rule = RbacRule.CreateButton(
                Guid.NewGuid(),
                new ProjectCode(ctx.Project),
                new RuleCode(req.RuleCode),
                new PermissionCode(req.PermissionCode),
                req.Title,
                req.Name ?? req.RuleCode,
                new RuleCode(req.ParentRuleCode),
                icon: req.Icon,
                remark: req.Remark,
                weigh: req.Weigh);
        }
        else
        {
            RuleType parsedType = ruleType;
            MenuType? menuType = null;
            if (!string.IsNullOrWhiteSpace(req.MenuType) &&
                Enum.TryParse<MenuType>(req.MenuType, ignoreCase: true, out var mt))
                menuType = mt;

            rule = RbacRule.CreateMenu(
                Guid.NewGuid(),
                new ProjectCode(ctx.Project),
                new RuleCode(req.RuleCode),
                new PermissionCode(req.PermissionCode),
                parsedType,
                req.Title,
                req.Name ?? req.RuleCode,
                req.Path ?? string.Empty,
                parentRuleCode: string.IsNullOrWhiteSpace(req.ParentRuleCode)
                    ? null : new RuleCode(req.ParentRuleCode),
                menuType: menuType,
                url: req.Url,
                component: req.Component,
                extend: req.Extend,
                icon: req.Icon,
                remark: req.Remark,
                keepalive: req.Keepalive,
                weigh: req.Weigh);
        }

        await _write.SaveRuleAsync(
            rule,
            changeKind: "Created",
            affectedPermissionCodes: new[] { rule.PermissionCode.Value },
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(new { ruleCode = rule.RuleCode.Value, weigh = rule.Weigh });
    }

    // ── 状态变更 ──────────────────────────────────────────────────

    /// <summary>PUT /api/rule/{ruleCode}/status — 启用/禁用规则。</summary>
    [HttpPut("{ruleCode}/status")]
    public async Task<ApiResponse<object>> ChangeStatus(
        string ruleCode, [FromBody] ChangeRuleStatusRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        var rule = await _guard.LoadRuleByCodeAsync(ruleCode, ctx.Project, ct);
        if (rule is null) return Fail(40400, "规则不存在");

        if (req.Status == "Disabled") rule.Disable();
        else rule.Enable();

        await _write.SaveRuleAsync(
            rule,
            changeKind: "StatusChanged",
            affectedPermissionCodes: new[] { rule.PermissionCode.Value },
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // ── 排序 ──────────────────────────────────────────────────────

    /// <summary>PUT /api/rule/{ruleCode}/weigh — 更新规则排序权重。</summary>
    [HttpPut("{ruleCode}/weigh")]
    public async Task<ApiResponse<object>> UpdateWeigh(
        string ruleCode, [FromBody] UpdateWeighRequest req, CancellationToken ct)
    {
        var ctx = RequireContext();

        var rule = await _guard.LoadRuleByCodeAsync(ruleCode, ctx.Project, ct);
        if (rule is null) return Fail(40400, "规则不存在");

        rule.UpdateWeigh(req.Weigh);

        await _write.SaveRuleAsync(
            rule,
            changeKind: "Reordered",
            affectedPermissionCodes: Array.Empty<string>(),
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // ── 删除 ──────────────────────────────────────────────────────

    /// <summary>DELETE /api/rule/{ruleCode} — 删除规则。</summary>
    [HttpDelete("{ruleCode}")]
    public async Task<ApiResponse<object>> Delete(string ruleCode, CancellationToken ct)
    {
        var ctx = RequireContext();

        var rule = await _guard.LoadRuleByCodeAsync(ruleCode, ctx.Project, ct);
        if (rule is null) return Fail(40400, "规则不存在");

        await _write.DeleteRuleAsync(
            rule,
            affectedPermissionCodes: new[] { rule.PermissionCode.Value },
            operatorUserid: ctx.Userid,
            ct);

        return ApiResponse<object>.Ok(null!);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────

    private CurrentRbacContext RequireContext() =>
        _ctx.Context ?? throw new InvalidOperationException("RbacContext missing");

    private static ApiResponse<object> Fail(int code, string msg) =>
        ApiResponse<object>.Fail(code, msg);
}

// ── Request DTOs ───────────────────────────────────────────────────

public sealed record CreateRuleRequest(
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

public sealed record ChangeRuleStatusRequest(string Status);
public sealed record UpdateWeighRequest(int Weigh);
