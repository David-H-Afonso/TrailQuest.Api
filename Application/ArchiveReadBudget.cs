using Microsoft.EntityFrameworkCore;
using TrailQuest.Api.Domain.Entities;

namespace TrailQuest.Api.Application;

public static class ArchiveReadBudget
{
    public const int MaxRecords = 10000;
    public const int MaxJsonCharacters = 8 * 1024 * 1024;

    // Called inside a read transaction before materializing private JSON/track payloads.
    public static async Task EnsureAsync(IQueryable<HealthDocument> documents, IQueryable<ActivityRecord> activities,
        bool includeTracks, CancellationToken ct)
    {
        var records = await documents.CountAsync(ct) + await activities.CountAsync(ct);
        if (records > MaxRecords) throw TooLarge();
        var characters = (await documents.SumAsync(x => (long?)x.PayloadJson.Length, ct) ?? 0) +
            (await activities.SumAsync(x => (long?)x.ExercisesJson.Length, ct) ?? 0);
        if (includeTracks) characters += await activities.SumAsync(x => (long?)x.TrackJson.Length, ct) ?? 0;
        if (characters > MaxJsonCharacters) throw TooLarge();
    }

    public static AppException TooLarge() => new("archive_too_large", 413,
        "This selection is too large to read safely. Select a shorter date range or fewer source records; no records were removed or silently truncated.");
}
