using System.Net;

namespace TrailQuest.Api.Infrastructure.Security;

// Preparing hosting is not enabling it: the default remains loopback-only.
public sealed class HostingPolicy
{
    public bool AllowRemote { get; }
    public Uri? PublicOrigin { get; }
    public IReadOnlyList<IPAddress> KnownProxies { get; }
    public IReadOnlySet<string> AllowedOrigins { get; }

    public HostingPolicy(IConfiguration configuration)
    {
        AllowRemote = configuration.GetValue("Hosting:AllowRemote", false);
        KnownProxies = (configuration.GetSection("Hosting:KnownProxies").Get<string[]>() ?? []).Select(value =>
            IPAddress.TryParse(value, out var ip) ? ip : throw new InvalidOperationException("Hosting:KnownProxies must contain explicit IP addresses.")).ToList();
        if (AllowRemote)
        {
            if (!Uri.TryCreate(configuration["Hosting:PublicOrigin"], UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
                origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0 || origin.Host.Contains('*'))
                throw new InvalidOperationException("Remote hosting requires one explicit HTTPS Hosting:PublicOrigin, with no path, credentials or wildcard.");
            PublicOrigin = origin;
            AllowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { origin.GetLeftPart(UriPartial.Authority) };
        }
        else
        {
            var webPort = configuration.GetValue("Hosting:LocalWebPort", 5186);
            var apiPort = configuration.GetValue("Hosting:LocalApiPort", 5187);
            if (webPort is < 1024 or > 65535) throw new InvalidOperationException("Hosting:LocalWebPort must be a valid unprivileged loopback port.");
            if (apiPort is < 1024 or > 65535) throw new InvalidOperationException("Hosting:LocalApiPort must be a valid unprivileged loopback port.");
            AllowedOrigins = new HashSet<string>(StringComparer.Ordinal)
            { $"http://127.0.0.1:{webPort}", $"http://localhost:{webPort}", $"http://127.0.0.1:{apiPort}", $"http://localhost:{apiPort}" };
        }
    }
}
