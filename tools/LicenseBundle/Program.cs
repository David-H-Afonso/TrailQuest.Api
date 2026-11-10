using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

if (args.Length != 4) throw new ArgumentException("Usage: LicenseBundle <published-app> <public-package-cache> <curated-licenses> <version>");
var publish = Path.GetFullPath(args[0]);
var cache = Path.GetFullPath(args[1]);
var curated = Path.GetFullPath(args[2]);
var version = args[3];
var output = Path.Combine(publish, "licenses");
Directory.CreateDirectory(output);
using var dependencies = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(publish, "TrailQuest.Api.deps.json")));
var packages = new List<object>();
var components = new List<object>();
foreach (var entry in dependencies.RootElement.GetProperty("libraries").EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
{
    if (entry.Value.GetProperty("type").GetString() != "package") continue;
    var identity = entry.Name.Split('/');
    if (identity.Length != 2 || identity.Any(x => !Regex.IsMatch(x, "^[A-Za-z0-9_.+-]+$") || x.Contains("..", StringComparison.Ordinal)))
        throw new InvalidOperationException("Unsafe package identity in publish manifest.");
    var id = identity[0]; var packageVersion = identity[1];
    var directory = Path.Combine(cache, id.ToLowerInvariant(), packageVersion.ToLowerInvariant());
    var specification = Directory.GetFiles(directory, "*.nuspec", SearchOption.TopDirectoryOnly).Single();
    using var reader = XmlReader.Create(specification, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
    var metadata = XDocument.Load(reader).Descendants().Single(x => x.Name.LocalName == "metadata");
    string Field(string name) => metadata.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value ?? "";
    var license = metadata.Elements().FirstOrDefault(x => x.Name.LocalName == "license")
        ?? throw new InvalidOperationException($"Missing declared license: {id}.");
    var expression = (string?)license.Attribute("type") == "expression" ? license.Value : null;
    var declared = expression ?? $"file:{license.Value}";
    var target = Path.Combine(output, "nuget", id, packageVersion);
    Directory.CreateDirectory(target);
    var texts = new List<object>();
    async Task Retain(string file, string relative)
    {
        var bytes = await File.ReadAllBytesAsync(file);
        if (bytes.Length > 2 * 1024 * 1024) throw new InvalidOperationException($"Unexpectedly large license: {id}.");
        var destination = Path.Combine(target, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, bytes);
        texts.Add(new { file = Path.GetRelativePath(output, destination).Replace('\\', '/'), sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
    }
    foreach (var file in Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, AttributesToSkip = FileAttributes.ReparsePoint }).Order(StringComparer.Ordinal))
        if (Regex.IsMatch(Path.GetFileName(file), "^(LICENSE|LICENCE|COPYING|NOTICE|OFL|THIRD.?PARTY.?NOTICES)([._-].*)?$", RegexOptions.IgnoreCase))
            await Retain(file, Path.GetRelativePath(directory, file));
    if (expression is "MIT" && !texts.Any()) await Retain(Path.Combine(curated, "dotnet-MIT.txt"), "UPSTREAM-MIT.txt");
    if (id.StartsWith("SQLitePCLRaw.", StringComparison.OrdinalIgnoreCase))
    {
        await Retain(Path.Combine(curated, "SQLitePCLRaw-LICENSE.txt"), "UPSTREAM-LICENSE.txt");
        await Retain(Path.Combine(curated, "SQLitePCLRaw-NOTICE.txt"), "UPSTREAM-NOTICE.txt");
    }
    if (texts.Count == 0) throw new InvalidOperationException($"No retained full license text for {id}@{packageVersion}.");
    var source = (string?)metadata.Elements().FirstOrDefault(x => x.Name.LocalName == "repository")?.Attribute("url") ?? Field("projectUrl");
    if (source.Length > 0 && (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0)) source = "";
    var notice = $"{id} {packageVersion}\nAuthors: {Field("authors")}\nCopyright: {Field("copyright")}\nDeclared license: {declared}\nSource: {source}\nThe dependency's own terms apply; the project's MIT license does not relicense it.\n";
    await File.WriteAllTextAsync(Path.Combine(target, "PACKAGE-NOTICE.txt"), notice);
    packages.Add(new { name = id, version = packageVersion, declaredLicense = declared, authors = Field("authors"), copyright = Field("copyright"), source, texts });
    var purl = $"pkg:nuget/{Uri.EscapeDataString(id)}@{Uri.EscapeDataString(packageVersion)}";
    object[] licenses = expression is null ? [new { license = new { name = id == "Garmin.FIT.Sdk" ? "Garmin FIT Protocol License" : declared } }] : [new { expression }];
    components.Add(new Dictionary<string, object> { ["type"] = "library", ["name"] = id, ["version"] = packageVersion, ["bom-ref"] = purl, ["purl"] = purl, ["licenses"] = licenses });
}
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
await File.WriteAllTextAsync(Path.Combine(output, "index.json"), JsonSerializer.Serialize(new
{
    project = "TrailQuest API", version, projectLicense = "MIT", dependencyLicensesAreSeparate = true,
    scope = "Packages in the published deps manifest; excludes the base OS/framework inventory", packages
}, options) + "\n");
await File.WriteAllTextAsync(Path.Combine(output, "bom.cdx.json"), JsonSerializer.Serialize(new
{
    bomFormat = "CycloneDX", specVersion = "1.6", version = 1,
    metadata = new { component = new { type = "application", name = "TrailQuest API", version } }, components
}, options) + "\n");
Console.WriteLine($"Retained license/notice texts for {packages.Count} published NuGet packages.");
