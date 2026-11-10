using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TrailQuest.Api.Application;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Domain.Entities;
using TrailQuest.Api.Infrastructure.Security;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Controllers;

[ApiController, Authorize, Route("api/v1/integrations")]
public sealed class IntegrationsController(TrailQuestDbContext db, CurrentUser current, TimeProvider clock) : ControllerBase
{
    public static readonly string[] Scopes = ["steps:read", "activities:read", "wellness:read", "routes:read"];
    [HttpGet("identity")]
    public object Identity() => new { applicationUserId = current.Id, provider = "local", externalIdentityLinked = false,
        note = "Account identifiers are local. Cross-application identity must be linked through a verified identity provider, never by username." };
    [HttpGet("grants")]
    public async Task<IReadOnlyList<GrantDto>> Grants(CancellationToken ct)
    {
        var id = current.Id;
        var rows = await db.IntegrationGrants.AsNoTracking().Where(x => x.UserId == id).ToListAsync(ct);
        return rows.Select(x => new GrantDto(x.Id, x.Application, JsonSerializer.Deserialize<string[]>(x.ScopesJson) ?? [], x.ExpiresAtUtc)).ToList();
    }
    [HttpPost("grants"), EnableRateLimiting("accounts")]
    public async Task<CreatedGrantDto> Create(CreateGrantRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Application) || request.Application.Length > 60 || request.Application.Any(char.IsControl) ||
            request.Scopes is null || request.Scopes.Count is < 1 or > 4 || request.Scopes.Distinct().Count() != request.Scopes.Count ||
            request.Scopes.Any(x => !Scopes.Contains(x)) || request.Days is < 1 or > 90)
            throw new AppException("invalid_grant", 400, "Choose an application, permitted read scopes and a 1–90 day lifetime.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var row = new IntegrationGrant { UserId = current.Id, Application = request.Application.Trim(), TokenHash = IntegrationAuthentication.Hash(token),
            ScopesJson = JsonSerializer.Serialize(request.Scopes), CreatedAtUtc = clock.GetUtcNow(), ExpiresAtUtc = clock.GetUtcNow().AddDays(request.Days) };
        db.IntegrationGrants.Add(row); await db.SaveChangesAsync(ct);
        return new(new(row.Id, row.Application, request.Scopes, row.ExpiresAtUtc), token);
    }
    [HttpDelete("grants/{id}")]
    public async Task<IActionResult> Revoke(string id, CancellationToken ct)
    {
        var user = current.Id;
        if (await db.IntegrationGrants.Where(x => x.Id == id && x.UserId == user).ExecuteDeleteAsync(ct) == 0) return NotFound();
        return NoContent();
    }
}

[ApiController, Authorize(AuthenticationSchemes = IntegrationAuthentication.SchemeName), Route("api/v1/provider"), EnableRateLimiting("provider")]
public sealed class ProviderController(HealthService health, TrailQuestDbContext db) : ControllerBase
{
    private void Require(string scope)
    {
        if (!User.HasClaim("scope", scope)) throw new AppException("insufficient_scope", 403, "This application is not authorized to read this dataset.");
    }
    [HttpGet("steps")]
    public async Task<object> Steps(DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        Require("steps:read"); HealthService.ValidateRange(startDate, endDate);
        var profile = await health.ProfileAsync("personal", ct);
        var days = await db.DailySteps.AsNoTracking().Where(x => x.ProfileId == profile.Id && x.Date >= startDate && x.Date <= endDate).OrderBy(x => x.Date)
            .Select(x => new { x.Date, x.Steps, x.DistanceMeters, x.IsDistanceEstimated, x.Source, x.UpdatedAtUtc }).ToListAsync(ct);
        return new { schemaVersion = 1, startDate, endDate, timeZoneId = Domain.TimeZoneIds.ForClient(profile.TimeZoneId), days };
    }
    [HttpGet("activities")]
    public async Task<object> Activities(DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        Require("activities:read");
        var overview = await health.OverviewAsync("personal", startDate, endDate, ct);
        return new { schemaVersion = 1, startDate, endDate, activities = overview.Activities };
    }
    [HttpGet("wellness")]
    public async Task<object> Wellness(DateOnly startDate, DateOnly endDate, CancellationToken ct)
    {
        Require("wellness:read");
        var overview = await health.OverviewAsync("personal", startDate, endDate, ct);
        return new { schemaVersion = 1, startDate, endDate, overview.Days, overview.Coverage, overview.LastSyncAtUtc };
    }
    [HttpGet("activities/{id:long}/track")]
    public async Task<object> Track(long id, CancellationToken ct)
    {
        Require("routes:read");
        var detail = await health.DetailAsync("personal", id, ct);
        return new { schemaVersion = 1, detail.Track };
    }
}
