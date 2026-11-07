using Microsoft.AspNetCore.RateLimiting;
﻿using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly ShmsBackend.Api.Services.Security.IThreatGuard _guard;
    private readonly ShmsBackend.Api.Services.Security.IRecoveryRequestService _recovery;

    public AuthController(
        IAuthService authService,
        ILogger<AuthController> logger,
        IUnitOfWork unitOfWork,
        INotificationPreferenceService notificationPreferenceService,
        ShmsDbContext context,
        IWeeklyPasswordService weeklyPasswordService,
        IWeeklyClientPasswordService weeklyClientPasswordService,
        ShmsBackend.Api.Services.Security.IThreatGuard guard,
        ShmsBackend.Api.Services.Security.IRecoveryRequestService recovery)
    {
        _guard = guard;
        _authService = authService;
        _logger = logger;
        _unitOfWork = unitOfWork;
        _notificationPreferenceService = notificationPreferenceService;
        _context = context;
        _recovery = recovery;
        _weeklyPasswordService = weeklyPasswordService;
        _weeklyClientPasswordService = weeklyClientPasswordService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // SuperAdmin and Admin sign in only on the separate staff page, everyone else only on the public page. The
        // refusal looks exactly like a wrong password, so the public page never reveals that those roles exist.
        var privileged = loginDto.SelectedUserType is ShmsBackend.Data.Enums.UserType.SuperAdmin or ShmsBackend.Data.Enums.UserType.Admin;
        var refused = privileged != loginDto.StaffPortal;
        if (!refused && privileged)
        {
            var st = await _guard.GetSettingsAsync();
            var cfg = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            if (ShmsBackend.Api.Services.Security.PrivilegedIpRule.Active(st, cfg)
                && !ShmsBackend.Api.Services.Security.PrivilegedIpRule.IsAllowed(ip, st.PrivilegedAllowedIps))
            {
                refused = true;
                var cache = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                if (ShmsBackend.Api.Services.Security.PrivilegedIpRule.ShouldReport(cache, ip))
                    await HttpContext.RequestServices.GetRequiredService<ShmsBackend.Api.Services.Security.ISecurityEventService>().RecordAsync(
                        "Access", "PrivilegedIpRefused", "High",
                        $"A {loginDto.SelectedUserType} sign-in for {loginDto.Email} was refused because address {ip} is not on the allowed list.", null, null, ip);
            }
        }
        var result = refused
            ? ShmsBackend.Api.Models.Responses.ApiResponse<ShmsBackend.Api.Models.Responses.AuthResponse>.FailureResponse("Invalid email, password, or user type")
            : await _authService.LoginAsync(loginDto, ip);
        // A correct password only leads to the code step, so only failures count here; the real sign-in is
        // recorded when the code is verified.
        if (!result.Success)
            await _guard.OnLoginAsync(ip, Request.Headers.UserAgent.ToString(), loginDto.Email, false, loginDto.SelectedUserType.ToString());
        if (!result.Success)
        {
            return Unauthorized(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// The staff sign-in page asks this first. When the approved-address rule is on and the caller is not on the list the answer is
    /// a plain 404, so the page looks like any route that does not exist.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("staff-portal-access")]
    public async Task<IActionResult> StaffPortalAccess()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var st = await _guard.GetSettingsAsync();
        var cfg = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (ShmsBackend.Api.Services.Security.PrivilegedIpRule.Active(st, cfg)
            && !ShmsBackend.Api.Services.Security.PrivilegedIpRule.IsAllowed(ip, st.PrivilegedAllowedIps))
            return NotFound();
        return Ok(new { allowed = true });
    }

    [AllowAnonymous]
    [HttpPost("login/verify-otp")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyLoginOtp([FromBody] VerifyLoginOtpDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _authService.VerifyLoginOtpAsync(dto, ip);
        if (result.Success && result.Data != null && !string.IsNullOrEmpty(result.Data.AccessToken))
            await _guard.OnLoginAsync(ip, Request.Headers.UserAgent.ToString(), result.Data.Email, true, result.Data.UserType.ToString());
        else if (!result.Success)
        {
            // Record the failed code against the real account and role (the caller is told nothing extra).
            var owner = await _context.LoginChallenges.AsNoTracking().Where(c => c.Id == dto.ChallengeId)
                .Join(_context.Admins.AsNoTracking(), c => c.AdminId, a => a.Id, (c, a) => new { a.Email, a.UserType })
                .FirstOrDefaultAsync();
            await _guard.OnLoginAsync(ip, Request.Headers.UserAgent.ToString(), owner?.Email ?? "code-step", false, owner?.UserType.ToString());
        }
        return result.Success ? Ok(result) : Unauthorized(result);
    }

    /// <summary>Whether the signed-in staff member's authenticator app is activated. It cannot be switched off by the user.</summary>
    [Authorize]
    [HttpGet("authenticator-status")]
    public async Task<IActionResult> AuthenticatorStatus()
    {
        var id = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;
        var a = await _context.Admins.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.TotpEnabledAt }).FirstOrDefaultAsync();
        if (a == null) return NotFound();
        return Ok(new { activated = a.TotpEnabledAt != null, activatedAt = a.TotpEnabledAt });
    }


    // ---- change phone / new recovery codes (signed-in staff, asks for a current app code first) ----
    public class CurrentCodeDto { [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(16)] public string Code { get; set; } = string.Empty; }
    public class ReplaceConfirmDto { [System.ComponentModel.DataAnnotations.Required] public string Pending { get; set; } = string.Empty; [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(16)] public string Code { get; set; } = string.Empty; }

    private Guid MyId => Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : Guid.Empty;

    /// <summary>Five wrong codes in 15 minutes pauses these screens for that person, so a stolen session cannot guess the app code.</summary>
    private bool TooManyWrong(Guid id, bool record)
    {
        var cache = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        var key = "authchg:" + id;
        var n = cache.TryGetValue<int>(key, out var v) ? v : 0;
        if (record) { n++; cache.Set(key, n, TimeSpan.FromMinutes(15)); }
        return n >= 5;
    }

    private async Task<(Admin? admin, long? step)> CheckCurrentAppCodeAsync(string code)
    {
        var totp = HttpContext.RequestServices.GetRequiredService<ShmsBackend.Api.Services.Security.IAuthenticatorService>();
        var protector = HttpContext.RequestServices.GetRequiredService<ShmsBackend.Api.Services.Security.ISecretProtector>();
        var admin = await _context.Admins.FirstOrDefaultAsync(a => a.Id == MyId && a.IsActive && !a.IsDeleted);
        var secret = protector.Unprotect(admin?.TotpSecretEnc);
        if (admin == null || secret == null) return (null, null);
        var step = totp.VerifyCode(secret, code, admin.TotpLastStep);
        if (step == null) return (admin, null);
        admin.TotpLastStep = step.Value;
        await _context.SaveChangesAsync();
        return (admin, step);
    }

    [Authorize]
    [HttpPost("authenticator/new-recovery-codes")]
    [EnableRateLimiting("auth-sensitive")]
    public async Task<IActionResult> NewRecoveryCodes([FromBody] CurrentCodeDto dto)
    {
        if (TooManyWrong(MyId, false)) return StatusCode(429, new { success = false, message = "Too many wrong codes. Try again in 15 minutes." });
        var (admin, step) = await CheckCurrentAppCodeAsync(dto.Code);
        if (admin == null || step == null) { TooManyWrong(MyId, true); return BadRequest(new { success = false, message = "That code is not correct. Use the 6-digit code from your authenticator app." }); }
        var codes = await HttpContext.RequestServices.GetRequiredService<ShmsBackend.Api.Services.Security.IAuthenticatorService>().CreateRecoveryCodesAsync(admin.Id);
        await HttpContext.RequestServices.GetRequiredService<ShmsBackend.Api.Services.Security.ISecurityEventService>().RecordAsync(
            "Access", "RecoveryCodesRenewed", "Info", $"The {admin.UserType} account {admin.Email} made a new set of recovery codes. The old set no longer works.",
            admin.Id.ToString(), admin.UserType.ToString(), HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(new { success = true, recoveryCodes = codes });
    }

    [Authorize]
    [HttpPost("authenticator/replace/start")]
    [EnableRateLimiting("auth-sensitive")]
    public async Task<IActionResult> ReplaceStart([FromBody] CurrentCodeDto dto)
    {
        if (TooManyWrong(MyId, false)) return StatusCode(429, new { success = false, message = "Too many wrong codes. Try again in 15 minutes." });
        var (admin, step) = await CheckCurrentAppCodeAsync(dto.Code);
        if (admin == null || step == null) { TooManyWrong(MyId, true); return BadRequest(new { success = false, message = "That code is not correct. Use the 6-digit code from your current authenticator app." }); }
        var totp = HttpContext.RequestServices.GetRequiredService<ShmsBackend.Api.Services.Security.IAuthenticatorService>();
        var protector = HttpContext.RequestServices.GetRequiredService<ShmsBackend.Api.Services.Security.ISecretProtector>();
        var secret = totp.NewSecret();
        // Nothing is stored yet: the new secret travels back sealed, bound to this person and valid for 10 minutes.
        var pending = protector.Protect($"{admin.Id:N}|{DateTime.UtcNow.AddMinutes(10).Ticks}|{secret}");
        return Ok(new { success = true, pending, secret, otpAuthUri = totp.BuildUri(admin.Email, secret), account = admin.Email });
    }

    [Authorize]
    [HttpPost("authenticator/replace/confirm")]
    [EnableRateLimiting("auth-sensitive")]
    public async Task<IActionResult> ReplaceConfirm([FromBody] ReplaceConfirmDto dto)
    {
        if (TooManyWrong(MyId, false)) return StatusCode(429, new { success = false, message = "Too many wrong codes. Try again in 15 minutes." });
        var totp = HttpContext.RequestServices.GetRequiredService<ShmsBackend.Api.Services.Security.IAuthenticatorService>();
        var protector = HttpContext.RequestServices.GetRequiredService<ShmsBackend.Api.Services.Security.ISecretProtector>();
        var plain = protector.Unprotect(dto.Pending);
        var parts = plain?.Split('|');
        if (parts == null || parts.Length != 3 || parts[0] != MyId.ToString("N") || !long.TryParse(parts[1], out var ticks) || ticks < DateTime.UtcNow.Ticks)
            return BadRequest(new { success = false, message = "This change has expired. Start again." });
        var admin = await _context.Admins.FirstOrDefaultAsync(a => a.Id == MyId && a.IsActive && !a.IsDeleted);
        if (admin == null) return NotFound();
        var step = totp.VerifyCode(parts[2], dto.Code, 0);
        if (step == null) { TooManyWrong(MyId, true); return BadRequest(new { success = false, message = "That code is not correct. Scan the new QR code, then enter the code the new app shows." }); }
        admin.TotpSecretEnc = protector.Protect(parts[2]);
        admin.TotpEnabledAt = DateTime.UtcNow;
        admin.TotpLastStep = step.Value;
        await _context.SaveChangesAsync();
        var codes = await totp.CreateRecoveryCodesAsync(admin.Id);
        await HttpContext.RequestServices.GetRequiredService<ShmsBackend.Api.Services.Security.ISecurityEventService>().RecordAsync(
            "Access", "AuthenticatorReplaced", "Warning", $"The {admin.UserType} account {admin.Email} moved its authenticator to a new phone or app. Old recovery codes no longer work.",
            admin.Id.ToString(), admin.UserType.ToString(), HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(new { success = true, recoveryCodes = codes });
    }

    public class EmergencyRecoveryDto { [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(200)] public string Email { get; set; } = string.Empty; public bool StaffPortal { get; set; } }

    /// <summary>Always answers the same way, so it cannot be used to find out which emails are staff accounts.</summary>
    [AllowAnonymous]
    [HttpPost("emergency-recovery")]
    [EnableRateLimiting("auth-sensitive")]
    public async Task<IActionResult> EmergencyRecovery([FromBody] EmergencyRecoveryDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        try { await _recovery.RequestAsync(dto.Email, ip, dto.StaffPortal); } catch (Exception ex) { _logger.LogWarning(ex, "Emergency recovery request failed"); }
        return Ok(new { success = true, message = "If that email belongs to a staff account, an administrator has been told and will contact you." });
    }

    [AllowAnonymous]
    [HttpPost("login/enrol/start")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> StartEnrol([FromBody] EnrolStartDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var result = await _authService.StartEnrolAsync(dto);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [AllowAnonymous]
    [HttpPost("login/enrol/confirm")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ConfirmEnrol([FromBody] EnrolConfirmDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _authService.ConfirmEnrolAsync(dto, ip);
        if (result.Success && result.Data != null && !string.IsNullOrEmpty(result.Data.AccessToken))
            await _guard.OnLoginAsync(ip, Request.Headers.UserAgent.ToString(), result.Data.Email, true, result.Data.UserType.ToString());
        return result.Success ? Ok(result) : Unauthorized(result);
    }

    [AllowAnonymous]
    [HttpPost("login/resend-otp")]
    [EnableRateLimiting("auth-sensitive")]
    public async Task<IActionResult> ResendLoginOtp([FromBody] ResendLoginOtpDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await _authService.ResendLoginOtpAsync(dto, ip);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    [AllowAnonymous]
    [HttpPost("refresh-token")]
    [EnableRateLimiting("auth")]
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

    [AllowAnonymous]
    [HttpPost("request-password-reset")]
    [EnableRateLimiting("auth-sensitive")]
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
    [EnableRateLimiting("auth-sensitive")]
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