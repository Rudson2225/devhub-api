using DevHub.Application.Abstractions;
using DevHub.Application.DTOs.Auth;
using DevHub.Application.DTOs.Users;
using DevHub.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController : ApiControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly IUserService _userService;
    private readonly IAuthService _authService;

    public UsersController(ICurrentUserService currentUser, IUserService userService, IAuthService authService)
    {
        _currentUser = currentUser;
        _userService = userService;
        _authService = authService;
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
        => Ok(await _userService.GetProfileAsync(GetCurrentUserId(), cancellationToken));

    [HttpPatch("me")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UserResponse>> UpdateMe([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);
        return Ok(await _userService.UpdateProfileAsync(GetCurrentUserId(), request, cancellationToken));
    }

    [HttpPost("me/change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await ValidateAsync(request, cancellationToken);
        await _authService.ChangePasswordAsync(GetCurrentUserId(), request, cancellationToken);
        return NoContent();
    }

    private Guid GetCurrentUserId()
        => _currentUser.UserId
           ?? throw new DevHub.Application.Common.Exceptions.UnauthorizedException("Authentication required.");
}
