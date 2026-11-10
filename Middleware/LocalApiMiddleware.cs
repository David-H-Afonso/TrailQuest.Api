using System.Net;
using Microsoft.AspNetCore.Mvc;
using TrailQuest.Api.Application;
using TrailQuest.Api.Infrastructure.Security;
using TrailQuest.Api.Infrastructure.Operations;

namespace TrailQuest.Api.Middleware;

public sealed class LocalApiMiddleware(RequestDelegate next, ILogger<LocalApiMiddleware> logger, HostingPolicy hosting)
{

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://tile.openstreetmap.org; font-src 'self'; connect-src 'self' https://tile.openstreetmap.org; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
        context.Response.Headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
        try
        {
            // These fixed, nonsensitive status routes are also usable by an internal HTTP probe.
            if (HealthProbe.IsStatusPath(context.Request.Path) && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
            {
                await next(context);
                return;
            }
            var remote = context.Connection.RemoteIpAddress;
            if (!hosting.AllowRemote && remote is not null && !IPAddress.IsLoopback(remote))
                throw new AppException("local_only", 403, "Remote access is disabled for this installation.");
            if (hosting.AllowRemote && (!context.Request.IsHttps ||
                !string.Equals(context.Request.Host.Host, hosting.PublicOrigin!.Host, StringComparison.OrdinalIgnoreCase) ||
                (context.Request.Host.Port ?? 443) != hosting.PublicOrigin.Port))
                throw new AppException("secure_origin_required", 403, "Use the configured HTTPS origin. Forwarded headers are accepted only from configured proxies.");
            var origin = context.Request.Headers.Origin.ToString();
            if ((origin.Length > 0 && !hosting.AllowedOrigins.Contains(origin)) || context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
                throw new AppException("origin_rejected", 403, "Requests from another website are not allowed.");
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) &&
                context.Request.Headers["X-TrailQuest-Client"] != "local-web")
                throw new AppException("client_header_required", 403, "The local client header is required for changes.");
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (context.Response.HasStarted) throw;
            var appError = error as AppException;
            var status = appError?.StatusCode ?? (error is BadHttpRequestException ? 400 : 500);
            if (status == 500) logger.LogError(error, "An unexpected local API error occurred.");
            context.Response.StatusCode = status;
            var problem = new ProblemDetails
            {
                Status = status, Title = appError?.Message ?? (status == 400 ? "The request is invalid." : "An unexpected local API error occurred."),
                Type = "about:blank"
            };
            problem.Extensions["code"] = appError?.Code ?? (status == 400 ? "validation" : "internal_error");
            await context.Response.WriteAsJsonAsync(problem, options: (System.Text.Json.JsonSerializerOptions?)null,
                contentType: "application/problem+json", cancellationToken: context.RequestAborted);
        }
    }
}
