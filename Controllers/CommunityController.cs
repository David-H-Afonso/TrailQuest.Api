using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TrailQuest.Api.Application;
using TrailQuest.Api.Contracts;

namespace TrailQuest.Api.Controllers;

[ApiController, Authorize, Route("api/v1/community")]
public sealed class CommunityController(CommunityService community) : ControllerBase
{
    [HttpGet]
    public Task<CommunityDto> Read(string profile = "personal", CancellationToken ct = default) => community.ReadAsync(profile, ct);
    [HttpPost("friends"), EnableRateLimiting("community")]
    public async Task<IActionResult> RequestFriend(FriendRequest request, CancellationToken ct) { await community.RequestAsync(request.Username, ct); return NoContent(); }
    [HttpPost("friends/{id}/accept")]
    public async Task<IActionResult> Accept(string id, CancellationToken ct) { await community.RespondAsync(id, true, ct); return NoContent(); }
    [HttpDelete("friends/{id}")]
    public async Task<IActionResult> Remove(string id, CancellationToken ct) { await community.RespondAsync(id, false, ct); return NoContent(); }
    [HttpPost("blocks/{userId}")]
    public async Task<IActionResult> Block(string userId, CancellationToken ct) { await community.BlockAsync(userId, ct); return NoContent(); }
    [HttpPost("preview")]
    public Task<PostDto> Preview(ShareRequest request, CancellationToken ct) => community.PreviewAsync(request, ct);
    [HttpPost("posts"), EnableRateLimiting("community")]
    public Task<PostDto> Publish(ShareRequest request, CancellationToken ct) => community.PublishAsync(request, ct);
    [HttpDelete("posts/{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct) { await community.DeleteAsync(id, ct); return NoContent(); }
}
