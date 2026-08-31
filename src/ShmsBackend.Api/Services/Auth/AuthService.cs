using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BCrypt.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShmsBackend.Api.Configuration;
using ShmsBackend.Api.Models.DTOs.Auth;
using ShmsBackend.Api.Models.DTOs.Email;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.OTP;
using ShmsBackend.Api.Services.Common;
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
    private readonly IFrontendUrlService _frontendUrlService;
    private readonly IWeeklyPasswordService _weeklyPasswordService;
    private readonly JwtOptions _jwtOptions;

    public AuthService(
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        IOtpService otpService,
        IEmailService emailService,
        ITokenBlacklistService tokenBlacklistService,
        IPreAuthCacheService preAuthCache,
        ILogger<AuthService> logger,
        IFrontendUrlService frontendUrlService,
        IWeeklyPasswordService weeklyPasswordService,
        IOptions<JwtOptions> jwtOptions)
    {
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _otpService = otpService;
        _emailService = emailService;
        _tokenBlacklistService = tokenBlacklistService;
        _preAuthCache = preAuthCache;
        _logger = logger;
        _frontendUrlService = frontendUrlService;
        _weeklyPasswordService = weeklyPasswordService;
        _jwtOptions = jwtOptions.Value;
    }

    /// <summary>
    /// True if this admin is subscribed to the weekly shared password AND the supplied password
    /// matches the current active weekly password. Used as an alternate credential at login —
    /// a match here counts as a normal successful login and does NOT increment failed attempts.
    /// </summary>
    private async Task<bool> TryWeeklyPasswordAsync(Guid adminId, string password)
    {
        if (!await _weeklyPasswordService.IsSubscribedAsync(adminId))
            return false;

        var weeklyHash = await _weeklyPasswordService.GetCurrentPasswordHashAsync();
        return !string.IsNullOrEmpty(weeklyHash)
            && BCrypt.Net.BCrypt.Verify(password, weeklyHash);
    }

    public async Task<ApiResponse<AuthResponse>> LoginAsync(LoginDto loginDto)
    {
        try
        {
            var admin = await _unitOfWork.Admins.GetByEmailAndTypeAsync(
                loginDto.Email, loginDto.SelectedUserType);

            if (admin == null)
            {
                _logger.LogWarning("Login attempt with non-existent email/type: {Email} - {UserType}",
                    loginDto.Email, loginDto.SelectedUserType);
                return ApiResponse<AuthResponse>.FailureResponse("Invalid email, password, or user type");
            }

            if (admin.IsLockedOut)
            {
                _logger.LogWarning("Login attempt for locked-out account: {Email}", loginDto.Email);
                return ApiResponse<AuthResponse>.FailureResponse(
                    "This account is locked due to too many failed login attempts. Please reset your password to regain access.");
            }

            if (admin.IsDeleted)
                return ApiResponse<AuthResponse>.FailureResponse("This account no longer exists.");

            if (!admin.IsActive)
            {
                _logger.LogWarning("Login attempt for inactive user: {Email}", loginDto.Email);
                return ApiResponse<AuthResponse>.FailureResponse("Account is inactive. Please contact support.");
            }

            if (!admin.IsEmailVerified)
            {
                _logger.LogWarning("Login attempt for unverified email: {Email}", loginDto.Email);
                return ApiResponse<AuthResponse>.FailureResponse(
                    "Email not verified. Please check your email for a verification link.");
            }

            if (!BCrypt.Net.BCrypt.Verify(loginDto.Password, admin.PasswordHash))
            {
                if (!await TryWeeklyPasswordAsync(admin.Id, loginDto.Password))
                {
                    _logger.LogWarning("Invalid password for: {Email}", loginDto.Email);

                    admin.FailedLoginAttempts++;
                    var nowLockedOut = admin.FailedLoginAttempts >= 4;
                    if (nowLockedOut) admin.IsLockedOut = true;
                    await _unitOfWork.Admins.UpdateAsync(admin);
                    await _unitOfWork.SaveChangesAsync();

                    if (nowLockedOut)
                    {
                        try { await _emailService.SendAccountLockedEmailAsync(admin.Email, admin.FirstName); }
                        catch (Exception ex) { _logger.LogWarning(ex, "Failed to send account-locked email to {Email}", admin.Email); }
                    }

                    return ApiResponse<AuthResponse>.FailureResponse(nowLockedOut
                        ? "This account is locked due to too many failed login attempts. Please reset your password to regain access."
                        : "Invalid email, password, or user type");
                }

                _logger.LogInformation("Weekly shared password accepted for login: {Email}", loginDto.Email);
            }

            admin.FailedLoginAttempts = 0;

            var accessToken = _tokenService.GenerateAccessToken(admin.Id, admin.Email, admin.UserType);
            var refreshToken = _tokenService.GenerateRefreshToken();

            admin.RefreshToken = refreshToken;
            admin.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays);
            await _unitOfWork.Admins.UpdateAsync(admin);
            await _unitOfWork.SaveChangesAsync();

            var authResponse = new AuthResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                UserId = admin.Id,
                Email = admin.Email,
                FirstName = admin.FirstName,
                LastName = admin.LastName,
                UserType = admin.UserType,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.AdminAccessTokenExpirationMinutes)
            };

            _logger.LogInformation("User logged in successfully: {Email} as {UserType}",
                admin.Email, admin.UserType);

            return ApiResponse<AuthResponse>.SuccessResponse(authResponse, "Login successful");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for email: {Email}", loginDto.Email);
            return ApiResponse<AuthResponse>.FailureResponse("An error occurred during login. Please try again.");
        }
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

            // CHECK EMAIL VERIFICATION
            if (!admin.IsEmailVerified)
            {
                _logger.LogWarning("Login attempt for unverified email: {Email}", loginDto.Email);
                return ApiResponse<PreAuthResponseDto>.FailureResponse(
                    "Email not verified. Please check your email for verification link or contact your administrator.");
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

            if (admin.IsLockedOut)
            {
                _logger.LogWarning("OTP-verify attempt for locked-out account: {Email}", verifyLoginDto.Email);
                return ApiResponse<AuthResponse>.FailureResponse(
                    "This account is locked due to too many failed login attempts. Please reset your password to regain access.");
            }

            // Verify password
            var isPasswordValid = BCrypt.Net.BCrypt.Verify(verifyLoginDto.Password, admin.PasswordHash);
            if (!isPasswordValid)
            {
                if (!await TryWeeklyPasswordAsync(admin.Id, verifyLoginDto.Password))
                {
                    _logger.LogWarning("Invalid password for: {Email}", verifyLoginDto.Email);

                    admin.FailedLoginAttempts++;
                    var nowLockedOut = admin.FailedLoginAttempts >= 4;
                    if (nowLockedOut) admin.IsLockedOut = true;
                    await _unitOfWork.Admins.UpdateAsync(admin);
                    await _unitOfWork.SaveChangesAsync();

                    if (nowLockedOut)
                    {
                        try { await _emailService.SendAccountLockedEmailAsync(admin.Email, admin.FirstName); }
                        catch (Exception ex) { _logger.LogWarning(ex, "Failed to send account-locked email to {Email}", admin.Email); }
                    }

                    return ApiResponse<AuthResponse>.FailureResponse(nowLockedOut
                        ? "This account is locked due to too many failed login attempts. Please reset your password to regain access."
                        : "Invalid password");
                }

                _logger.LogInformation("Weekly shared password accepted for login (OTP step): {Email}", verifyLoginDto.Email);
            }

            admin.FailedLoginAttempts = 0;

            // Generate tokens
            var accessToken = _tokenService.GenerateAccessToken(
                admin.Id, admin.Email, admin.UserType);
            var refreshToken = _tokenService.GenerateRefreshToken();

            // Save refresh token
            admin.RefreshToken = refreshToken;
            admin.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays);
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
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.AdminAccessTokenExpirationMinutes)
            };

            _logger.LogInformation("User logged in successfully: {Email} as {UserType}. Token expires in {ExpirationMinutes} minutes",
                admin.Email, admin.UserType, _jwtOptions.AdminAccessTokenExpirationMinutes);

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

            if (!admin.IsActive || admin.IsDeleted || admin.IsLockedOut)
            {
                _logger.LogWarning("Refresh token used for inactive/locked/deleted account: {Email}", admin.Email);
                return ApiResponse<AuthResponse>.FailureResponse("Account is no longer active.");
            }

            if (admin.RefreshTokenExpiryTime < DateTime.UtcNow)
            {
                _logger.LogWarning("Expired refresh token for user: {Email}", admin.Email);
                return ApiResponse<AuthResponse>.FailureResponse("Refresh token expired");
            }

            var accessToken = _tokenService.GenerateAccessToken(admin.Id, admin.Email, admin.UserType);
            var newRefreshToken = _tokenService.GenerateRefreshToken();

            admin.RefreshToken = newRefreshToken;
            admin.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays);
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
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.AdminAccessTokenExpirationMinutes)
            };

            _logger.LogInformation("Token refreshed successfully for user: {Email}. New token expires in {ExpirationMinutes} minutes",
                admin.Email, _jwtOptions.AdminAccessTokenExpirationMinutes);

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
            await _tokenBlacklistService.BlacklistTokenAsync(token, TimeSpan.FromHours(24));
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
            var admin = await _unitOfWork.Admins.GetByEmailAndTypeAsync(requestPasswordResetDto.Email, requestPasswordResetDto.UserType);

            if (admin == null || !admin.IsActive)
            {
                _logger.LogWarning("Password reset requested for non-existent/inactive email: {Email}",
                    requestPasswordResetDto.Email);
                return ApiResponse<string>.SuccessResponse(
                    "If an account exists with this email, a password reset link has been sent",
                    "Password reset email sent");
            }

            var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            admin.PasswordResetToken = BCrypt.Net.BCrypt.HashPassword(otp);
            admin.PasswordResetTokenExpiry = DateTime.UtcNow.AddMinutes(15);
            admin.PasswordResetAttempts = 0;
            await _unitOfWork.Admins.UpdateAsync(admin);
            await _unitOfWork.SaveChangesAsync();

            await _emailService.SendPasswordResetOtpEmailAsync(admin.Email, admin.FirstName, otp);

            _logger.LogInformation("Password reset OTP requested for: {Email}", admin.Email);
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

    public async Task<ApiResponse<string>> ResetPasswordAsync(ResetPasswordDto dto)
    {
        try
        {
            var admin = await _unitOfWork.Admins.GetByEmailAndTypeAsync(dto.Email, dto.UserType);
            if (admin == null)
                return ApiResponse<string>.FailureResponse("Invalid request.");

            if (string.IsNullOrEmpty(admin.PasswordResetToken))
                return ApiResponse<string>.FailureResponse("Invalid OTP code.");

            if (admin.PasswordResetTokenExpiry == null || admin.PasswordResetTokenExpiry < DateTime.UtcNow)
                return ApiResponse<string>.FailureResponse("OTP code has expired. Please request a new one.");

            if (!BCrypt.Net.BCrypt.Verify(dto.Otp, admin.PasswordResetToken))
            {
                admin.PasswordResetAttempts++;
                if (admin.PasswordResetAttempts >= 5)
                {
                    admin.PasswordResetToken = null;
                    admin.PasswordResetTokenExpiry = null;
                    admin.PasswordResetAttempts = 0;
                    await _unitOfWork.Admins.UpdateAsync(admin);
                    await _unitOfWork.SaveChangesAsync();
                    return ApiResponse<string>.FailureResponse(
                        "Too many incorrect attempts. Please request a new OTP code.");
                }
                await _unitOfWork.Admins.UpdateAsync(admin);
                await _unitOfWork.SaveChangesAsync();
                return ApiResponse<string>.FailureResponse("Invalid OTP code.");
            }

            admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            admin.PasswordResetToken = null;
            admin.PasswordResetTokenExpiry = null;
            admin.PasswordResetAttempts = 0;
            admin.FailedLoginAttempts = 0;
            admin.IsLockedOut = false;
            admin.UpdatedAt = DateTime.UtcNow;

            await _unitOfWork.Admins.UpdateAsync(admin);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Password reset successful for: {Email} type: {Type}", dto.Email, dto.UserType);
            return ApiResponse<string>.SuccessResponse("Password reset successfully.", "Password updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during password reset for: {Email}", dto.Email);
            return ApiResponse<string>.FailureResponse("An error occurred. Please try again.");
        }
    }

    public async Task<ApiResponse<string>> ChangePasswordAsync(Guid adminId, ChangePasswordDto dto)
    {
        try
        {
            var admin = await _unitOfWork.Admins.GetByIdAsync(adminId);
            if (admin == null)
                return ApiResponse<string>.FailureResponse("Invalid request.");

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, admin.PasswordHash))
            {
                _logger.LogWarning("Change password failed - incorrect current password for admin: {AdminId}", adminId);
                return ApiResponse<string>.FailureResponse("Current password is incorrect");
            }

            admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            admin.UpdatedAt = DateTime.UtcNow;

            await _unitOfWork.Admins.UpdateAsync(admin);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Password changed successfully for admin: {AdminId}", adminId);
            return ApiResponse<string>.SuccessResponse("Password changed successfully.", "Password updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during password change for admin: {AdminId}", adminId);
            return ApiResponse<string>.FailureResponse("An error occurred. Please try again.");
        }
    }
}