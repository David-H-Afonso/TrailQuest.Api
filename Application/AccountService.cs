using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Domain.Entities;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Application;

public sealed class AccountService(
    TrailQuestDbContext db, IPasswordHasher<AppUser> passwords, TimeProvider clock,
    LocalMutationGate gate, AdventureService adventures, CurrentUser current)
{
    public async Task<SessionDto> GetSessionAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = id is null ? null : await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        var sessionId = principal.FindFirstValue("session");
        DateTimeOffset? expiry = null;
        if (user is not null && sessionId is not null)
        {
            var hash = HashSession(sessionId);
            expiry = await db.Sessions.Where(x => x.IdHash == hash && x.UserId == user.Id)
                .Select(x => (DateTimeOffset?)x.ExpiresAtUtc).SingleOrDefaultAsync(cancellationToken);
        }
        return new SessionDto(user is null ? null : ToDto(user), !await db.Users.AnyAsync(cancellationToken), expiry);
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, bool setup, CancellationToken cancellationToken)
    {
        ValidateNewUser(request);
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (setup)
            {
                if (await db.Users.AnyAsync(cancellationToken))
                    throw new AppException("setup_complete", 409, "The owner account already exists.");
            }
            else
            {
                var actor = await db.Users.SingleAsync(x => x.Id == current.Id, cancellationToken);
                if (!actor.IsOwner) throw new AppException("owner_required", 403, "Only the owner can create accounts.");
            }
            var normalized = request.Username.Trim().ToUpperInvariant();
            if (await db.Users.AnyAsync(x => x.NormalizedUsername == normalized, cancellationToken))
                throw new AppException("username_unavailable", 409, "Choose another username.");
            var user = new AppUser
            {
                Username = request.Username.Trim(), NormalizedUsername = normalized,
                DisplayName = request.DisplayName.Trim(), IsOwner = setup, CreatedAtUtc = clock.GetUtcNow()
            };
            user.PasswordHash = passwords.HashPassword(user, request.Password);
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
            await adventures.ProvisionProfilesAsync(user.Id, setup, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToDto(user);
        }
        finally { gate.Semaphore.Release(); }
    }

    public async Task<ClaimsPrincipal> LoginAsync(LoginRequest request, ClaimsPrincipal previous, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 64 || string.IsNullOrEmpty(request.Password) || request.Password.Length > 128)
            throw InvalidCredentials();
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            var normalized = request.Username.Trim().ToUpperInvariant();
            var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedUsername == normalized, cancellationToken);
            if (user is null)
            {
                // Equal-cost work prevents the fast unknown-account path from becoming an enumeration signal.
                passwords.HashPassword(new AppUser(), request.Password);
                throw InvalidCredentials();
            }
            if (user.LockedUntilUtc > clock.GetUtcNow()) throw InvalidCredentials();
            var verification = passwords.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (verification == PasswordVerificationResult.Failed)
            {
                user.FailedLogins++;
                if (user.FailedLogins >= 5) { user.LockedUntilUtc = clock.GetUtcNow().AddMinutes(15); user.FailedLogins = 0; }
                await db.SaveChangesAsync(cancellationToken);
                throw InvalidCredentials();
            }
            if (verification == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = passwords.HashPassword(user, request.Password);
            user.FailedLogins = 0;
            user.LockedUntilUtc = null;
            await RevokeSessionAsync(previous, cancellationToken);
            var principal = NewSession(user);
            await db.SaveChangesAsync(cancellationToken);
            return principal;
        }
        finally { gate.Semaphore.Release(); }
    }

    public async Task<bool> IsSessionValidAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken)
    {
        var sessionId = principal?.FindFirstValue("session");
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (sessionId is null || userId is null) return false;
        var hash = HashSession(sessionId);
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.IdHash == hash && x.UserId == userId, cancellationToken);
        return session is not null && session.ExpiresAtUtc > clock.GetUtcNow();
    }

    public async Task LogoutAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        await gate.Semaphore.WaitAsync(cancellationToken);
        try { await RevokeSessionAsync(principal, cancellationToken); await db.SaveChangesAsync(cancellationToken); }
        finally { gate.Semaphore.Release(); }
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        ValidatePassword(request.NewPassword);
        if (string.IsNullOrEmpty(request.CurrentPassword) || request.CurrentPassword.Length > 128) throw InvalidCredentials();
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var user = await db.Users.SingleAsync(x => x.Id == current.Id, cancellationToken);
            if (passwords.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed) throw InvalidCredentials();
            user.PasswordHash = passwords.HashPassword(user, request.NewPassword);
            await db.Sessions.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { gate.Semaphore.Release(); }
    }

    public async Task<IReadOnlyList<UserDto>> ListUsersAsync(CancellationToken cancellationToken) =>
        (await db.Users.AsNoTracking().OrderBy(x => x.Username).ToListAsync(cancellationToken)).Select(ToDto).ToList();

    // Offline console only: never exposed by an HTTP controller, and never promotes a member.
    public async Task RecoverOwnerAsync(string username, string password, CancellationToken cancellationToken)
    {
        ValidatePassword(password);
        if (string.IsNullOrWhiteSpace(username) || username.Length > 32)
            throw new AppException("owner_not_found", 404, "An existing owner account is required for recovery.");
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var normalized = username.Trim().ToUpperInvariant();
            var owner = await db.Users.SingleOrDefaultAsync(x => x.NormalizedUsername == normalized && x.IsOwner, cancellationToken)
                ?? throw new AppException("owner_not_found", 404, "An existing owner account is required for recovery.");
            owner.PasswordHash = passwords.HashPassword(owner, password);
            owner.FailedLogins = 0;
            owner.LockedUntilUtc = null;
            await db.Sessions.Where(x => x.UserId == owner.Id).ExecuteDeleteAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { gate.Semaphore.Release(); }
    }

    private ClaimsPrincipal NewSession(AppUser user)
    {
        var sessionId = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        db.Sessions.Add(new UserSession { IdHash = HashSession(sessionId), UserId = user.Id, ExpiresAtUtc = clock.GetUtcNow().AddHours(8) });
        return new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.IsOwner ? "Owner" : "Member"), new Claim("session", sessionId)
        ], CookieAuthenticationDefaults.AuthenticationScheme));
    }

    private async Task RevokeSessionAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var id = principal.FindFirstValue("session");
        if (id is null) return;
        var hash = HashSession(id);
        await db.Sessions.Where(x => x.IdHash == hash).ExecuteDeleteAsync(cancellationToken);
    }

    private static void ValidateNewUser(CreateUserRequest request)
    {
        if (request.Username is null || !Regex.IsMatch(request.Username.Trim(), "^[a-zA-Z0-9._-]{3,32}$") ||
            string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length is < 2 or > 60 || request.DisplayName.Any(char.IsControl))
            throw new AppException("invalid_account", 400, "Use a 3–32 character username and a 2–60 character display name.");
        ValidatePassword(request.Password);
    }

    private static void ValidatePassword(string? password)
    {
        if (password is null || password.Length is < 12 or > 128 || password.Any(char.IsControl))
            throw new AppException("invalid_password", 400, "Use a password or passphrase of 12–128 characters.");
    }

    private static string HashSession(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    private static AppException InvalidCredentials() => new("invalid_credentials", 401, "The credentials are invalid or temporarily unavailable.");
    public static UserDto ToDto(AppUser user) => new(user.Id, user.Username, user.DisplayName, user.IsOwner);
}
