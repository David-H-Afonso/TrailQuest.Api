using Microsoft.AspNetCore.Authentication.Cookies;

namespace TrailQuest.Api.Infrastructure.Security;

// Select the cookie name per request without changing the authentication/key purpose.
public sealed class TransportCookieManager : ICookieManager
{
    private readonly ChunkingCookieManager cookies = new();

    public string? GetRequestCookie(HttpContext context, string key) =>
        cookies.GetRequestCookie(context, Name(context, key));

    public void AppendResponseCookie(HttpContext context, string key, string? value, CookieOptions options) =>
        cookies.AppendResponseCookie(context, Name(context, key), value, options);

    public void DeleteCookie(HttpContext context, string key, CookieOptions options) =>
        cookies.DeleteCookie(context, Name(context, key), options);

    private static string Name(HttpContext context, string key) =>
        context.Request.IsHttps ? key : "trailquest.lan.session";
}
