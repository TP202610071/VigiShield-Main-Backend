using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VigiShield.Application.DTOs.Auth;
using VigiShield.Application.Services;
using VigiShield.Common.Extensions;

namespace VigiShield.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly AuthService _authService;

    public UsersController(AuthService authService) => _authService = authService;

    [HttpGet]
    public async Task<ActionResult<List<SecondaryUserDto>>> GetSecondaryUsers()
    {
        return Ok(await _authService.GetSecondaryUsersAsync(User.GetHouseholdId()));
    }

    [HttpPost("invite")]
    public async Task<ActionResult<InviteUserResponse>> InviteUser([FromBody] InviteUserRequest request)
    {
        if (!User.IsPrimaryResident())
            return Forbid();

        return Ok(await _authService.InviteUserAsync(User.GetHouseholdId(), request));
    }

    [HttpDelete("{userId:guid}/revoke")]
    public async Task<IActionResult> RevokeUser(Guid userId)
    {
        if (!User.IsPrimaryResident())
            return Forbid();

        await _authService.RevokeUserAsync(User.GetHouseholdId(), userId);
        return NoContent();
    }

    [HttpPost("accept-invitation")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> AcceptInvitation([FromBody] AcceptInvitationRequest request)
    {
        return Ok(await _authService.AcceptInvitationAsync(request));
    }

    // ── Developer/administrator management (Admin role only) ───────────────────

    [HttpGet("admins")]
    public async Task<ActionResult<List<AdminUserDto>>> GetAdmins()
    {
        if (!User.IsAdmin()) return Forbid();
        return Ok(await _authService.GetAdminsAsync());
    }

    [HttpPost("admins")]
    public async Task<ActionResult<AdminUserDto>> AddAdmin([FromBody] AddAdminRequest request)
    {
        if (!User.IsAdmin()) return Forbid();
        return Ok(await _authService.AddAdminAsync(request.Email));
    }

    [HttpDelete("admins/{userId:guid}")]
    public async Task<IActionResult> RemoveAdmin(Guid userId)
    {
        if (!User.IsAdmin()) return Forbid();
        await _authService.RemoveAdminAsync(userId);
        return NoContent();
    }

    /// <summary>Search households by primary user name/email (for the dev alert-trigger tool).</summary>
    [HttpGet("households")]
    public async Task<ActionResult<List<HouseholdSummaryDto>>> SearchHouseholds([FromQuery] string? query)
    {
        if (!User.IsAdmin()) return Forbid();
        return Ok(await _authService.SearchHouseholdsAsync(query));
    }
}
