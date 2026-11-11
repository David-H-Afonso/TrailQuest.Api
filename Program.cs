using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using TrailQuest.Api.Application;
using TrailQuest.Api.Infrastructure.Catalog;
using TrailQuest.Api.Infrastructure.Garmin;
using TrailQuest.Api.Infrastructure.Persistence;
using TrailQuest.Api.Infrastructure.Security;
using TrailQuest.Api.Infrastructure.Operations;
using TrailQuest.Api.Domain.Entities;
using TrailQuest.Api.Middleware;

if (args.Length == 1 && args[0] == "--version")
{
    Console.WriteLine(typeof(Program).Assembly.GetName().Version!.ToString(3));
    return;
}
if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine("""
        TrailQuest API
        --version                         Print the release version without opening storage.
        --Health:Probe=true               Read-only loopback readiness probe; no hosting or migration.
        --Backup:Destination=<new-path>    Coherent private DB-only backup; no key-store access.
        --Account:Action=create-owner     Offline, empty-account-store bootstrap.
        --Account:Action=recover-owner    Offline password recovery for an existing owner only.
        --Account:Username=<chosen-name>  Required for account maintenance; never supplied by the image.
        --Account:DisplayName=<name>      Optional display name for create-owner.
        --Account:PasswordInput=stdin     Explicit bounded stdin input for managed secrets/tests.
        Passwords are otherwise entered twice in a hidden private console. Never pass them in argv/environment.
        Stop the application before account maintenance. Data:Directory and Security:KeyDirectory select storage.
        """);
    return;
}
var builder = WebApplication.CreateBuilder(args);
// Probes must not migrate, create directories/keys, run jobs or open another listener.
if (builder.Configuration.GetValue("Health:Probe", false))
{
    Environment.ExitCode = await HealthProbe.RunAsync(builder.Configuration);
    return;
}
AccountCommand? accountCommand;
string? maintenancePassword = null;
try
{
    accountCommand = AccountCommand.Parse(builder.Configuration);
    if (accountCommand is not null) maintenancePassword = await accountCommand.ReadPasswordAsync();
}
catch (InvalidOperationException error)
{
    Console.Error.WriteLine(error.Message);
    Environment.ExitCode = 2;
    return;
}
builder.Services.AddSingleton<HostingPolicy>();
builder.Services.AddHsts(options => { options.IncludeSubDomains = false; options.Preload = false; });
builder.Services.AddOptions<HostFilteringOptions>().PostConfigure<HostingPolicy>((options, policy) =>
{
    if (policy.AllowRemote) options.AllowedHosts = policy.AllowedHosts.ToArray();
});
builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<HostingPolicy>((options, policy) =>
{
    if (policy.AllowRemote && policy.HasTrustedProxies)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = 1;
        options.KnownIPNetworks.Clear(); options.KnownProxies.Clear();
        foreach (var address in policy.KnownProxies) options.KnownProxies.Add(address);
        foreach (var network in policy.KnownNetworks) options.KnownIPNetworks.Add(network);
        foreach (var host in policy.AllowedHosts) options.AllowedHosts.Add(host);
    }
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
var dataDirectory = Path.GetFullPath(builder.Configuration["Data:Directory"] ?? "data", builder.Environment.ContentRootPath);
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = Path.Combine(dataDirectory, "trailquest.db"), ForeignKeys = true, DefaultTimeout = 15
}.ToString();

