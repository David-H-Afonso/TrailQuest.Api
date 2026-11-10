using System.Collections.Concurrent;

namespace TrailQuest.Api.Application;

public sealed class ExportCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> users = new();

    public IDisposable Enter(string userId)
    {
        var semaphore = users.GetOrAdd(userId, _ => new(1, 1));
        if (!semaphore.Wait(0)) throw new AppException("export_in_progress", 429, "An export is already running for this account.");
        return new Lease(semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) semaphore.Release(); }
    }
}
