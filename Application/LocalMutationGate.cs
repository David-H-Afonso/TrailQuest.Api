namespace TrailQuest.Api.Application;

public sealed class LocalMutationGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}