// Explicit, human-invoked coherent backup; exits before migrations or security-key access.
if (builder.Configuration["Backup:Destination"] is { Length: > 0 } backupDestination)
{
    var destination = Path.GetFullPath(backupDestination);
    if (File.Exists(destination)) throw new InvalidOperationException("The backup destination already exists. No file was overwritten.");
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    var readOnly = new SqliteConnectionStringBuilder(connectionString) { Mode = SqliteOpenMode.ReadOnly, Pooling = false };
    await using var source = new SqliteConnection(readOnly.ToString());
    await source.OpenAsync();
    await using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
    await target.OpenAsync();
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    source.BackupDatabase(target);
    await using var check = target.CreateCommand(); check.CommandText = "PRAGMA integrity_check;";
    if (!Equals(await check.ExecuteScalarAsync(), "ok")) throw new InvalidOperationException("Backup integrity verification failed. Do not use this snapshot.");
    Console.WriteLine("Coherent private SQLite backup created; integrity_check passed. No protection keys were copied.");
    return;
}

builder.Services.AddDbContext<TrailQuestDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var environment = services.GetRequiredService<IWebHostEnvironment>();
    var directory = Path.GetFullPath(configuration["Data:Directory"] ?? "data", environment.ContentRootPath);
    options.UseSqlite(new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(directory, "trailquest.db"), ForeignKeys = true, DefaultTimeout = 15
    }.ToString());
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LocalMutationGate>();
builder.Services.AddSingleton<RouteCatalog>();
builder.Services.AddScoped<AdventureService>();
builder.Services.AddSingleton<DemoData>();
builder.Services.AddScoped<HealthService>();
builder.Services.AddScoped<GarminSyncService>();
builder.Services.AddScoped<CommunityService>();
builder.Services.AddSingleton<SyncCoordinator>();
builder.Services.AddSingleton<ExportCoordinator>();
builder.Services.AddHostedService<GarminSyncWorker>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<GarminConnectionService>();
builder.Services.AddScoped<IGarminGateway>(services => services.GetRequiredService<GarminConnectionService>());
builder.Services.AddSingleton<IGarminClient, GarminGateway>();
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210000);
builder.Services.AddSingleton(services => new SecurityStorage(services.GetRequiredService<IConfiguration>()["Security:KeyDirectory"] ??
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "trailquest", "security")));
builder.Services.AddDataProtection().SetApplicationName("TrailQuest.Local");
builder.Services.AddOptions<KeyManagementOptions>().Configure<SecurityStorage, ILoggerFactory>((options, storage, logs) =>
{
    options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(storage.KeyDirectory), logs);
    if (OperatingSystem.IsWindows()) options.XmlEncryptor = new DpapiXmlEncryptor(false, logs);
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "trailquest.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.Path = "/";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = 401;
        return context.Response.WriteAsJsonAsync(new { code = "authentication_required", title = "Sign in to access your data.", status = 401 });
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = 403;
        return context.Response.WriteAsJsonAsync(new { code = "owner_required", title = "This action requires the owner account.", status = 403 });
    };
    options.Events.OnValidatePrincipal = async context =>
    {
        var accounts = context.HttpContext.RequestServices.GetRequiredService<AccountService>();
        if (!await accounts.IsSessionValidAsync(context.Principal, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    };
}).AddScheme<AuthenticationSchemeOptions, IntegrationAuthentication>(IntegrationAuthentication.SchemeName, _ => { });
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<TimeProvider, HostingPolicy>((options, clock, policy) =>
    {
        options.TimeProvider = clock;
        options.Cookie.SecurePolicy = policy.AllowRemote && !policy.LanHttpEnabled ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        if (policy.LanHttpEnabled) options.CookieManager = new TransportCookieManager();
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.OnRejected = (context, cancellationToken) => new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
        new { code = "rate_limited", title = "Too many requests. Try again later.", status = 429 }, cancellationToken));
    foreach (var (name, permits, minutes) in new[] { ("login", 10, 1), ("accounts", 12, 60), ("garmin", 4, 1), ("garmin-credentials", 3, 1), ("provider", 60, 1), ("community", 30, 1), ("export", 4, 1) })
        options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(minutes), QueueLimit = 0 }));
});
builder.Services.AddHttpClient("garmin", client =>
{
    client.BaseAddress = new Uri("https://personal.connectapi.garmin.com");
    client.Timeout = TimeSpan.FromSeconds(20);
    client.MaxResponseContentBufferSize = 512 * 1024;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false }).RemoveAllLoggers();
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = _ => new BadRequestObjectResult(new ProblemDetails
    {
        Status = 400, Title = "The request is invalid.", Extensions = { ["code"] = "validation" }
    });
});

