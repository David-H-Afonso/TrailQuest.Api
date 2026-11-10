using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Infrastructure.Catalog;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Tests;

public sealed class EditorialAndPreviewTests
{
    [Fact]
    public async Task Demo_stamp_preview_works_without_publishing_to_real_friendships()
    {
        using var app=new LocalAppFactory();using var client=app.CreateLocalClient();
        var request=new {kind="checkpoint",sourceId="tokyo",checkpointId="shinjuku",profileId="demo",includeTrack=false};
        var response=await client.PostAsJsonAsync("/api/v1/community/preview",request);response.EnsureSuccessStatusCode();
        var preview=(await response.Content.ReadFromJsonAsync<PostDto>())!;
        Assert.Equal("Shinjuku",preview.Title);Assert.Contains("Sello de salida",preview.Summary);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/v1/community/posts",request)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<CommunityDto>("/api/v1/community?profile=personal"))!.Posts);
        Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsJsonAsync("/api/v1/community/preview",new {kind="checkpoint",sourceId="tokyo",checkpointId="shinjuku",profileId="foreign-profile"})).StatusCode);
    }

    [Fact]
    public async Task Updating_editorial_content_preserves_history_start_dates_and_earned_stamps()
    {
        using var app=new LocalAppFactory();using var client=app.CreateLocalClient();
        (await client.PutAsJsonAsync("/api/v1/profiles/personal/days/2026-11-09",new {steps=20000})).EnsureSuccessStatusCode();
        var before=(await client.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard"))!;
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>();
            var profile=await db.Profiles.SingleAsync(x=>x.Kind=="personal"&&x.UserId!=null);
            var journey=await db.Journeys.SingleAsync(x=>x.ProfileId==profile.Id&&x.RouteId=="tokyo");
            var old=RouteCatalog.ForJourney(journey) with {ContentVersion="1.0.0",Description="Previous editorial copy"};
            journey.ContentVersion=old.ContentVersion;journey.DefinitionJson=JsonSerializer.Serialize(old,RouteCatalog.Json);await db.SaveChangesAsync();
        }
        (await client.PutAsJsonAsync("/api/v1/profiles/personal/journeys/tokyo/content",new {})).EnsureSuccessStatusCode();
        var after=(await client.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard"))!;
        Assert.Equal(before.Totals,after.Totals);Assert.Equal(before.History,after.History);
        Assert.NotNull(before.CurrentJourney); Assert.NotNull(after.CurrentJourney);
        Assert.Equal(before.CurrentJourney.StartedOn,after.CurrentJourney.StartedOn);
        Assert.Equal(before.CurrentJourney.Visits,after.CurrentJourney.Visits);
        Assert.Equal("1.1.0",after.CurrentJourney.ContentVersion);
        Assert.EndsWith(".jpg",after.CurrentJourney.Definition!.Cover);
        Assert.Equal("shinjuku-v1.jpg",after.CurrentJourney.Definition.Checkpoints[0].Image);
    }

    [Fact]
    public async Task An_editorial_update_cannot_silently_change_route_targets()
    {
        using var app=new LocalAppFactory();using var client=app.CreateLocalClient();
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>();
            var profile=await db.Profiles.SingleAsync(x=>x.Kind=="personal"&&x.UserId!=null);
            var journey=await db.Journeys.SingleAsync(x=>x.ProfileId==profile.Id&&x.RouteId=="tokyo");
            var changed=RouteCatalog.ForJourney(journey) with {DistanceKm=30};
            journey.DefinitionJson=JsonSerializer.Serialize(changed,RouteCatalog.Json);await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync("/api/v1/profiles/personal/journeys/tokyo/content",new {})).StatusCode);
    }
}
