using DevHub.Application.Abstractions;
using DevHub.Application.DTOs.Auth;
using DevHub.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevHub.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ICurrentUserService currentUser, ILogger<AuthController> logger)
    {
        _authService = authService;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        // The submitted address is never logged: credentials and identifiers stay out of the logs.
        _logger.LogInformation("Registration attempt from {Ip}", ip);

        var response = await _authService.RegisterAsync(request, ip, cancellationToken);

        // Location has to name a resource the caller can actually retrieve. This action is POST-only,
        // so pointing at it produced a header that answered 405 to any client following it; the
        // canonical URI of the user that was just created is its own profile.
        return CreatedAtAction(nameof(UsersController.Me), "Users", new { }, response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var response = await _authService.LoginAsync(request, ip, cancellationToken);

        return Ok(response);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var response = await _authService.RefreshAsync(request, ip, cancellationToken);

        return Ok(response);
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    // Same budget as the other three routes of this controller: without it the one endpoint that
    // ends a session would be the only one left open to an endless burst of requests.
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);

        // The acting user comes from the validated principal, never from the request body, so one
        // user can never revoke a token that belongs to another.
        var userId = _currentUser.UserId
            ?? throw new DevHub.Application.Common.Exceptions.UnauthorizedException("Authentication required.");

        await _authService.LogoutAsync(userId, request, cancellationToken);

        return NoContent();
    }
}
