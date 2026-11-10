using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Domain.Entities;
using TrailQuest.Api.Infrastructure.Persistence;
using TrailQuest.Api.Infrastructure.Security;

namespace TrailQuest.Api.Application;

public sealed class GarminConnectionService(
    TrailQuestDbContext db, CurrentUser current, SecurityStorage storage,
    IDataProtectionProvider protection, IGarminClient client, LocalMutationGate gate, TimeProvider clock) : IGarminGateway
{
    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken)
    {
        var userId = current.Id;
        return await db.GarminConnections.AnyAsync(x => x.UserId == userId && x.InstallationId == storage.InstallationId, cancellationToken);
    }

    public async Task<string?> GetVersionAsync(CancellationToken cancellationToken)
    {
        var userId = current.Id;
        return await db.GarminConnections.AsNoTracking().Where(x => x.UserId == userId && x.InstallationId == storage.InstallationId)
            .Select(x => x.Version).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<GarminConnectionDto> StatusAsync(CancellationToken cancellationToken)
    {
        var userId = current.Id;
        var row = await db.GarminConnections.AsNoTracking().Where(x => x.UserId == userId && x.InstallationId == storage.InstallationId)
            .Select(x => new { x.VerifiedAtUtc }).SingleOrDefaultAsync(cancellationToken);
        return new GarminConnectionDto(row is not null, row?.VerifiedAtUtc);
    }

    public async Task SaveAsync(SaveGarminKeyRequest request, CancellationToken cancellationToken)
    {
        var key = request.ApiKey?.Trim();
        if (key is null || key.Length is < 8 or > 4096 || key.Any(char.IsWhiteSpace) || key.Any(char.IsControl))
            throw new AppException("invalid_garmin_key", 400, "Enter a valid personal API key without whitespace.");
        var userId = current.Id;
        await client.VerifyAsync(key, cancellationToken);
        var encrypted = Protector(userId).Protect(key);
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            var connection = await db.GarminConnections.SingleOrDefaultAsync(x => x.UserId == userId && x.InstallationId == storage.InstallationId, cancellationToken);
            if (connection is null)
            {
                connection = new GarminConnection { UserId = userId, InstallationId = storage.InstallationId };
                db.GarminConnections.Add(connection);
            }
            else client.Invalidate(Scope(connection));
            connection.ProtectedApiKey = encrypted;
            connection.Version = Guid.NewGuid().ToString("N");
            connection.VerifiedAtUtc = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }
        finally { gate.Semaphore.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        var userId = current.Id;
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            var connection = await db.GarminConnections.SingleOrDefaultAsync(x => x.UserId == userId && x.InstallationId == storage.InstallationId, cancellationToken);
            if (connection is null) return;
            client.Invalidate(Scope(connection));
            db.GarminConnections.Remove(connection);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally { gate.Semaphore.Release(); }
    }

    public async Task<IReadOnlyList<GarminDay>> GetDaysAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        var userId = current.Id;
        var connection = await db.GarminConnections.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.InstallationId == storage.InstallationId, cancellationToken)
            ?? throw new AppException("garmin_not_configured", 409, "Configure your Garmin connection in the app first.");
        string key;
        try { key = Protector(userId).Unprotect(connection.ProtectedApiKey); }
        catch (CryptographicException)
        {
            throw new AppException("garmin_key_unavailable", 409, "The local credential cannot be decrypted. Reconnect Garmin on this installation.");
        }
        return await client.GetDaysAsync(key, Scope(connection), startDate, endDate, cancellationToken);
    }

    public async Task<System.Text.Json.JsonElement> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var (key, scope) = await CredentialAsync(cancellationToken);
        return await client.ReadAsync(key, scope, path, cancellationToken);
    }

    public async Task<byte[]> DownloadAsync(long activityId, CancellationToken cancellationToken)
    {
        var (key, scope) = await CredentialAsync(cancellationToken);
        return await client.DownloadAsync(key, scope, activityId, cancellationToken);
    }

    private async Task<(string Key, string Scope)> CredentialAsync(CancellationToken cancellationToken)
    {
        var id = current.Id;
        var row = await db.GarminConnections.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == id && x.InstallationId == storage.InstallationId, cancellationToken)
            ?? throw new AppException("garmin_not_configured", 409, "Configure your Garmin connection first.");
        try { return (Protector(id).Unprotect(row.ProtectedApiKey), Scope(row)); }
        catch (CryptographicException) { throw new AppException("garmin_key_unavailable", 409, "Reconnect Garmin on this installation."); }
    }

    private IDataProtector Protector(string userId) => protection.CreateProtector("TrailQuest.Garmin", "v1", storage.InstallationId, userId);
    private static string Scope(GarminConnection connection) => $"{connection.UserId}:{connection.InstallationId}:{connection.Version}";
}
