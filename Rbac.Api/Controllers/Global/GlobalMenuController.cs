using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Search;
using Rbac.Application.Security;

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
    private readonly IRbacManagementSearchService _search;

    public GlobalMenuController(IRbacManagementSearchService search)
    {
        _search = search;
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
}
