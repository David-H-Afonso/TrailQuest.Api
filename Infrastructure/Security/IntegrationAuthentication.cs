using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Infrastructure.Security;

public sealed class IntegrationAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, TrailQuestDbContext db, TimeProvider clock) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "integration";
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal)) return AuthenticateResult.NoResult();
        var token = authorization[7..];
        if (token.Length is < 32 or > 100) return AuthenticateResult.Fail("Invalid integration credential.");
        var hash = Hash(token);
        var grant = await db.IntegrationGrants.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash, Context.RequestAborted);
        if (grant is null || grant.ExpiresAtUtc <= clock.GetUtcNow()) return AuthenticateResult.Fail("Invalid integration credential.");
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, grant.UserId), new("client_id", grant.Id) };
        claims.AddRange((JsonSerializer.Deserialize<string[]>(grant.ScopesJson) ?? []).Select(x => new Claim("scope", x)));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Response.WriteAsJsonAsync(new { code = "integration_authentication_required", title = "A valid application grant is required.", status = 401 });
    }
}
