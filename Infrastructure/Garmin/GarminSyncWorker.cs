using Microsoft.EntityFrameworkCore;
using TrailQuest.Api.Application;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Infrastructure.Garmin;

// Exists only inside the running app. No external timer, OS task or service is installed.
public sealed class GarminSyncWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<GarminSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunDueAsync(stoppingToken);
    }

    public async Task RunDueAsync(CancellationToken stoppingToken)
    {
        using var discovery = scopes.CreateScope();
        var db = discovery.ServiceProvider.GetRequiredService<TrailQuestDbContext>();
        var rows = await db.SyncSettings.AsNoTracking().Where(x => x.IntervalHours == 12 || x.IntervalHours == 24).ToListAsync(stoppingToken);
        foreach (var row in rows.Where(x => x.LastAttemptAtUtc is null || x.LastAttemptAtUtc <= clock.GetUtcNow().AddHours(-x.IntervalHours)))
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<CurrentUser>().UseWorkerIdentity(row.UserId);
            if (!await scope.ServiceProvider.GetRequiredService<GarminConnectionService>().IsConfiguredAsync(stoppingToken)) continue;
            try { await scope.ServiceProvider.GetRequiredService<GarminSyncService>().SyncAsync(new FullSyncRequest(null, null), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (AppException error) { logger.LogWarning("A scheduled Garmin import was not completed ({Code}).", error.Code); }
            catch (Exception) { logger.LogError("A scheduled Garmin import failed. Private provider responses are not logged."); }
        }
    }
}
