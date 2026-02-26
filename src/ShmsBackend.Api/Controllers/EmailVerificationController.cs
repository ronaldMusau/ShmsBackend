using Microsoft.AspNetCore.Mvc;
using ShmsBackend.Api.Models.DTOs.Auth;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Data.Repositories.Interfaces;
using BCrypt.Net;
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
    /// Verify email.
    /// Looks up by TOKEN (unique per user row), NOT by email — because
    /// multiple roles can share the same email address in this system.
    /// Uses the existing IRepository.GetFirstOrDefaultAsync method.
    /// </summary>
    [HttpPost("verify")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailDto verifyEmailDto)
    {
        try
        {
            _logger.LogInformation("Verifying email for: {Email}", verifyEmailDto.Email);

            // KEY FIX: look up by token (unique) not email (not unique across roles)
            var user = await _unitOfWork.Admins.GetFirstOrDefaultAsync(
                a => a.EmailVerificationToken == verifyEmailDto.Token);

            if (user == null)
            {
                _logger.LogWarning("No user found with token: {Token}", verifyEmailDto.Token);
                return NotFound(ApiResponse<object>.FailureResponse("Invalid verification token"));
            }

            // Confirm the token belongs to the email in the request
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
    /// Also looks up by TOKEN for the same reason.
    /// </summary>
    [HttpPost("set-password")]
    public async Task<IActionResult> SetPassword([FromBody] SetPasswordDto setPasswordDto)
    {
        try
        {
            _logger.LogInformation("Setting password for: {Email}", setPasswordDto.Email);

            // KEY FIX: look up by token, not email
            var user = await _unitOfWork.Admins.GetFirstOrDefaultAsync(
                a => a.EmailVerificationToken == setPasswordDto.Token);

            if (user == null)
            {
                _logger.LogWarning("No user found with token for password set: {Token}", setPasswordDto.Token);
                return NotFound(ApiResponse<object>.FailureResponse("Invalid verification token"));
            }

            // Confirm token belongs to the right email
            if (!string.Equals(user.Email, setPasswordDto.Email, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Token/email mismatch on set-password. Token owner: {TokenEmail}, Requested: {Email}",
                    user.Email, setPasswordDto.Email);
                return BadRequest(ApiResponse<object>.FailureResponse("Invalid verification token"));
            }

            if (user.EmailVerificationTokenExpiry < DateTime.UtcNow)
            {
                _logger.LogWarning("Expired token for password set: {Email} (UserType: {UserType})",
                    user.Email, user.UserType);
                return BadRequest(ApiResponse<object>.FailureResponse("Verification token has expired"));
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(setPasswordDto.NewPassword);
            user.IsEmailVerified = true;
            user.EmailVerificationToken = null;
            user.EmailVerificationTokenExpiry = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _unitOfWork.Admins.UpdateAsync(user);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("User {Email} (UserType: {UserType}) set password successfully",
                user.Email, user.UserType);

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
    /// Resend verification.
    /// Uses FindAsync (returns ALL matches) to cover multiple unverified
    /// accounts sharing the same email across different roles.
    /// </summary>
    [HttpPost("resend")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationDto resendDto)
    {
        try
        {
            _logger.LogInformation("Resending verification email to: {Email}", resendDto.Email);

            // FindAsync returns IEnumerable — all rows matching the predicate
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

            // Refresh tokens and save first
            foreach (var user in unverifiedUsers)
            {
                user.EmailVerificationToken = GenerateVerificationToken();
                user.EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24);
                await _unitOfWork.Admins.UpdateAsync(user);
            }

            await _unitOfWork.SaveChangesAsync();

            // Then send emails
            foreach (var user in unverifiedUsers)
            {
                var verificationLink = _frontendUrlService.GetEmailVerificationUrl(
                    user.EmailVerificationToken!, user.Email);

                _logger.LogInformation(
                    "Sending verification email to {Email} (UserType: {UserType})",
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