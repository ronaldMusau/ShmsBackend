using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShmsBackend.Api.Models.DTOs.Auth;
using ShmsBackend.Api.Services.Auth;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public AuthController(IAuthService authService, ILogger<AuthController> logger, IUnitOfWork unitOfWork)
    {
        _authService = authService;
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _authService.LoginAsync(loginDto);
        if (!result.Success)
        {
            return Unauthorized(result);
        }

        return Ok(result);
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

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _authService.ResetPasswordAsync(dto);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var adminIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(adminIdClaim, out var adminId))
            return Unauthorized();

        var result = await _authService.ChangePasswordAsync(adminId, dto);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe()
    {
        var adminIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(adminIdClaim, out var adminId))
            return Unauthorized();

        var admin = await _unitOfWork.Admins.GetByIdAsync(adminId);
        if (admin == null)
            return NotFound(new { success = false, message = "Admin not found." });

        return Ok(new
        {
            success = true,
            data = new
            {
                admin.Id,
                admin.FirstName,
                admin.LastName,
                admin.Email,
                admin.PhoneNumber,
                admin.NationalId,
                admin.DateOfBirth,
                admin.IsActive,
                admin.IsEmailVerified,
                admin.UserType,
                admin.CreatedAt
            }
        });
    }
}