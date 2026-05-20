using Microsoft.AspNetCore.Mvc;

namespace Rbac.Api.Controllers;

[ApiController]
[Route("api/test")]
public sealed class TestController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { ok = true });
}
