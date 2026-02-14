using System;
using System.Linq;
using System.Threading.Tasks;
using BCrypt.Net;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Auth;
using ShmsBackend.Api.Models.DTOs.Email;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.OTP;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Api.Services.Auth;

public class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly IOtpService _otpService;
    private readonly IEmailService _emailService;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly ILogger<AuthService> _logger;
    private readonly IPreAuthCacheService _preAuthCache;

    public AuthService(
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        IOtpService otpService,
        IEmailService emailService,
        ITokenBlacklistService tokenBlacklistService,
        IPreAuthCacheService preAuthCache,
        ILogger<AuthService> logger)
    {
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _otpService = otpService;
        _emailService = emailService;
        _tokenBlacklistService = tokenBlacklistService;
        _preAuthCache = preAuthCache;
        _logger = logger;
    }

    public async Task<ApiResponse<PreAuthResponseDto>> PreLoginAsync(LoginDto loginDto)
    {
        try
        {
            // Check if user exists with the selected type
            var admin = await _unitOfWork.Admins.GetByEmailAndTypeAsync(
                loginDto.Email,
                loginDto.SelectedUserType);

            if (admin == null)
            {
                _logger.LogWarning("Login attempt with non-existent email/type combination: {Email} - {UserType}",
                    loginDto.Email, loginDto.SelectedUserType);
                return ApiResponse<PreAuthResponseDto>.FailureResponse(
                    "Invalid email or user type combination");
            }

            if (!admin.IsActive)
            {
                _logger.LogWarning("Login attempt for inactive user: {Email}", loginDto.Email);
                return ApiResponse<PreAuthResponseDto>.FailureResponse(
                    "Account is inactive. Please contact support.");
            }

            // Generate OTP
            var otp = await _otpService.GenerateOtpAsync($"{loginDto.Email}:{loginDto.SelectedUserType}");

            // Cache pre-auth data
            var preAuthData = new PreAuthDto
            {
                Email = admin.Email,
                SelectedUserType = admin.UserType,
                FirstName = admin.FirstName,
                LastName = admin.LastName
            };
            await _preAuthCache.StorePreAuthDataAsync(admin.Email, admin.UserType, preAuthData);

            // Send OTP email
            var emailSent = await _emailService.SendOtpEmailAsync(new EmailTemplateDto
            {
                To = admin.Email,
                Subject = "Your Login Verification Code",
                RecipientName = $"{admin.FirstName} {admin.LastName}",
                OtpCode = otp,
                ExpiryMinutes = 10
            });

            if (!emailSent)
            {
                _logger.LogError("Failed to send OTP email to: {Email}", admin.Email);
                return ApiResponse<PreAuthResponseDto>.FailureResponse(
                    "Failed to send verification code. Please try again.");
            }

            var response = new PreAuthResponseDto
            {
                Email = admin.Email,
                SelectedUserType = admin.UserType,
                FirstName = admin.FirstName,
                LastName = admin.LastName,
                Message = "Verification code sent to your email"
            };

            _logger.LogInformation("Pre-login successful for: {Email} as {UserType}",
                admin.Email, admin.UserType);

            return ApiResponse<PreAuthResponseDto>.SuccessResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during pre-login for email: {Email}", loginDto.Email);
            return ApiResponse<PreAuthResponseDto>.FailureResponse(
                "An error occurred during login. Please try again.");
        }
    }

    public async Task<ApiResponse<AuthResponse>> VerifyLoginOtpAsync(VerifyLoginDto verifyLoginDto)
    {
        try
        {
            // Validate OTP
            var isOtpValid = await _otpService.ValidateOtpAsync(
                $"{verifyLoginDto.Email}:{verifyLoginDto.SelectedUserType}",
                verifyLoginDto.Otp);

            if (!isOtpValid)
            {
                _logger.LogWarning("Invalid OTP for email/type: {Email} - {UserType}",
                    verifyLoginDto.Email, verifyLoginDto.SelectedUserType);
                return ApiResponse<AuthResponse>.FailureResponse("Invalid or expired verification code");
            }

            // Get pre-auth data from cache
            var preAuthData = await _preAuthCache.GetPreAuthDataAsync(
                verifyLoginDto.Email, verifyLoginDto.SelectedUserType);

            if (preAuthData == null)
            {
                _logger.LogWarning("Pre-auth data expired for: {Email} - {UserType}",
                    verifyLoginDto.Email, verifyLoginDto.SelectedUserType);
                return ApiResponse<AuthResponse>.FailureResponse(
                    "Session expired. Please start login again.");
            }

            // Get user and verify password
            var admin = await _unitOfWork.Admins.GetByEmailAndTypeAsync(
                verifyLoginDto.Email, verifyLoginDto.SelectedUserType);

            if (admin == null)
            {
                _logger.LogError("User not found after OTP validation: {Email}", verifyLoginDto.Email);
                return ApiResponse<AuthResponse>.FailureResponse("User not found");
            }

            // Verify password
            var isPasswordValid = BCrypt.Net.BCrypt.Verify(verifyLoginDto.Password, admin.PasswordHash);
            if (!isPasswordValid)
            {
                _logger.LogWarning("Invalid password for: {Email}", verifyLoginDto.Email);
                return ApiResponse<AuthResponse>.FailureResponse("Invalid password");
            }

            // Generate tokens
            var accessToken = _tokenService.GenerateAccessToken(
                admin.Id, admin.Email, admin.UserType);
            var refreshToken = _tokenService.GenerateRefreshToken();

            // Save refresh token
            admin.RefreshToken = refreshToken;
            admin.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
            await _unitOfWork.Admins.UpdateAsync(admin);
            await _unitOfWork.SaveChangesAsync();

            // Clear pre-auth cache
            await _preAuthCache.RemovePreAuthDataAsync(verifyLoginDto.Email, verifyLoginDto.SelectedUserType);

            var authResponse = new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                UserId = admin.Id,
                Email = admin.Email,
                FirstName = admin.FirstName,
                LastName = admin.LastName,
                UserType = admin.UserType,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            _logger.LogInformation("User logged in successfully: {Email} as {UserType}",
                admin.Email, admin.UserType);

            return ApiResponse<AuthResponse>.SuccessResponse(authResponse, "Login successful");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OTP verification for email: {Email}",
                verifyLoginDto.Email);
            return ApiResponse<AuthResponse>.FailureResponse(
                "An error occurred during verification. Please try again.");
        }
    }

    public async Task<ApiResponse<AuthResponse>> RefreshTokenAsync(RefreshTokenDto refreshTokenDto)
    {
        try
        {
            var admin = await _unitOfWork.Admins
                .GetFirstOrDefaultAsync(a => a.RefreshToken == refreshTokenDto.RefreshToken);

            if (admin == null)
            {
                _logger.LogWarning("Invalid refresh token provided");
                return ApiResponse<AuthResponse>.FailureResponse("Invalid refresh token");
            }

            if (admin.RefreshTokenExpiryTime < DateTime.UtcNow)
            {
                _logger.LogWarning("Expired refresh token for user: {Email}", admin.Email);
                return ApiResponse<AuthResponse>.FailureResponse("Refresh token expired");
            }

            var accessToken = _tokenService.GenerateAccessToken(admin.Id, admin.Email, admin.UserType);
            var newRefreshToken = _tokenService.GenerateRefreshToken();

            admin.RefreshToken = newRefreshToken;
            admin.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
            await _unitOfWork.Admins.UpdateAsync(admin);
            await _unitOfWork.SaveChangesAsync();

            var authResponse = new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = newRefreshToken,
                UserId = admin.Id,
                Email = admin.Email,
                FirstName = admin.FirstName,
                LastName = admin.LastName,
                UserType = admin.UserType,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            };

            _logger.LogInformation("Token refreshed successfully for user: {Email}", admin.Email);
            return ApiResponse<AuthResponse>.SuccessResponse(authResponse, "Token refreshed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return ApiResponse<AuthResponse>.FailureResponse(
                "An error occurred during token refresh. Please try again.");
        }
    }

    public async Task<ApiResponse<string>> LogoutAsync(string token)
    {
        try
        {
            await _tokenBlacklistService.BlacklistTokenAsync(token, TimeSpan.FromHours(1));
            _logger.LogInformation("User logged out successfully");
            return ApiResponse<string>.SuccessResponse("Logged out successfully", "Logout successful");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return ApiResponse<string>.FailureResponse(
                "An error occurred during logout. Please try again.");
        }
    }

    public async Task<ApiResponse<string>> RequestPasswordResetAsync(RequestPasswordResetDto requestPasswordResetDto)
    {
        try
        {
            var admin = await _unitOfWork.Admins.GetByEmailAsync(requestPasswordResetDto.Email);

            if (admin == null)
            {
                _logger.LogWarning("Password reset requested for non-existent email: {Email}",
                    requestPasswordResetDto.Email);
                return ApiResponse<string>.SuccessResponse(
                    "If an account exists with this email, a password reset link has been sent",
                    "Password reset email sent");
            }

            var resetToken = Guid.NewGuid().ToString();
            admin.PasswordResetToken = resetToken;
            admin.PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(1);
            await _unitOfWork.Admins.UpdateAsync(admin);
            await _unitOfWork.SaveChangesAsync();

            var resetLink = $"https://your-frontend-url.com/reset-password?token={resetToken}";
            await _emailService.SendPasswordResetEmailAsync(admin.Email, admin.FirstName, resetLink);

            _logger.LogInformation("Password reset requested for: {Email}", admin.Email);
            return ApiResponse<string>.SuccessResponse(
                "If an account exists with this email, a password reset link has been sent",
                "Password reset email sent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during password reset request for email: {Email}",
                requestPasswordResetDto.Email);
            return ApiResponse<string>.FailureResponse("An error occurred. Please try again.");
        }
    }
}