using System;
using System.Threading.Tasks;
using BCrypt.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShmsBackend.Api.Configuration;
using ShmsBackend.Api.Models.DTOs.PortalAuth;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Auth;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Api.Services.PortalAuth;

public class PortalAuthService : IPortalAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly IFrontendUrlService _frontendUrlService;
    private readonly ILogger<PortalAuthService> _logger;
    private readonly JwtOptions _jwtOptions;

    public PortalAuthService(
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        IEmailService emailService,
        ITokenBlacklistService tokenBlacklistService,
        IFrontendUrlService frontendUrlService,
        ILogger<PortalAuthService> logger,
        IOptions<JwtOptions> jwtOptions)
    {
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _emailService = emailService;
        _tokenBlacklistService = tokenBlacklistService;
        _frontendUrlService = frontendUrlService;
        _logger = logger;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task<ApiResponse<PortalAuthResponse>> LoginAsync(PortalLoginDto dto)
    {
        try
        {
            var user = await _unitOfWork.PortalUsers.GetByEmailAndTypeAsync(
                dto.Email, dto.PortalUserType);

            if (user == null)
            {
                _logger.LogWarning("Portal login attempt with unknown email/type: {Email} {Type}",
                    dto.Email, dto.PortalUserType);
                return ApiResponse<PortalAuthResponse>.FailureResponse(
                    "Invalid email, password, or account type.");
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Portal login attempt for inactive account: {Email}", dto.Email);
                return ApiResponse<PortalAuthResponse>.FailureResponse(
                    "Account is inactive. Please contact support.");
            }

            if (!user.IsEmailVerified)
            {
                _logger.LogWarning("Portal login attempt for unverified email: {Email}", dto.Email);
                return ApiResponse<PortalAuthResponse>.FailureResponse(
                    "Email not verified. Please check your inbox for the verification link.");
            }

            if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            {
                _logger.LogWarning("Portal login: invalid password for {Email}", dto.Email);
                return ApiResponse<PortalAuthResponse>.FailureResponse(
                    "Invalid email, password, or account type.");
            }

            var accessToken = _tokenService.GeneratePortalAccessToken(
                user.Id, user.Email, user.PortalUserType);
            var refreshToken = _tokenService.GenerateRefreshToken();

            user.RefreshToken = refreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays);
            await _unitOfWork.PortalUsers.UpdateAsync(user);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Portal login successful: {Email} as {Type}", user.Email, user.PortalUserType);

            return ApiResponse<PortalAuthResponse>.SuccessResponse(new PortalAuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                UserId = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.PortalUserType,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes)
            }, "Login successful");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during portal login for {Email}", dto.Email);
            return ApiResponse<PortalAuthResponse>.FailureResponse(
                "An error occurred during login. Please try again.");
        }
    }

    public async Task<ApiResponse<string>> RegisterExplorerAsync(RegisterExplorerDto dto)
    {
        try
        {
            // Prevent duplicate registration across all portal user types
            var existing = await _unitOfWork.PortalUsers.GetByEmailAsync(dto.Email);
            if (existing != null)
            {
                _logger.LogWarning("Explorer registration attempt with existing email: {Email}", dto.Email);
                return ApiResponse<string>.FailureResponse(
                    "An account with this email already exists.");
            }

            var verificationToken = Guid.NewGuid().ToString("N");

            var explorer = new Explorer
            {
                Id = Guid.NewGuid(),
                Email = dto.Email.ToLower().Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, 12),
                FirstName = dto.FirstName.Trim(),
                LastName = dto.LastName.Trim(),
                PhoneNumber = dto.PhoneNumber?.Trim(),
                County = dto.County,
                Constituency = dto.Constituency,
                Ward = dto.Ward,
                IsActive = true,
                IsEmailVerified = false,
                EmailVerificationToken = verificationToken,
                EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Explorers.AddAsync(explorer);
            await _unitOfWork.SaveChangesAsync();

            var verificationLink = _frontendUrlService.GetEmailVerificationUrl(
                verificationToken, dto.Email);

            var emailSent = await _emailService.SendEmailVerificationEmailAsync(
                explorer.Email, explorer.FirstName, verificationLink);

            if (!emailSent)
            {
                _logger.LogError("Failed to send verification email to Explorer: {Email}", explorer.Email);
            }

            try
            {
                await _emailService.SendWelcomeEmailAsync(
                    explorer.Email,
                    explorer.FirstName,
                    "Use the password you registered with to log in to the Romah Client Portal."
                );
                _logger.LogInformation("Welcome email sent to explorer {Email}", explorer.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send welcome email to explorer {Email}", explorer.Email);
            }

            _logger.LogInformation("Explorer registered: {Email}", explorer.Email);

            return ApiResponse<string>.SuccessResponse(
                "Registration successful. Please check your email to verify your account before logging in.",
                "Registration successful");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Explorer registration for {Email}", dto.Email);
            return ApiResponse<string>.FailureResponse(
                "An error occurred during registration. Please try again.");
        }
    }

    public async Task<ApiResponse<PortalAuthResponse>> RefreshTokenAsync(PortalRefreshTokenDto dto)
    {
        try
        {
            var user = await _unitOfWork.PortalUsers.GetFirstOrDefaultAsync(
                u => u.RefreshToken == dto.RefreshToken);

            if (user == null)
            {
                _logger.LogWarning("Portal refresh: invalid refresh token");
                return ApiResponse<PortalAuthResponse>.FailureResponse("Invalid refresh token.");
            }

            if (user.RefreshTokenExpiryTime == null || user.RefreshTokenExpiryTime < DateTime.UtcNow)
            {
                _logger.LogWarning("Portal refresh: expired refresh token for {Email}", user.Email);
                return ApiResponse<PortalAuthResponse>.FailureResponse(
                    "Refresh token has expired. Please log in again.");
            }

            var accessToken = _tokenService.GeneratePortalAccessToken(
                user.Id, user.Email, user.PortalUserType);
            var newRefreshToken = _tokenService.GenerateRefreshToken();

            user.RefreshToken = newRefreshToken;
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays);
            await _unitOfWork.PortalUsers.UpdateAsync(user);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Portal token refreshed for {Email}", user.Email);

            return ApiResponse<PortalAuthResponse>.SuccessResponse(new PortalAuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = newRefreshToken,
                UserId = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.PortalUserType,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes)
            }, "Token refreshed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during portal token refresh");
            return ApiResponse<PortalAuthResponse>.FailureResponse(
                "An error occurred during token refresh. Please try again.");
        }
    }

    public async Task<ApiResponse<string>> LogoutAsync(string token)
    {
        try
        {
            await _tokenBlacklistService.BlacklistTokenAsync(token, TimeSpan.FromHours(24));
            _logger.LogInformation("Portal user logged out successfully");
            return ApiResponse<string>.SuccessResponse("Logged out successfully", "Logout successful");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during portal logout");
            return ApiResponse<string>.FailureResponse(
                "An error occurred during logout. Please try again.");
        }
    }

    public async Task<ApiResponse<string>> RequestPasswordResetAsync(PortalRequestPasswordResetDto dto)
    {
        // Always return the same message to prevent user enumeration
        const string genericMessage =
            "If an account exists with this email, a password reset code has been sent.";

        try
        {
            var user = await _unitOfWork.PortalUsers.GetByEmailAndTypeAsync(
                dto.Email, dto.PortalUserType);

            if (user == null || !user.IsActive)
            {
                _logger.LogWarning("Portal password reset request for unknown/inactive: {Email} {Type}",
                    dto.Email, dto.PortalUserType);
                return ApiResponse<string>.SuccessResponse(genericMessage, "Password reset requested");
            }

            // 6-digit OTP, single use, 15-minute expiry
            var otp = new Random().Next(100000, 999999).ToString();
            user.PasswordResetToken = otp;
            user.PasswordResetTokenExpiry = DateTime.UtcNow.AddMinutes(15);
            await _unitOfWork.PortalUsers.UpdateAsync(user);
            await _unitOfWork.SaveChangesAsync();

            var emailSent = await _emailService.SendPasswordResetOtpEmailAsync(
                user.Email, user.FirstName, otp);

            if (!emailSent)
            {
                _logger.LogError("Failed to send password reset email to: {Email}", user.Email);
            }

            _logger.LogInformation("Portal password reset OTP issued for {Email} {Type}",
                user.Email, user.PortalUserType);

            return ApiResponse<string>.SuccessResponse(genericMessage, "Password reset requested");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during portal password reset request for {Email}", dto.Email);
            return ApiResponse<string>.FailureResponse(
                "An error occurred. Please try again.");
        }
    }

    public async Task<ApiResponse<string>> ResetPasswordAsync(PortalResetPasswordDto dto)
    {
        try
        {
            var user = await _unitOfWork.PortalUsers.GetByEmailAndTypeAsync(
                dto.Email, dto.PortalUserType);

            if (user == null)
                return ApiResponse<string>.FailureResponse("Invalid request.");

            if (string.IsNullOrEmpty(user.PasswordResetToken) || user.PasswordResetToken != dto.Otp)
                return ApiResponse<string>.FailureResponse("Invalid OTP code.");

            if (user.PasswordResetTokenExpiry == null || user.PasswordResetTokenExpiry < DateTime.UtcNow)
                return ApiResponse<string>.FailureResponse(
                    "OTP code has expired. Please request a new one.");

            // Hash with cost 12, clear OTP (single use)
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword, 12);
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpiry = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _unitOfWork.PortalUsers.UpdateAsync(user);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Portal password reset successful for {Email} {Type}",
                user.Email, user.PortalUserType);

            return ApiResponse<string>.SuccessResponse(
                "Password reset successfully.", "Password updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during portal password reset for {Email}", dto.Email);
            return ApiResponse<string>.FailureResponse(
                "An error occurred. Please try again.");
        }
    }
}
