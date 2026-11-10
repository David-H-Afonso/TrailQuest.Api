using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TrailQuest.Api.Application;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Infrastructure.Garmin;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Controllers;

[ApiController, Authorize, Route("api/v1/profiles/{profileId}")]
public sealed class HealthController(HealthService health, TrailQuestDbContext db) : ControllerBase
{
    [HttpGet("health")]
    public Task<HealthOverviewDto> Overview(string profileId, DateOnly? startDate, DateOnly? endDate, CancellationToken ct) => health.OverviewAsync(profileId, startDate, endDate, ct);
    [HttpGet("activities/{id:long}")]
    public Task<ActivityDetailDto> Activity(string profileId, long id, CancellationToken ct) => health.DetailAsync(profileId, id, ct);
    [HttpGet("health/documents/{dataset}")]
    public Task<IReadOnlyList<RawDocumentDto>> Documents(string profileId, string dataset, CancellationToken ct, [FromQuery] int limit = 31) => health.DocumentsAsync(profileId, dataset, ct, limit);
    [HttpGet("challenges")]
    public Task<IReadOnlyList<ChallengeDto>> Challenges(string profileId, CancellationToken ct) => health.ChallengesAsync(profileId, ct);
    [HttpPost("activities/import"), EnableRateLimiting("garmin"), RequestSizeLimit(ActivityFileReader.MaximumBytes + 65536), RequestFormLimits(MultipartBodyLengthLimit = ActivityFileReader.MaximumBytes)]
    public async Task<IActionResult> Import(string profileId, IFormFile file, [FromForm] string? name, CancellationToken ct)
    {
        if (file.Length is 0 or > ActivityFileReader.MaximumBytes) return BadRequest(new { code = "activity_file_invalid" });
        using var output = new MemoryStream(); await file.CopyToAsync(output, ct);
        var id = await health.ImportFileAsync(profileId, output.ToArray(), name ?? "", ct);
        return Ok(new { id });
    }
    [HttpGet("activities/{id:long}/original")]
    public async Task<IActionResult> Original(string profileId, long id, CancellationToken ct)
    {
        var profile = await health.ProfileAsync(profileId, ct);
        var row = await db.Activities.AsNoTracking().Where(x => x.Id == id && x.ProfileId == profile.Id).Select(x => new { x.OriginalFile, x.OriginalFormat }).SingleOrDefaultAsync(ct);
        if (row?.OriginalFile is null) return NotFound();
        var extension = row.OriginalFile.Length > 2 && row.OriginalFile[0] == 'P' && row.OriginalFile[1] == 'K' ? "zip" : row.OriginalFormat ?? "bin";
        return File(row.OriginalFile, "application/octet-stream", $"trailquest-activity-{id}.{extension}");
    }
}

[ApiController, Authorize, Route("api/v1/connections/garmin")]
public sealed class GarminSyncController(GarminSyncService sync) : ControllerBase
{
    [HttpGet("datasets")]
    public IReadOnlyList<DatasetDto> Datasets() => GarminDatasets.All;
    [HttpGet("preferences")]
    public Task<SyncPreferencesDto> Preferences(CancellationToken ct) => sync.PreferencesAsync(ct);
    [HttpPut("preferences")]
    public async Task<IActionResult> Save(SaveSyncPreferencesRequest request, CancellationToken ct) { await sync.SavePreferencesAsync(request, ct); return NoContent(); }
    [HttpPost("sync"), EnableRateLimiting("garmin")]
    public Task<FullSyncResultDto> Sync(FullSyncRequest request, CancellationToken ct) => sync.SyncAsync(request, ct);
}
