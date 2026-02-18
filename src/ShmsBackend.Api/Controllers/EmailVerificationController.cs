using Microsoft.AspNetCore.Mvc;
using ShmsBackend.Api.Models.DTOs.Auth;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Data.Repositories.Interfaces;
using BCrypt.Net;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmailVerificationController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailVerificationController> _logger;

    public EmailVerificationController(
        IUnitOfWork unitOfWork,
        IEmailService emailService,
        ILogger<EmailVerificationController> logger)
    {
        _unitOfWork = unitOfWork;
        _emailService = emailService;
        _logger = logger;
    }

    [HttpPost("verify")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailDto verifyEmailDto)
    {
        try
        {
            var user = await _unitOfWork.Admins.GetByEmailAsync(verifyEmailDto.Email);

            if (user == null)
            {
                return NotFound(ApiResponse<object>.FailureResponse("User not found"));
            }

            if (user.IsEmailVerified)
            {
                return BadRequest(ApiResponse<object>.FailureResponse("Email already verified"));
            }

            if (user.EmailVerificationToken != verifyEmailDto.Token)
            {
                return BadRequest(ApiResponse<object>.FailureResponse("Invalid verification token"));
            }

            if (user.EmailVerificationTokenExpiry < DateTime.UtcNow)
            {
                return BadRequest(ApiResponse<object>.FailureResponse("Verification token has expired"));
            }

            // Token is valid - return success so frontend can show set password form
            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                email = user.Email,
                token = user.EmailVerificationToken
            }, "Email verified. Please set your password."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying email");
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred"));
        }
    }

    [HttpPost("set-password")]
    public async Task<IActionResult> SetPassword([FromBody] SetPasswordDto setPasswordDto)
    {
        try
        {
            var user = await _unitOfWork.Admins.GetByEmailAsync(setPasswordDto.Email);

            if (user == null)
            {
                return NotFound(ApiResponse<object>.FailureResponse("User not found"));
            }

            if (user.EmailVerificationToken != setPasswordDto.Token)
            {
                return BadRequest(ApiResponse<object>.FailureResponse("Invalid verification token"));
            }

            if (user.EmailVerificationTokenExpiry < DateTime.UtcNow)
            {
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

            _logger.LogInformation("User {Email} verified email and set password", user.Email);

            return Ok(ApiResponse<object>.SuccessResponse(null, "Password set successfully. You can now login."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting password");
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred"));
        }
    }

    [HttpPost("resend")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationDto resendDto)
    {
        try
        {
            var user = await _unitOfWork.Admins.GetByEmailAsync(resendDto.Email);

            if (user == null)
            {
                return NotFound(ApiResponse<object>.FailureResponse("User not found"));
            }

            if (user.IsEmailVerified)
            {
                return BadRequest(ApiResponse<object>.FailureResponse("Email already verified"));
            }

            // Generate new verification token
            user.EmailVerificationToken = GenerateVerificationToken();
            user.EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24);

            await _unitOfWork.Admins.UpdateAsync(user);
            await _unitOfWork.SaveChangesAsync();

            // Send new verification email
            var verificationLink = $"https://your-frontend.com/verify-email?token={user.EmailVerificationToken}&email={user.Email}";
            await _emailService.SendEmailVerificationEmailAsync(user.Email, user.FirstName, verificationLink);

            return Ok(ApiResponse<object>.SuccessResponse(null, "Verification email resent"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resending verification");
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred"));
        }
    }

    private string GenerateVerificationToken()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("/", "_")
            .Replace("+", "-")
            .TrimEnd('=');
    }
}