using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Global;
using Rbac.Application.Repositories;
using Rbac.Application.Security;

namespace Rbac.Api.Controllers.Global;

/// <summary>
/// 项目发现接口。
/// 调用方必须携带 X-Project: __global__ header。
/// </summary>
[ApiController]
[Route("api/global/project")]
public sealed class GlobalProjectController : ControllerBase
{
    private readonly ICurrentRbacContextAccessor _ctx;
    private readonly IProjectGrantRepository _grantRepo;

    public GlobalProjectController(
        ICurrentRbacContextAccessor ctx,
        IProjectGrantRepository grantRepo)
    {
        _ctx = ctx;
        _grantRepo = grantRepo;
    }

    /// <summary>
    /// GET /api/global/project/list — 获取所有已知业务 project 列表。
    /// </summary>
    [HttpGet("list")]
    public async Task<ApiResponse<PagedData<string>>> List(CancellationToken ct)
    {
        _ = _ctx.Context ?? throw new InvalidOperationException("RbacContext missing");

        var all = await _grantRepo.GetDistinctProjectsAsync(ct);
        var projects = all
            .Where(p => !RbacGlobalConstants.IsReservedProject(p))
            .ToList();

        return ApiResponse<PagedData<string>>.Ok(new PagedData<string>
        {
            List = projects,
            Total = projects.Count,
        });
    }
}
