using System.Text.Json;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Domain.Entities;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Infrastructure.Catalog;

// Optional fixture content. No production query depends on this file or invents fallback values.
public sealed class DemoData(IWebHostEnvironment environment, IConfiguration configuration, RouteCatalog catalog)
{
    private DemoFixture? Load()
    {
        if (!configuration.GetValue("Demo:Enabled", false)) return null;
        var path = configuration["Demo:FixturePath"] ?? Path.Combine(environment.ContentRootPath, "Content", "Demo", "sample.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<DemoFixture>(File.ReadAllText(path), RouteCatalog.Json) : null;
    }

    public bool Available => Load() is not null;

    public void Seed(TrailQuestDbContext db, ProfileSettings profile, DateOnly today, DateTimeOffset now)
    {
        var fixture = Load();
        if (fixture is null) return;
        foreach (var routeId in fixture.Journeys ?? [])
        {
            if (routeId == profile.SelectedRouteId) continue;
            var definition = catalog.Get(routeId);
            db.Journeys.Add(new Journey { ProfileId = profile.Id, RouteId = definition.Id, ContentVersion = definition.ContentVersion,
                DefinitionJson = JsonSerializer.Serialize(definition, RouteCatalog.Json), StartedOn = today.AddDays(-6), CreatedAtUtc = now,
                LeftAtUtc = now });
        }
        foreach (var (steps, index) in fixture.Steps.Select((value, index) => (value, index)))
            db.DailySteps.Add(new DailySteps { ProfileId = profile.Id, Date = today.AddDays(index - fixture.Steps.Length + 1),
                Steps = steps, DistanceMeters = steps * profile.StepLengthCm / 100, StepLengthCm = profile.StepLengthCm, Source = "demo", UpdatedAtUtc = now });
        foreach (var item in fixture.Activities)
            db.Activities.Add(new ActivityRecord { ProfileId = profile.Id, SourceId = item.Id, Source = "demo", Name = item.Name,
                Sport = item.Sport, Date = today.AddDays(item.OffsetDays), StartedAtUtc = new DateTimeOffset(today.AddDays(item.OffsetDays).ToDateTime(new TimeOnly(17, 0)), TimeSpan.Zero),
                DurationSeconds = item.DurationSeconds, DistanceMeters = item.DistanceMeters, CaloriesKcal = item.CaloriesKcal,
                AverageHeartRate = item.AverageHeartRate, Repetitions = item.Repetitions, Sets = item.Sets, VolumeKg = item.VolumeKg,
                ExercisesJson = JsonSerializer.Serialize(item.Exercises, RouteCatalog.Json), TrackJson = JsonSerializer.Serialize(item.Track, RouteCatalog.Json), TrackPointCount = item.Track.Count,
                OriginalStatus = item.Track.Count > 0 ? "imported" : "no-track", UpdatedAtUtc = now });
        foreach (var item in fixture.Health)
        {
            var date = today.AddDays(item.OffsetDays);
            var payload = item.Payload.GetRawText().Replace("@DATE", date.ToString("yyyy-MM-dd"), StringComparison.Ordinal);
            db.HealthDocuments.Add(new HealthDocument { ProfileId = profile.Id, Dataset = item.Dataset, Date = date,
                SourceKey = date.ToString("yyyy-MM-dd"), Source = "demo", PayloadJson = payload,
                ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload))), RetrievedAtUtc = now });
        }
    }

    public IReadOnlyList<PostDto> Posts(string userId, DateTimeOffset now) => Load()?.Posts.Select((x, i) =>
        new PostDto($"demo-{i}", userId, x.Author, x.Kind, x.Title, x.Summary, now.AddHours(-i - 1), false, [])).ToList() ?? [];
}

public sealed record DemoFixture(int[] Steps, IReadOnlyList<DemoActivity> Activities, IReadOnlyList<DemoHealth> Health, IReadOnlyList<DemoPost> Posts, IReadOnlyList<string>? Journeys = null);
public sealed record DemoActivity(string Id, string Name, string Sport, int OffsetDays, double DurationSeconds,
    double? DistanceMeters, double? CaloriesKcal, double? AverageHeartRate, int? Repetitions, int? Sets, double? VolumeKg,
    IReadOnlyList<ExerciseDto> Exercises, IReadOnlyList<TrackPointDto> Track);
public sealed record DemoHealth(string Dataset, int OffsetDays, JsonElement Payload);
public sealed record DemoPost(string Author, string Kind, string Title, string Summary);
