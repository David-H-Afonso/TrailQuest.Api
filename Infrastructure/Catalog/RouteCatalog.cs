using System.Text.Json;
using System.Text.RegularExpressions;
using TrailQuest.Api.Contracts;

namespace TrailQuest.Api.Infrastructure.Catalog;

public sealed class RouteCatalog
{
    public IReadOnlyList<AdventureRouteDto> Routes { get; }
    public AdventureRouteDto Default => Routes.FirstOrDefault(x => x.IsDefault) ?? Routes[0];
    private readonly string root;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public RouteCatalog(IWebHostEnvironment environment)
    {
        root = Path.Combine(environment.ContentRootPath, "Content", "Adventures");
        Routes = Directory.GetFiles(root, "adventure.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Select(path => JsonSerializer.Deserialize<AdventureRouteDto>(File.ReadAllText(path), Json)
                ?? throw new InvalidOperationException($"Invalid adventure: {Path.GetFileName(Path.GetDirectoryName(path))}"))
            .ToList();
        Validate(Routes);
        foreach (var route in Routes)
            foreach (var file in route.Gallery.Concat(route.Checkpoints.SelectMany(x => x.Gallery)).Select(x => x.File)
                .Concat(route.Checkpoints.Select(x => x.Image).OfType<string>()).Concat(new[] { route.Cover, route.Map.Artwork }.OfType<string>()))
                if (!File.Exists(Path.Combine(root, route.Id, "assets", file))) throw new InvalidOperationException($"Missing asset in pack {route.Id}: {file}");
    }

    public static void Validate(IReadOnlyList<AdventureRouteDto> routes)
    {
        if (routes.Count == 0 || routes.Select(x => x.Id).Distinct().Count() != routes.Count || routes.Count(x => x.IsDefault) > 1)
            throw new InvalidOperationException("Adventure IDs must be unique and the catalog must not be empty.");
        foreach (var route in routes)
        {
            if (!Regex.IsMatch(route.Id, "^[a-z0-9][a-z0-9-]{0,63}$") || route.SchemaVersion != 1 ||
                !Version.TryParse(route.ContentVersion, out _) || route.Setting is not ("real" or "fiction") ||
                route.Map.Kind is not ("geographic" or "illustrated" or "schematic") ||
                !double.IsFinite(route.DistanceKm) || route.DistanceKm <= 0 || route.Checkpoints.Count < 2 || route.Coordinates.Length < 2 ||
                route.Checkpoints.Select(x => x.Id).Distinct().Count() != route.Checkpoints.Count ||
                route.Checkpoints.First().DistanceKm != 0 || Math.Abs(route.Checkpoints.Last().DistanceKm - route.DistanceKm) > 0.00001 ||
                !route.Checkpoints.Select(x => x.DistanceKm).SequenceEqual(route.Checkpoints.Select(x => x.DistanceKm).Order()) ||
                route.Checkpoints.Any(x => !Regex.IsMatch(x.Id, "^[a-z0-9-]+$") || x.DistanceKm < 0 || x.DistanceKm > route.DistanceKm || x.Position.Length != 2 || x.Position.Any(v => !double.IsFinite(v))) ||
                route.Coordinates.Any(x => x.Length != 2 || x.Any(v => !double.IsFinite(v))) ||
                !Regex.IsMatch(route.Passport.Color, "^#[0-9a-fA-F]{6}$"))
                throw new InvalidOperationException($"Invalid content pack: {route.Id}");
            if (route.References.Any(x => !Uri.TryCreate(x.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0))
                throw new InvalidOperationException($"Content references must be public HTTPS links: {route.Id}");
            if (route.Gallery.Concat(route.Checkpoints.SelectMany(x => x.Gallery)).SelectMany(x => new[] { x.SourceUrl, x.LicenseUrl }).OfType<string>()
                .Any(x => !Uri.TryCreate(x, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0))
                throw new InvalidOperationException($"Media credits must be public HTTPS links: {route.Id}");
            foreach (var file in route.Gallery.Concat(route.Checkpoints.SelectMany(x => x.Gallery)).Select(x => x.File)
                .Concat(route.Checkpoints.Select(x => x.Image).OfType<string>()).Concat(new[] { route.Cover, route.Map.Artwork }.OfType<string>()))
                if (!Regex.IsMatch(file, "^[a-zA-Z0-9_-]+\\.(svg|webp|jpg|jpeg|png)$"))
                    throw new InvalidOperationException($"Unsafe content asset: {route.Id}");
        }
    }

    public (string Path, string ContentType) Asset(string id, string file)
    {
        Get(id);
        // Historical snapshots may reference an older, retained filename in this pack's assets directory.
        if (!Regex.IsMatch(file, "^[a-zA-Z0-9_-]+\\.(svg|webp|jpg|jpeg|png)$")) throw new Application.AppException("asset_not_found", 404, "This asset does not exist.");
        var path = Path.Combine(root, id, "assets", file);
        if (!File.Exists(path)) throw new Application.AppException("asset_not_found", 404, "This asset does not exist.");
        return (path, Path.GetExtension(file).ToLowerInvariant() switch { ".svg" => "image/svg+xml", ".webp" => "image/webp", ".png" => "image/png", _ => "image/jpeg" });
    }

    public static AdventureRouteDto Visible(AdventureRouteDto route, IReadOnlySet<string>? unlocked = null) => route with
    {
        Checkpoints = route.Checkpoints.Select(x => unlocked?.Contains(x.Id) == true ? x : x with { Description = "", Fact = "", Story = [], Gallery = [] }).ToList()
    };

    public static AdventureRouteDto ForJourney(Domain.Entities.Journey journey) =>
        JsonSerializer.Deserialize<AdventureRouteDto>(journey.DefinitionJson, Json)
        ?? throw new InvalidOperationException("The saved adventure definition is missing.");

    public AdventureRouteDto Get(string id) => Routes.FirstOrDefault(x => x.Id == id)
        ?? throw new Application.AppException("route_not_found", 404, "The requested route does not exist.");
}
