using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using TrailQuest.Api.Application;

namespace TrailQuest.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/demo")]
public sealed class DemoController(AdventureService adventures) : ControllerBase
{
    [HttpPost("advance")]
    public async Task<IActionResult> Advance(CancellationToken cancellationToken)
    {
        await adventures.AdvanceDemoAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset(CancellationToken cancellationToken)
    {
        await adventures.ResetDemoAsync(cancellationToken);
        return NoContent();
    }
}
