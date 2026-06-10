using Microsoft.AspNetCore.Mvc;
using Rbac.Application.Authentication;
using Rbac.Application.Contracts.Common;
using Rbac.Application.Contracts.Compatibility;
using Rbac.Application.Menus;
using Rbac.Application.Repositories;
using Rbac.Application.Security;
using Rbac.Domain.Users;
using Rbac.Domain.ValueObjects;

namespace Rbac.Api.Controllers;

/// <summary>
/// Authentication entry endpoints.
///
/// This controller does not issue or refresh tokens. It validates the already
/// supplied portal JWT against RBAC project access and returns the frontend
/// entry payload.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IUserIdentityResolver _userIdentityResolver;
    private readonly ICurrentRbacContextAccessor _contextAccessor;
    private readonly IRbacProjectResolver _projectResolver;
    private readonly IAdministratorRepository _adminRepository;
    private readonly RbacMenuBuilder _menuBuilder;
    private readonly RbacLoginResultFactory _loginResultFactory;

    public AuthController(
        IUserIdentityResolver userIdentityResolver,
        ICurrentRbacContextAccessor contextAccessor,
        IRbacProjectResolver projectResolver,
        IAdministratorRepository adminRepository,
        RbacMenuBuilder menuBuilder,
        RbacLoginResultFactory loginResultFactory)
    {
        _userIdentityResolver = userIdentityResolver;
        _contextAccessor = contextAccessor;
        _projectResolver = projectResolver;
        _adminRepository = adminRepository;
        _menuBuilder = menuBuilder;
        _loginResultFactory = loginResultFactory;
    }

    /// <summary>
    /// POST /api/auth/login - validate whether the current JWT user can enter a project.
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<LoginResponseDto>>> Login(
        CancellationToken ct)
    {
        var userid = ResolveUserid();
        if (string.IsNullOrWhiteSpace(userid))
        {
            return Unauthorized(ApiResponse<LoginResponseDto>.Fail(40100, "JWT userid missing"));
        }

        var requestedProject = ResolveProject();
        if (string.IsNullOrWhiteSpace(requestedProject))
        {
            return BadRequest(ApiResponse<LoginResponseDto>.Fail(40001, "project 不能为空"));
        }

        var rbacContext = await _projectResolver.ResolveAsync(
            userid, requestedProject, HttpContext.TraceIdentifier, ct);

        if (!rbacContext.IsProjectAuthorized)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<LoginResponseDto>.Fail(40300, "无权访问当前 project"));
        }

        var admin = await _adminRepository.FindByUseridAsync(new UserId(userid), ct);
        if (admin is { Status: AdminStatus.Disabled })
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<LoginResponseDto>.Fail(40300, "管理员账号已禁用"));
        }

        var adminInfo = BuildAdminInfo(admin, rbacContext);
        var menus = await _menuBuilder.BuildUserMenusAsync(
            rbacContext.Userid,
            rbacContext.Project,
            rbacContext.IsProjectSuper,
            ct);
        var routePath = RbacMenuRoutePathResolver.ResolveRoutePath(
            menus,
            RbacLoginResultFactory.DefaultDashboardPath);

        var response = _loginResultFactory.BuildSuccess(
            ResolveBearerToken(),
            adminInfo,
            routePath);

        return ApiResponse<LoginResponseDto>.Ok(response);
    }

    private string ResolveUserid()
    {
        var userid = _userIdentityResolver.ResolveUserId(User);
        if (!string.IsNullOrWhiteSpace(userid))
            return userid;

        return _contextAccessor.Context?.Userid ?? string.Empty;
    }

    private string ResolveProject()
    {
        var context = _contextAccessor.Context;
        return !string.IsNullOrWhiteSpace(context?.RequestedProject)
            ? context.RequestedProject
            : context?.Project ?? string.Empty;
    }

    private string ResolveBearerToken()
    {
        var header = HttpContext.Request.Headers.Authorization.FirstOrDefault();
        const string prefix = "Bearer ";
        return header is not null && header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : string.Empty;
    }

    private static AdminInfoDto BuildAdminInfo(RbacAdministrator? admin, CurrentRbacContext context)
    {
        if (admin is null)
        {
            return new AdminInfoDto
            {
                Userid = context.Userid,
                Username = context.Userid,
                Project = context.Project,
                Super = context.IsProjectSuper,
            };
        }

        return new AdminInfoDto
        {
            Userid = admin.Userid.Value,
            Username = admin.Username,
            Project = context.Project,
            Super = context.IsProjectSuper,
        };
    }
}
