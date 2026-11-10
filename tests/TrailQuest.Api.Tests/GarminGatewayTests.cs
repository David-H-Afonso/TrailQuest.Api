using System.Net;
using System.Text;
using System.Text.Json;
using TrailQuest.Api.Application;
using TrailQuest.Api.Infrastructure.Garmin;

namespace TrailQuest.Api.Tests;

public sealed class GarminGatewayTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    [Theory]
    [InlineData("{}")]
    [InlineData("[{\"calendarDate\":\"2026-11-09\",\"totalSteps\":-1}]")]
    [InlineData("[{\"calendarDate\":\"2026-11-10\",\"totalSteps\":100}]")]
    [InlineData("[{\"calendarDate\":\"2026-11-09\",\"totalSteps\":100},{\"calendarDate\":\"2026-11-09\",\"totalSteps\":100}]")]
    [InlineData("[{\"calendarDate\":\"2026-11-09\",\"totalDistance\":100}]")]
    [InlineData("[{\"calendarDate\":\"2026-11-09\",\"totalSteps\":null}]")]
    [InlineData("[{\"calendarDate\":\"2026-11-09\",\"totalSteps\":\"1000\"}]")]
    [InlineData("[{\"calendarDate\":\"2026-11-09\",\"totalSteps\":1000,\"totalDistance\":\"700\"}]")]
    [InlineData("[{\"calendarDate\":\"2026-11-09\",\"totalSteps\":100,\"totalDistance\":-1}]")]
    public void Invalid_daily_payloads_are_rejected_instead_of_becoming_zeroes(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        var error = Assert.Throws<AppException>(() => GarminGateway.ParseDays(json.RootElement, Today, Today));
        Assert.Equal("garmin_response", error.Code);
    }

    [Fact]
    public void Garmin_reported_distance_is_in_meters_and_missing_distance_is_explicit()
    {
        using var json = JsonDocument.Parse("[{\"calendarDate\":\"2026-11-09\",\"totalSteps\":1234,\"totalDistance\":890}]");
        var day = Assert.Single(GarminGateway.ParseDays(json.RootElement, Today, Today));
        Assert.Equal(890, day.DistanceMeters);
        using var noDistance = JsonDocument.Parse("[{\"calendarDate\":\"2026-11-09\",\"totalSteps\":1234,\"totalDistance\":0}]");
        Assert.Null(Assert.Single(GarminGateway.ParseDays(noDistance.RootElement, Today, Today)).DistanceMeters);
    }

    [Fact]
    public async Task Official_plain_text_token_is_cached_only_in_memory()
    {
        var tokenRequests = 0;
        var dataRequests = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                tokenRequests++;
                Assert.Equal("ApiKey", request.Headers.Authorization?.Scheme);
                return TextResponse(Token());
            }
            dataRequests++;
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.EndsWith("/usersummary-service/stats/steps/daily/2026-11-09/2026-11-09", request.RequestUri!.AbsoluteUri);
            return TextResponse("[{\"calendarDate\":\"2026-11-09\",\"totalSteps\":1000,\"totalDistance\":750}]");
        });
        var gateway = Gateway(handler);
        await gateway.GetDaysAsync("fixture-key", "fixture-user-a", Today, Today, default);
        await gateway.GetDaysAsync("fixture-key", "fixture-user-a", Today, Today, default);
        Assert.Equal(1, tokenRequests);
        Assert.Equal(2, dataRequests);
    }

    [Fact]
    public async Task A_revoked_access_token_gets_exactly_one_refresh_attempt()
    {
        var tokens = 0;
        var gets = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post) { tokens++; return TextResponse(Token()); }
            gets++;
            return gets == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : TextResponse("[]");
        });
        Assert.Empty(await Gateway(handler).GetDaysAsync("fixture-key", "fixture-user-a", Today, Today, default));
        Assert.Equal(2, tokens);
        Assert.Equal(2, gets);
    }

    [Fact]
    public async Task Upstream_error_bodies_are_never_exposed()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("unsafe-upstream-body")
        });
        var error = await Assert.ThrowsAsync<AppException>(() => Gateway(handler).GetDaysAsync("fixture-key", "fixture-user-a", Today, Today, default));
        Assert.Equal("garmin_auth", error.Code);
        Assert.DoesNotContain("unsafe-upstream-body", error.Message);
    }

    private static GarminGateway Gateway(HttpMessageHandler handler) => new(
        new StubClientFactory(handler), new FixedClock());

    [Fact]
    public async Task Bearer_tokens_are_never_shared_between_user_connection_scopes()
    {
        var issued = 0;
        using var handler = new StubHandler(request =>
        {
            if (request.Method == HttpMethod.Post) { issued++; return TextResponse(Token()); }
            return TextResponse("[]");
        });
        var gateway = Gateway(handler);
        await gateway.GetDaysAsync("fixture-key-a", "user-a:version-one", Today, Today, default);
        await gateway.GetDaysAsync("fixture-key-b", "user-b:version-one", Today, Today, default);
        await gateway.GetDaysAsync("fixture-key-a", "user-a:version-one", Today, Today, default);
        Assert.Equal(2, issued);
        gateway.Invalidate("user-a:version-one");
        await gateway.GetDaysAsync("fixture-key-a", "user-a:version-one", Today, Today, default);
        Assert.Equal(3, issued);
    }

    private static HttpResponseMessage TextResponse(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static string Token()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { exp = new FixedClock().GetUtcNow().AddDays(1).ToUnixTimeSeconds() })))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"fixture.{payload}.signature";
    }

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri("https://personal.connectapi.garmin.com") };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
