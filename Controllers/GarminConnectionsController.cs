using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TrailQuest.Api.Application;
using TrailQuest.Api.Contracts;

namespace TrailQuest.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/connections/garmin")]
public sealed class GarminConnectionsController(GarminConnectionService connections) : ControllerBase
{
    [HttpGet]
    public Task<GarminConnectionDto> Status(CancellationToken cancellationToken) => connections.StatusAsync(cancellationToken);

    [HttpPut]
    [EnableRateLimiting("garmin-credentials")]
    public async Task<GarminConnectionDto> Save(SaveGarminKeyRequest request, CancellationToken cancellationToken)
    {
        await connections.SaveAsync(request, cancellationToken);
        return await connections.StatusAsync(cancellationToken);
    }

    [HttpDelete]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        await connections.DisconnectAsync(cancellationToken);
        return NoContent();
    }
}
