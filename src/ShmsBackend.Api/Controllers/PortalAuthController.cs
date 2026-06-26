using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.PortalAuth;
using ShmsBackend.Api.Services.PortalAuth;


namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/portalauth")]
public class PortalAuthController : ControllerBase
{
    private readonly IPortalAuthService _portalAuthService;
    private readonly ILogger<PortalAuthController> _logger;

    public PortalAuthController(
        IPortalAuthService portalAuthService,
        ILogger<PortalAuthController> logger)
    {
        _portalAuthService = portalAuthService;
        _logger = logger;
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
}
