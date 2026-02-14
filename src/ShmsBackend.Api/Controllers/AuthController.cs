using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShmsBackend.Api.Models.DTOs.Auth;
using ShmsBackend.Api.Services.Auth;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("pre-login")]
    public async Task<IActionResult> PreLogin([FromBody] LoginDto loginDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _authService.PreLoginAsync(loginDto);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyLoginDto verifyLoginDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _authService.VerifyLoginOtpAsync(verifyLoginDto);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenDto refreshTokenDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _authService.RefreshTokenAsync(refreshTokenDto);
        if (!result.Success)
        {
            return Unauthorized(result);
        }

        return Ok(result);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var token = HttpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
        var result = await _authService.LogoutAsync(token);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpPost("request-password-reset")]
    public async Task<IActionResult> RequestPasswordReset([FromBody] RequestPasswordResetDto requestPasswordResetDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _authService.RequestPasswordResetAsync(requestPasswordResetDto);
        return Ok(result);
    }
}