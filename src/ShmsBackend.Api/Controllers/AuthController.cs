using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.DTOs.Auth;
using ShmsBackend.Api.Services.Auth;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationPreferenceService _notificationPreferenceService;
    private readonly ShmsDbContext _context;
    private readonly IWeeklyPasswordService _weeklyPasswordService;
    private readonly IWeeklyClientPasswordService _weeklyClientPasswordService;

    public AuthController(
        IAuthService authService,
        ILogger<AuthController> logger,
        IUnitOfWork unitOfWork,
        INotificationPreferenceService notificationPreferenceService,
        ShmsDbContext context,
        IWeeklyPasswordService weeklyPasswordService,
        IWeeklyClientPasswordService weeklyClientPasswordService)
    {
        _authService = authService;
        _logger = logger;
        _unitOfWork = unitOfWork;
        _notificationPreferenceService = notificationPreferenceService;
        _context = context;
        _weeklyPasswordService = weeklyPasswordService;
        _weeklyClientPasswordService = weeklyClientPasswordService;
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

    [HttpGet("notification-preferences")]
    [Authorize]
    public async Task<IActionResult> GetNotificationPreferences()
    {
        var adminIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(adminIdClaim, out var adminId))
            return Unauthorized();

        var pref = await _notificationPreferenceService.GetOrCreateAsync(adminId, isPortalUser: false);
        return Ok(new { success = true, data = NotificationPreferenceDto.FromEntity(pref) });
    }

    [HttpPut("notification-preferences")]
    [Authorize]
    public async Task<IActionResult> UpdateNotificationPreferences([FromBody] NotificationPreferenceDto dto)
    {
        var adminIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(adminIdClaim, out var adminId))
            return Unauthorized();

        await _notificationPreferenceService.UpdateAsync(adminId, isPortalUser: false, dto);
        return Ok(new { success = true, message = "Notification preferences updated." });
    }

    [HttpPost("push-subscription")]
    [Authorize]
    public async Task<IActionResult> SavePushSubscription([FromBody] PushSubscriptionDto dto)
    {
        var adminIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(adminIdClaim, out var adminId))
            return Unauthorized();

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var exists = await _context.PushSubscriptions
            .AnyAsync(s => s.UserId == adminId && !s.IsPortalUser && s.Endpoint == dto.Endpoint);

        if (!exists)
        {
            _context.PushSubscriptions.Add(new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = adminId,
                IsPortalUser = false,
                Endpoint = dto.Endpoint,
                P256dh = dto.Keys.P256dh,
                Auth = dto.Keys.Auth,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        return Ok(new { success = true });
    }

    [HttpPost("push-unsubscribe")]
    [Authorize]
    public async Task<IActionResult> RemovePushSubscription([FromBody] PushSubscriptionDto dto)
    {
        var adminIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(adminIdClaim, out var adminId))
            return Unauthorized();

        var rows = await _context.PushSubscriptions
            .Where(s => s.UserId == adminId && !s.IsPortalUser && s.Endpoint == dto.Endpoint)
            .ToListAsync();

        if (rows.Count > 0)
        {
            _context.PushSubscriptions.RemoveRange(rows);
            await _context.SaveChangesAsync();
        }

        return Ok(new { success = true });
    }

    // ── Weekly shared password — subscriber management ──────────────────────

    [HttpGet("weekly-password/eligible")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> GetWeeklyPasswordEligibleAdmins()
    {
        var data = await _weeklyPasswordService.GetAllEligibleAdminsAsync();
        return Ok(new { success = true, data });
    }

    [HttpGet("weekly-password/subscribers")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> GetWeeklyPasswordSubscribers()
    {
        var data = await _weeklyPasswordService.GetSubscribersAsync();
        return Ok(new { success = true, data });
    }

    [HttpPost("weekly-password/subscribe/{adminId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> SubscribeToWeeklyPassword(Guid adminId)
    {
        await _weeklyPasswordService.SetSubscriptionAsync(adminId, true);
        return Ok(new { success = true, message = "Subscribed to the weekly shared password." });
    }

    [HttpPost("weekly-password/unsubscribe/{adminId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> UnsubscribeFromWeeklyPassword(Guid adminId)
    {
        await _weeklyPasswordService.SetSubscriptionAsync(adminId, false);
        return Ok(new { success = true, message = "Unsubscribed from the weekly shared password." });
    }

    [HttpPost("weekly-password/rotate-now")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> RotateWeeklyPasswordNow()
    {
        await _weeklyPasswordService.GenerateAndRotateAsync();
        return Ok(new { success = true, message = "New password generated and emailed to all enabled staff." });
    }

    // ── Weekly client-portal support password — subscriber management ───────

    [HttpPost("weekly-client-password/subscribe/{adminId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> SubscribeToWeeklyClientPassword(Guid adminId)
    {
        await _weeklyClientPasswordService.SetSubscriptionAsync(adminId, true);
        return Ok(new { success = true, message = "Subscribed to the weekly client portal support password." });
    }

    [HttpPost("weekly-client-password/unsubscribe/{adminId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> UnsubscribeFromWeeklyClientPassword(Guid adminId)
    {
        await _weeklyClientPasswordService.SetSubscriptionAsync(adminId, false);
        return Ok(new { success = true, message = "Unsubscribed from the weekly client portal support password." });
    }

    [HttpPost("weekly-client-password/rotate-now")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> RotateWeeklyClientPasswordNow()
    {
        await _weeklyClientPasswordService.GenerateAndRotateAsync();
        return Ok(new { success = true, message = "New client portal support password generated and emailed to all enabled staff." });
    }
}