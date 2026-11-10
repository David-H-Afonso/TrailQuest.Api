using System.Security.Claims;

namespace TrailQuest.Api.Application;

public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    private string? workerUserId;
    internal void UseWorkerIdentity(string userId)
    {
        if (accessor.HttpContext is not null) throw new InvalidOperationException("Worker identities cannot replace HTTP identities.");
        workerUserId = userId;
    }
    public string Id => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? workerUserId
        ?? throw new AppException("authentication_required", 401, "Sign in to access your data.");
}
