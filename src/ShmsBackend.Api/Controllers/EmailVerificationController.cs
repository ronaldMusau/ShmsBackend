using Microsoft.AspNetCore.Mvc;
using ShmsBackend.Api.Models.DTOs.Auth;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Data.Repositories.Interfaces;
using Microsoft.Extensions.Options;
using ShmsBackend.Api.Configuration;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmailVerificationController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailVerificationController> _logger;
    private readonly IFrontendUrlService _frontendUrlService;
    private readonly IOptions<AppSettings> _appSettings;

    public EmailVerificationController(
        IUnitOfWork unitOfWork,
        IEmailService emailService,
        ILogger<EmailVerificationController> logger,
        IFrontendUrlService frontendUrlService,
        IOptions<AppSettings> appSettings)
    {
        _unitOfWork = unitOfWork;
        _emailService = emailService;
        _logger = logger;
        _frontendUrlService = frontendUrlService;
        _appSettings = appSettings;

        _logger.LogInformation("EmailVerificationController initialized with FrontendUrl: {FrontendUrl}",
            _appSettings.Value.FrontendUrl);
    }

    /// <summary>
    /// Verify email. Looks up by TOKEN (unique per row) not email (not unique across roles).
    /// </summary>
    [HttpPost("verify")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailDto verifyEmailDto)
    {
        try
        {
            _logger.LogInformation("Verifying email for: {Email}", verifyEmailDto.Email);

            var user = await _unitOfWork.Admins.GetFirstOrDefaultAsync(
                a => a.EmailVerificationToken == verifyEmailDto.Token);

            if (user == null)
            {
                _logger.LogWarning("No user found with token: {Token}", verifyEmailDto.Token);
                return NotFound(ApiResponse<object>.FailureResponse("Invalid verification token"));
            }

            if (!string.Equals(user.Email, verifyEmailDto.Email, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Token/email mismatch. Token owner: {TokenEmail}, Requested: {Email}",
                    user.Email, verifyEmailDto.Email);
                return BadRequest(ApiResponse<object>.FailureResponse("Invalid verification token"));
            }

            if (user.IsEmailVerified)
            {
                _logger.LogWarning("Email already verified for: {Email} (UserType: {UserType})",
                    user.Email, user.UserType);
                return BadRequest(ApiResponse<object>.FailureResponse("Email already verified"));
            }

            if (user.EmailVerificationTokenExpiry < DateTime.UtcNow)
            {
                _logger.LogWarning("Expired verification token for: {Email} (UserType: {UserType})",
                    user.Email, user.UserType);
                return BadRequest(ApiResponse<object>.FailureResponse("Verification token has expired"));
            }

            _logger.LogInformation("Email verified successfully for: {Email} (UserType: {UserType})",
                user.Email, user.UserType);

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                email = user.Email,
                token = user.EmailVerificationToken
            }, "Email verified. Please set your password."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying email for: {Email}", verifyEmailDto?.Email ?? "unknown");
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred during email verification"));
        }
    }

    /// <summary>
    /// Set password.
    /// - Flow "new-user":       CurrentPassword is REQUIRED (proves they received the email
    ///                          and know the temporary password — security check).
    /// - Flow "forgot-password": CurrentPassword is NOT required (that's the whole point of forgot password).
    /// Looks up by TOKEN not email, since multiple roles can share the same email.
    /// </summary>
    [HttpPost("set-password")]
    public async Task<IActionResult> SetPassword([FromBody] SetPasswordDto setPasswordDto)
    {
        try
        {
            _logger.LogInformation("Setting password for: {Email}, Flow: {Flow}",
                setPasswordDto.Email, setPasswordDto.Flow);

            // Look up by token (unique) not email (not unique across roles)
            var user = await _unitOfWork.Admins.GetFirstOrDefaultAsync(
                a => a.EmailVerificationToken == setPasswordDto.Token);

            if (user == null)
            {
                _logger.LogWarning("No user found with token: {Token}", setPasswordDto.Token);
                return NotFound(ApiResponse<object>.FailureResponse("Invalid verification token"));
            }

            if (!string.Equals(user.Email, setPasswordDto.Email, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Token/email mismatch. Token owner: {TokenEmail}, Requested: {Email}",
                    user.Email, setPasswordDto.Email);
                return BadRequest(ApiResponse<object>.FailureResponse("Invalid verification token"));
            }

            if (user.EmailVerificationTokenExpiry < DateTime.UtcNow)
            {
                _logger.LogWarning("Expired token for: {Email} (UserType: {UserType})",
                    user.Email, user.UserType);
                return BadRequest(ApiResponse<object>.FailureResponse("Verification token has expired"));
            }

            // ── Flow-specific logic ──────────────────────────────────────────
            if (setPasswordDto.Flow == "new-user")
            {
                // New user MUST provide their current (temporary) password as proof
                // they are the legitimate recipient of the email
                if (string.IsNullOrWhiteSpace(setPasswordDto.CurrentPassword))
                {
                    return BadRequest(ApiResponse<object>.FailureResponse(
                        "Current password is required to activate your account"));
                }

                if (!BCrypt.Net.BCrypt.Verify(setPasswordDto.CurrentPassword, user.PasswordHash))
                {
                    _logger.LogWarning("Invalid current password for new-user flow: {Email}", user.Email);
                    return BadRequest(ApiResponse<object>.FailureResponse(
                        "Current password is incorrect"));
                }
            }
            // For "forgot-password" flow, no current password check — that's the point

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(setPasswordDto.NewPassword);
            user.IsEmailVerified = true;
            user.EmailVerificationToken = null;
            user.EmailVerificationTokenExpiry = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _unitOfWork.Admins.UpdateAsync(user);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("User {Email} (UserType: {UserType}) set password successfully via {Flow} flow",
                user.Email, user.UserType, setPasswordDto.Flow);

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                loginUrl = _frontendUrlService.GetLoginUrl()
            }, "Password set successfully. You can now login."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting password for: {Email}", setPasswordDto?.Email ?? "unknown");
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while setting password"));
        }
    }

    /// <summary>
    /// Resend verification — handles multiple unverified accounts sharing the same email.
    /// </summary>
    [HttpPost("resend")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationDto resendDto)
    {
        try
        {
            _logger.LogInformation("Resending verification email to: {Email}", resendDto.Email);

            var unverifiedUsers = (await _unitOfWork.Admins.FindAsync(
                a => a.Email.ToLower() == resendDto.Email.ToLower() && !a.IsEmailVerified))
                .ToList();

            if (!unverifiedUsers.Any())
            {
                var anyExists = await _unitOfWork.Admins.ExistsAsync(
                    a => a.Email.ToLower() == resendDto.Email.ToLower());

                return anyExists
                    ? BadRequest(ApiResponse<object>.FailureResponse("Email already verified"))
                    : NotFound(ApiResponse<object>.FailureResponse("User not found"));
            }

            foreach (var user in unverifiedUsers)
            {
                user.EmailVerificationToken = GenerateVerificationToken();
                user.EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24);
                await _unitOfWork.Admins.UpdateAsync(user);
            }

            await _unitOfWork.SaveChangesAsync();

            foreach (var user in unverifiedUsers)
            {
                var verificationLink = _frontendUrlService.GetEmailVerificationUrl(
                    user.EmailVerificationToken!, user.Email);

                _logger.LogInformation("Resending to {Email} (UserType: {UserType})",
                    user.Email, user.UserType);

                await _emailService.SendEmailVerificationEmailAsync(
                    user.Email, user.FirstName, verificationLink);
            }

            return Ok(ApiResponse<object>.SuccessResponse(null, "Verification email resent successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resending verification to: {Email}", resendDto?.Email ?? "unknown");
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while resending verification"));
        }
    }

    [HttpGet("debug/frontend-url")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult DebugFrontendUrl()
    {
        if (!_appSettings.Value.Environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
            return NotFound();

        return Ok(new
        {
            configuredUrl = _appSettings.Value.FrontendUrl,
            environment = _appSettings.Value.Environment,
            sampleVerificationUrl = _frontendUrlService.GetEmailVerificationUrl("sample-token-123", "test@example.com"),
            loginUrl = _frontendUrlService.GetLoginUrl()
        });
    }

    private string GenerateVerificationToken()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("/", "_")
            .Replace("+", "-")
            .TrimEnd('=');
    }
}