var app = builder.Build();
// Resolve final host/test configuration before touching a data path or taking its lock.
dataDirectory = Path.GetFullPath(app.Configuration["Data:Directory"] ?? "data", app.Environment.ContentRootPath);
Directory.CreateDirectory(dataDirectory);
if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(dataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
using var instance = ApplicationInstance.Acquire(dataDirectory);
app.Lifetime.ApplicationStopped.Register(instance.Dispose);
if (accountCommand is not null)
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>();
        await db.Database.MigrateAsync();
        if (accountCommand.Action == "create-owner")
        {
            if (await db.Users.AnyAsync()) throw new AppException("setup_complete", 409, "The owner account already exists. Nothing was reset.");
            await scope.ServiceProvider.GetRequiredService<AdventureService>().InitializeAsync();
        }
        await accountCommand.ExecuteAsync(scope.ServiceProvider, maintenancePassword!);
        Console.WriteLine(accountCommand.Action == "create-owner" ? "Owner created. No default credentials were stored in the image." : "Owner password recovered; previous sessions revoked. Activity was preserved.");
    }
    catch (Exception error) when (error is AppException or InvalidOperationException)
    {
        Console.Error.WriteLine(error.Message);
        Environment.ExitCode = 2;
    }
    finally { maintenancePassword = null; await app.DisposeAsync(); }
    return;
}
var hosting = app.Services.GetRequiredService<HostingPolicy>();
if (hosting.AllowRemote && hosting.HasTrustedProxies) app.UseForwardedHeaders();
if (hosting.AllowRemote) app.UseHsts();
app.UseMiddleware<LocalApiMiddleware>();
var licensesDirectory = Path.Combine(app.Environment.ContentRootPath, "licenses");
if (Directory.Exists(licensesDirectory))
{
    var licenses = new PhysicalFileProvider(licensesDirectory);
    app.Lifetime.ApplicationStopped.Register(licenses.Dispose);
    app.UseStaticFiles(new StaticFileOptions { FileProvider = licenses, RequestPath = "/api/v1/licenses" });
}
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Json(new { status = "ok" }));
app.MapGet("/health/ready", async (TrailQuestDbContext db, CancellationToken ct) =>
{
    try { return await db.Database.CanConnectAsync(ct) ? Results.Json(new { status = "ok" }) : Results.StatusCode(503); }
    catch (Exception error) when (error is SqliteException or InvalidOperationException) { return Results.StatusCode(503); }
});
app.MapGet("/api/v1/health", () => new { status = "ok", version = typeof(Program).Assembly.GetName().Version!.ToString(3), localOnly = !hosting.AllowRemote });
app.MapGet("/api/v1/routes", (RouteCatalog catalog) => catalog.Routes.Select(x => RouteCatalog.Visible(x)));
app.MapGet("/api/v1/content/{id}/{file}", (string id, string file, RouteCatalog catalog, HttpContext context) =>
{
    var asset = catalog.Asset(id, file);
    context.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'";
    return Results.File(asset.Path, asset.ContentType);
});
app.MapGet("/api/v1/demo/status", (DemoData demo) => new { available = demo.Available });
app.MapControllers();
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: 404, title: "This API endpoint does not exist."));

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>();
    await db.Database.MigrateAsync();
    if (hosting.AllowRemote && !await db.Users.AnyAsync())
        throw new InvalidOperationException("Create the owner account in local-only mode before enabling remote hosting. Remote first-owner setup is disabled.");
    await scope.ServiceProvider.GetRequiredService<AdventureService>().InitializeAsync();
}

await app.RunAsync();

public partial class Program;
