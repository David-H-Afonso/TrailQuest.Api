using System.Net;

namespace TrailQuest.Api.Infrastructure.Security;

// Preparing hosting is not enabling it: the default remains loopback-only.
public sealed class HostingPolicy
{
    public bool AllowRemote { get; }
    public Uri? PublicOrigin { get; }
    public bool LanHttpEnabled { get; }
    public IReadOnlyList<IPAddress> KnownProxies { get; }
    public IReadOnlyList<IPNetwork> KnownNetworks { get; }
    public bool HasTrustedProxies => KnownProxies.Count > 0 || KnownNetworks.Count > 0;
    public IReadOnlySet<string> AllowedOrigins { get; }

    public HostingPolicy(IConfiguration configuration)
    {
        AllowRemote = configuration.GetValue("Hosting:AllowRemote", false);
        KnownProxies = (configuration.GetSection("Hosting:KnownProxies").Get<string[]>() ?? []).Select(value =>
            IPAddress.TryParse(value, out var ip) ? ip : throw new InvalidOperationException("Hosting:KnownProxies must contain explicit IP addresses.")).ToList();
        KnownNetworks = (configuration.GetSection("Hosting:KnownNetworks").Get<string[]>() ?? []).Select(value =>
        {
            if (!IPNetwork.TryParse(value, out var network) || !IsPrivateAddress(network.BaseAddress) ||
                network.PrefixLength < (network.BaseAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 24 : 64))
                throw new InvalidOperationException("Hosting:KnownNetworks must contain explicit private CIDRs, /24 or narrower for IPv4 and /64 or narrower for IPv6.");
            return network;
        }).ToList();
        if (AllowRemote)
        {
            if (!Uri.TryCreate(configuration["Hosting:PublicOrigin"], UriKind.Absolute, out var origin) ||
                origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0 || origin.Host.Contains('*'))
                throw new InvalidOperationException("Remote hosting requires one explicit Hosting:PublicOrigin, with no path, credentials or wildcard.");
            LanHttpEnabled = origin.Scheme == "http" && configuration.GetValue("Hosting:AllowLanHttp", false) &&
                IPAddress.TryParse(origin.DnsSafeHost, out var hostAddress) && IsPrivateAddress(hostAddress);
            if (origin.Scheme != "https" && !LanHttpEnabled)
                throw new InvalidOperationException("HTTPS is required by default. LAN HTTP requires Hosting:AllowLanHttp=true and an explicit private IP origin.");
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

    public static bool IsLanClient(IPAddress? address) => address is not null &&
        (IPAddress.IsLoopback(address) || IsPrivateAddress(address));

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168);
        return (bytes[0] & 0xfe) == 0xfc;
    }
}
