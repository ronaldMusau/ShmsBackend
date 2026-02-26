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

    [HttpPost("verify")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailDto verifyEmailDto)
    {
        try
        {
            _logger.LogInformation("Verifying email for: {Email}", verifyEmailDto.Email);

            var user = await _unitOfWork.Admins.GetByEmailAsync(verifyEmailDto.Email);

            if (user == null)
            {
                _logger.LogWarning("User not found for email: {Email}", verifyEmailDto.Email);
                return NotFound(ApiResponse<object>.FailureResponse("User not found"));
            }

            if (user.IsEmailVerified)
            {
                _logger.LogWarning("Email already verified for: {Email}", verifyEmailDto.Email);
                return BadRequest(ApiResponse<object>.FailureResponse("Email already verified"));
            }

            if (user.EmailVerificationToken != verifyEmailDto.Token)
            {
                _logger.LogWarning("Invalid verification token for: {Email}", verifyEmailDto.Email);
                return BadRequest(ApiResponse<object>.FailureResponse("Invalid verification token"));
            }

            if (user.EmailVerificationTokenExpiry < DateTime.UtcNow)
            {
                _logger.LogWarning("Expired verification token for: {Email}", verifyEmailDto.Email);
                return BadRequest(ApiResponse<object>.FailureResponse("Verification token has expired"));
            }

            // Token is valid - return success so frontend can show set password form
            _logger.LogInformation("Email verified successfully for: {Email}", verifyEmailDto.Email);

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

    [HttpPost("set-password")]
    public async Task<IActionResult> SetPassword([FromBody] SetPasswordDto setPasswordDto)
    {
        try
        {
            _logger.LogInformation("Setting password for: {Email}", setPasswordDto.Email);

            var user = await _unitOfWork.Admins.GetByEmailAsync(setPasswordDto.Email);

            if (user == null)
            {
                _logger.LogWarning("User not found for password set: {Email}", setPasswordDto.Email);
                return NotFound(ApiResponse<object>.FailureResponse("User not found"));
            }

            if (user.EmailVerificationToken != setPasswordDto.Token)
            {
                _logger.LogWarning("Invalid verification token for password set: {Email}", setPasswordDto.Email);
                return BadRequest(ApiResponse<object>.FailureResponse("Invalid verification token"));
            }

            if (user.EmailVerificationTokenExpiry < DateTime.UtcNow)
            {
                _logger.LogWarning("Expired verification token for password set: {Email}", setPasswordDto.Email);
                return BadRequest(ApiResponse<object>.FailureResponse("Verification token has expired"));
            }

            // Hash the new password
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(setPasswordDto.NewPassword);
            user.IsEmailVerified = true;
            user.EmailVerificationToken = null;
            user.EmailVerificationTokenExpiry = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _unitOfWork.Admins.UpdateAsync(user);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("User {Email} verified email and set password successfully", user.Email);

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

    [HttpPost("resend")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationDto resendDto)
    {
        try
        {
            _logger.LogInformation("Resending verification email to: {Email}", resendDto.Email);

            var user = await _unitOfWork.Admins.GetByEmailAsync(resendDto.Email);

            if (user == null)
            {
                _logger.LogWarning("User not found for resend verification: {Email}", resendDto.Email);
                return NotFound(ApiResponse<object>.FailureResponse("User not found"));
            }

            if (user.IsEmailVerified)
            {
                _logger.LogWarning("Email already verified for resend: {Email}", resendDto.Email);
                return BadRequest(ApiResponse<object>.FailureResponse("Email already verified"));
            }

            // Generate new verification token
            user.EmailVerificationToken = GenerateVerificationToken();
            user.EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24);

            await _unitOfWork.Admins.UpdateAsync(user);
            await _unitOfWork.SaveChangesAsync();

            // Send new verification email using the FrontendUrlService
            var verificationLink = _frontendUrlService.GetEmailVerificationUrl(user.EmailVerificationToken, user.Email);

            _logger.LogInformation("Sending verification email to {Email} with link: {VerificationLink}",
                user.Email, verificationLink);

            var emailSent = await _emailService.SendEmailVerificationEmailAsync(user.Email, user.FirstName, verificationLink);

            if (emailSent)
            {
                _logger.LogInformation("Verification email resent successfully to: {Email}", user.Email);
                return Ok(ApiResponse<object>.SuccessResponse(null, "Verification email resent successfully"));
            }
            else
            {
                _logger.LogError("Failed to send verification email to: {Email}", user.Email);
                return StatusCode(500, ApiResponse<object>.FailureResponse("Failed to send verification email"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resending verification to: {Email}", resendDto?.Email ?? "unknown");
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while resending verification"));
        }
    }

    [HttpGet("debug/frontend-url")]
    [ApiExplorerSettings(IgnoreApi = true)] // Hide from Swagger
    public IActionResult DebugFrontendUrl()
    {
        if (!_appSettings.Value.Environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

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