using TrailQuest.Api.Contracts;
using TrailQuest.Api.Domain.Entities;

namespace TrailQuest.Api.Application;

public static class ProgressCalculator
{
    public static JourneyDto Journey(
        Journey journey, AdventureRouteDto route, IReadOnlyList<DailySteps> days,
        IReadOnlyList<CheckpointVisit> visits, DateOnly today)
    {
        var (steps, km) = JourneyTotals(journey, days, today);
        var next = route.Checkpoints.FirstOrDefault(x => x.DistanceKm > km + 0.000001);
        return new JourneyDto(
            route.Id, journey.StartedOn, steps, Math.Round(km, 4),
            Math.Round(Math.Clamp(km / route.DistanceKm * 100, 0, 100), 2),
            Math.Round(Math.Max(0, route.DistanceKm - km), 4), next?.Id,
            Math.Round(Math.Max(0, (next?.DistanceKm ?? km) - km), 4),
            visits.Where(x => x.JourneyId == journey.Id)
                .Select(x => new CheckpointVisitDto(x.CheckpointId, x.UnlockedAtUtc)).ToList())
        { IsActive = journey.LeftAtUtc is null, LeftAtUtc = journey.LeftAtUtc, CountingFrom = journey.CountingFrom };
    }

    public static (long Steps, double DistanceKm) JourneyTotals(Journey journey, IReadOnlyList<DailySteps> days, DateOnly today)
    {
        var countingFrom = journey.CountingFrom ?? journey.StartedOn;
        var eligible = days.Where(x => x.Date >= countingFrom && x.Date <= today).ToList();
        var frozen = journey.LeftAtUtc is not null && journey.SavedSteps.HasValue && journey.SavedDistanceKm.HasValue;
        var steps = frozen ? journey.SavedSteps!.Value : (journey.SavedSteps ?? 0) + eligible.Sum(x => x.Date == countingFrom ? Math.Max(0, (long)x.Steps - journey.InitialDaySteps) : x.Steps);
        var km = frozen ? journey.SavedDistanceKm!.Value : (journey.SavedDistanceKm ?? 0) + eligible.Sum(x => x.Date == countingFrom ? Math.Max(0, x.DistanceMeters / 1000 - journey.InitialDayDistanceKm) : x.DistanceMeters / 1000);
        return (steps, km);
    }

    public static TotalsDto Totals(IReadOnlyList<DailySteps> days, int goal, DateOnly today)
    {
        var eligible = days.Where(x => x.Date <= today).ToList();
        var steps = eligible.Sum(x => (long)x.Steps);
        var xp = steps / 10;
        return new TotalsDto(steps, Math.Round(eligible.Sum(x => x.DistanceMeters) / 1000, 4),
            eligible.Count(x => x.Steps > 0), xp, 1 + xp / 1000, xp % 1000, 1000,
            eligible.Count(x => x.Steps >= goal));
    }
}
