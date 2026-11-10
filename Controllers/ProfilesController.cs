using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrailQuest.Api.Application;
using TrailQuest.Api.Contracts;

namespace TrailQuest.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/profiles/{profileId}")]
public sealed class ProfilesController(AdventureService adventures, ExportCoordinator exports, CurrentUser current) : ControllerBase
{
    [HttpGet("dashboard")]
    public Task<DashboardDto> Dashboard(string profileId, CancellationToken cancellationToken) =>
        adventures.GetDashboardAsync(profileId, cancellationToken);

    [HttpPut("days/{date}")]
    public async Task<IActionResult> SaveDay(string profileId, DateOnly date, SaveDailyStepsRequest request, CancellationToken cancellationToken)
    {
        await adventures.SaveDayAsync(profileId, date, request.Steps, cancellationToken);
        return NoContent();
    }

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings(string profileId, SaveSettingsRequest request, CancellationToken cancellationToken)
    {
        await adventures.SaveSettingsAsync(profileId, request, cancellationToken);
        return NoContent();
    }

    [HttpPost("journeys")]
    public async Task<IActionResult> StartJourney(string profileId, StartJourneyRequest request, CancellationToken cancellationToken)
    {
        await adventures.StartJourneyAsync(profileId, request, cancellationToken);
        return NoContent();
    }

    [HttpPut("journeys/{routeId}")]
    public async Task<IActionResult> UpdateJourney(string profileId, string routeId, UpdateJourneyRequest request, CancellationToken cancellationToken)
    {
        await adventures.UpdateJourneyAsync(profileId, routeId, request, cancellationToken);
        return NoContent();
    }

    [HttpDelete("journeys/{routeId}")]
    public async Task<IActionResult> LeaveJourney(string profileId, string routeId, CancellationToken cancellationToken)
    {
        await adventures.LeaveJourneyAsync(profileId, routeId, cancellationToken);
        return NoContent();
    }

    [HttpPost("garmin/sync")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("garmin")]
    public Task<SyncResultDto> Sync(string profileId, SyncGarminRequest request, CancellationToken cancellationToken) =>
        adventures.SyncGarminAsync(profileId, request, cancellationToken);

    [HttpPut("journeys/{routeId}/content")]
    public async Task<IActionResult> UpdateContent(string profileId, string routeId, CancellationToken cancellationToken)
    {
        await adventures.UpdateContentAsync(profileId, routeId, cancellationToken);
        return NoContent();
    }

    [HttpGet("export")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("export")]
    public async Task<IActionResult> Export(string profileId, CancellationToken cancellationToken, [FromQuery] DateOnly? startDate = null, [FromQuery] DateOnly? endDate = null)
    {
        using var lease = exports.Enter(current.Id);
        var export = await adventures.ExportAsync(profileId, startDate, endDate, cancellationToken);
        Response.ContentType = "application/json";
        Response.Headers["X-TrailQuest-Export-Range"] = startDate is { } start && endDate is { } end ? $"{start:yyyy-MM-dd}/{end:yyyy-MM-dd}" : "all";
        Response.Headers.ContentDisposition = $"attachment; filename=\"trailquest-{profileId}-{export.ExportedAtUtc:yyyy-MM-dd}.json\"";
        await JsonSerializer.SerializeAsync(Response.Body, export, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }, cancellationToken);
        return new EmptyResult();
    }
}
