using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TrailQuest.Api.Application;
using TrailQuest.Api.Contracts;

namespace TrailQuest.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AccountsController(AccountService accounts) : ControllerBase
{
    [HttpGet("session")]
    public Task<SessionDto> Session(CancellationToken cancellationToken) => accounts.GetSessionAsync(User, cancellationToken);

    [HttpPost("setup")]
    [EnableRateLimiting("accounts")]
    public async Task<IActionResult> Setup(CreateUserRequest request, CancellationToken cancellationToken)
    {
        await accounts.CreateAsync(request, true, cancellationToken);
        var principal = await accounts.LoginAsync(new LoginRequest { Username = request.Username, Password = request.Password }, User, cancellationToken);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return Ok(await accounts.GetSessionAsync(principal, cancellationToken));
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var principal = await accounts.LoginAsync(request, User, cancellationToken);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return Ok(await accounts.GetSessionAsync(principal, cancellationToken));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await accounts.LogoutAsync(User, cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [Authorize]
    [EnableRateLimiting("accounts")]
    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await accounts.ChangePasswordAsync(request, cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [Authorize(Roles = "Owner")]
    [HttpGet("users")]
    public Task<IReadOnlyList<UserDto>> Users(CancellationToken cancellationToken) => accounts.ListUsersAsync(cancellationToken);

    [Authorize(Roles = "Owner")]
    [EnableRateLimiting("accounts")]
    [HttpPost("users")]
    public Task<UserDto> CreateUser(CreateUserRequest request, CancellationToken cancellationToken) => accounts.CreateAsync(request, false, cancellationToken);
}
