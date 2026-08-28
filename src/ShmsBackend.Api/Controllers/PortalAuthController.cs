using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.PortalAuth;
using ShmsBackend.Api.Services.PortalAuth;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;


namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/portalauth")]
public class PortalAuthController : ControllerBase
{
    private readonly IPortalAuthService _portalAuthService;
    private readonly ILogger<PortalAuthController> _logger;
    private readonly ShmsDbContext _context;

    public PortalAuthController(
        IPortalAuthService portalAuthService,
        ILogger<PortalAuthController> logger,
        ShmsDbContext context)
    {
        _portalAuthService = portalAuthService;
        _logger = logger;
        _context = context;
    }

    /// <summary>
    /// Authenticates a portal user (Landlord, Agent, Tenant, or Explorer).
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] PortalLoginDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _portalAuthService.LoginAsync(dto);
        if (!result.Success)
            return Unauthorized(result);

        return Ok(result);
    }

    /// <summary>
    /// Self-registration for Explorer accounts only.
    /// Sends a verification email — account is not active until email is confirmed.
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterExplorerDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _portalAuthService.RegisterExplorerAsync(dto);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Issues a new access token using a valid refresh token.
    /// </summary>
    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken([FromBody] PortalRefreshTokenDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _portalAuthService.RefreshTokenAsync(dto);
        if (!result.Success)
            return Unauthorized(result);

        return Ok(result);
    }

    /// <summary>
    /// Invalidates the current access token.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var token = HttpContext.Request.Headers["Authorization"]
            .ToString()
            .Replace("Bearer ", string.Empty);

        var result = await _portalAuthService.LogoutAsync(token);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Sends a 6-digit OTP to the registered email for password reset.
    /// </summary>
    [HttpPost("request-password-reset")]
    public async Task<IActionResult> RequestPasswordReset([FromBody] PortalRequestPasswordResetDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _portalAuthService.RequestPasswordResetAsync(dto);
        return Ok(result);
    }

    /// <summary>
    /// Resets the password using the OTP received by email.
    /// OTP is single-use and expires in 15 minutes.
    /// </summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] PortalResetPasswordDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _portalAuthService.ResetPasswordAsync(dto);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Verifies the email address of a Landlord, Agent, or Tenant using the token sent by email.
    /// </summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyEmail([FromBody] PortalVerifyEmailDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var result = await _portalAuthService.VerifyEmailAsync(dto);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    /// <summary>
    /// Allows a Landlord, Agent, or Tenant to set a new password using their temporary password.
    /// </summary>
    [HttpPost("set-password")]
    [AllowAnonymous]
    public async Task<IActionResult> SetPassword([FromBody] PortalSetPasswordDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var result = await _portalAuthService.SetPasswordAsync(dto);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    /// <summary>
    /// Allows an authenticated portal user (Landlord, Agent, Tenant, or Explorer) to change
    /// their password by supplying their current password.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] PortalChangePasswordDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(new { success = false, message = "Invalid token." });

        var result = await _portalAuthService.ChangePasswordAsync(userId, dto);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(new { success = false, message = "Invalid token." });

        var user = await _context.PortalUsers
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return NotFound(new { success = false, message = "User not found." });

        // Location source: for Tenants, prefer the assigned flat's location
        // (the tenant's own PortalUser County/Constituency/Ward are typically empty);
        // for all other roles, use the user's own values.
        var county = user.County;
        var constituency = user.Constituency;
        var ward = user.Ward;

        if (user.PortalUserType == PortalUserType.Tenant)
        {
            var tenant = await _context.Tenants
                .Include(t => t.House)
                    .ThenInclude(h => h!.Flat)
                .FirstOrDefaultAsync(t => t.Id == userId);

            if (tenant?.House?.Flat != null)
            {
                county = tenant.House.Flat.County;
                constituency = tenant.House.Flat.Constituency;
                ward = tenant.House.Flat.Ward;
            }
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email,
                user.PendingEmail,
                user.PhoneNumber,
                user.NationalId,
                user.DateOfBirth,
                County = county,
                Constituency = constituency,
                Ward = ward,
                user.IsActive,
                user.IsEmailVerified,
                user.PortalUserType,
                user.CreatedAt
            }
        });
    }

    /// <summary>
    /// Allows an authenticated portal user to update their own phone number and/or
    /// request an email-address change. A phone change applies immediately; an email
    /// change is staged in PendingEmail until confirmed via the link sent to the new address.
    /// </summary>
    [HttpPatch("update-profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdatePortalProfileDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            return Unauthorized(new { success = false, message = "Invalid token." });

        var result = await _portalAuthService.UpdateProfileAsync(userId, dto);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// Confirms a requested email-address change using the token sent to the new address.
    /// AllowAnonymous because the link is clicked from an email, not an authenticated session.
    /// </summary>
    [HttpPost("confirm-email-change")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmailChange([FromBody] ConfirmEmailChangeDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _portalAuthService.ConfirmEmailChangeAsync(dto);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }
}
