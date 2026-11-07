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
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Api.Services.Auth;

public class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly ILogger<AuthService> _logger;
    private readonly IFrontendUrlService _frontendUrlService;
    private readonly IWeeklyPasswordService _weeklyPasswordService;
    private readonly JwtOptions _jwtOptions;
    private readonly ShmsBackend.Api.Services.Security.ISecurityEventService _events;
    private readonly ShmsBackend.Api.Services.Security.ILoginOtpService _otp;

    public AuthService(
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        IEmailService emailService,
        ITokenBlacklistService tokenBlacklistService,
        ILogger<AuthService> logger,
        IFrontendUrlService frontendUrlService,
        IWeeklyPasswordService weeklyPasswordService,
        IOptions<JwtOptions> jwtOptions,
        ShmsBackend.Api.Services.Security.ISecurityEventService events,
        ShmsBackend.Api.Services.Security.ILoginOtpService otp)
    {
        _events = events;
        _otp = otp;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _emailService = emailService;
        _tokenBlacklistService = tokenBlacklistService;
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

    public async Task<ApiResponse<AuthResponse>> LoginAsync(LoginDto loginDto, string? ip = null)
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
                        await _events.RecordAsync("Auth", "LoginLockout", admin is SuperAdmin ? "Critical" : "High",
                            $"{admin.UserType} account locked after repeated wrong passwords ({admin.Email}).",
                            admin.Id.ToString(), admin.UserType.ToString());
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
            await _unitOfWork.Admins.UpdateAsync(admin);
            await _unitOfWork.SaveChangesAsync();

            // Email and password are correct for this role. No tokens are issued and nothing says who the user is
            // until the second step is done. An account that has its authenticator app asks for the app code only
            // (no email is sent). An account without one gets the emailed code once, then must set the app up.
            if (admin.TotpEnabledAt != null)
            {
                var cid = await _otp.IssueAuthenticatorAsync(admin, ip);
                return ApiResponse<AuthResponse>.SuccessResponse(new AuthResponse
                {
                    RequiresOtp = true, ChallengeId = cid, Method = "authenticator"
                }, "Enter the code from your authenticator app");
            }
            var issued = await _otp.IssueAsync(admin, ip);
            return ApiResponse<AuthResponse>.SuccessResponse(new AuthResponse
            {
                RequiresOtp = true,
                Method = "email",
                ChallengeId = issued.ChallengeId,
                ResendAfterSeconds = issued.ResendAfterSeconds,
                CodeValidMinutes = issued.ValidMinutes,
                MaskedEmail = issued.MaskedEmail
            }, "Enter the code we emailed you");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for email: {Email}", loginDto.Email);
            return ApiResponse<AuthResponse>.FailureResponse("An error occurred during login. Please try again.");
        }
    }

    private async Task<ApiResponse<AuthResponse>> CompleteLoginAsync(Admin admin, List<string>? recoveryCodes = null)
    {
        try
        {
            var accessToken = _tokenService.GenerateAccessToken(admin.Id, admin.Email, admin.UserType);
            var refreshToken = _tokenService.GenerateRefreshToken();

            admin.RefreshToken = refreshToken;
            admin.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays);
            admin.RecoveryRequestedAt = null;   // they got in, so an open recovery request is settled
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
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.AdminAccessTokenExpirationMinutes),
                RecoveryCodes = recoveryCodes
            };

            _logger.LogInformation("User logged in successfully: {Email} as {UserType}",
                admin.Email, admin.UserType);

            return ApiResponse<AuthResponse>.SuccessResponse(authResponse, "Login successful");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing login for {UserId}", admin.Id);
            return ApiResponse<AuthResponse>.FailureResponse("An error occurred during login. Please try again.");
        }
    }

    public async Task<ApiResponse<AuthResponse>> VerifyLoginOtpAsync(VerifyLoginOtpDto dto, string? ip)
    {
        var r = await _otp.VerifyAsync(dto.ChallengeId, dto.Code, ip);
        switch (r.Result)
        {
            case ShmsBackend.Api.Services.Security.OtpResult.Ok:
                // The emailed code proves the mailbox, not the person's device: an account with no authenticator app yet
                // must now set one up. No tokens are issued until that is done.
                if (r.Kind == 0)
                {
                    var enrol = await _otp.IssueEnrolAsync(r.Admin!, ip);
                    return ApiResponse<AuthResponse>.SuccessResponse(new AuthResponse
                    { RequiresEnrolment = true, ChallengeId = enrol, Method = "enrol", FirstName = r.Admin!.FirstName },
                    "Set up your authenticator app to finish signing in");
                }
                return await CompleteLoginAsync(r.Admin!);
            case ShmsBackend.Api.Services.Security.OtpResult.Wrong:
                return ApiResponse<AuthResponse>.FailureResponse(
                    $"That code is not correct. {r.TriesLeft} {(r.TriesLeft == 1 ? "try" : "tries")} left.");
            case ShmsBackend.Api.Services.Security.OtpResult.TooManyTries:
                return ApiResponse<AuthResponse>.FailureResponse("Too many wrong codes. Please sign in again to get a new code.");
            case ShmsBackend.Api.Services.Security.OtpResult.AccountLocked:
                return ApiResponse<AuthResponse>.FailureResponse("This account is locked. Please reset your password to regain access.");
            case ShmsBackend.Api.Services.Security.OtpResult.Expired:
                return ApiResponse<AuthResponse>.FailureResponse("That code has expired. Please sign in again to get a new code.");
            default:
                return ApiResponse<AuthResponse>.FailureResponse("This code is no longer valid. Please sign in again.");
        }
    }

    public async Task<ApiResponse<EnrolSetupDto>> StartEnrolAsync(EnrolStartDto dto)
    {
        var r = await _otp.StartEnrolAsync(dto.ChallengeId);
        if (r.Result != ShmsBackend.Api.Services.Security.OtpResult.Ok)
            return ApiResponse<EnrolSetupDto>.FailureResponse("This set-up link has expired. Please sign in again.");
        return ApiResponse<EnrolSetupDto>.SuccessResponse(new EnrolSetupDto { Secret = r.Secret!, OtpAuthUri = r.Uri!, Account = r.Admin!.Email }, "Scan the QR code with your authenticator app");
    }

    public async Task<ApiResponse<AuthResponse>> ConfirmEnrolAsync(EnrolConfirmDto dto, string? ip)
    {
        var r = await _otp.ConfirmEnrolAsync(dto.ChallengeId, dto.Code, ip);
        switch (r.Result)
        {
            case ShmsBackend.Api.Services.Security.OtpResult.Ok:
                return await CompleteLoginAsync(r.Admin!, r.RecoveryCodes);
            case ShmsBackend.Api.Services.Security.OtpResult.Wrong:
                return ApiResponse<AuthResponse>.FailureResponse($"That code is not correct. {r.TriesLeft} {(r.TriesLeft == 1 ? "try" : "tries")} left. Check that your phone's clock is automatic.");
            case ShmsBackend.Api.Services.Security.OtpResult.TooManyTries:
                return ApiResponse<AuthResponse>.FailureResponse("Too many wrong codes. Please sign in again to restart the set-up.");
            default:
                return ApiResponse<AuthResponse>.FailureResponse("This set-up has expired. Please sign in again.");
        }
    }

    public async Task<ApiResponse<AuthResponse>> ResendLoginOtpAsync(ResendLoginOtpDto dto, string? ip)
    {
        var r = await _otp.ResendAsync(dto.ChallengeId, ip);
        switch (r.Result)
        {
            case ShmsBackend.Api.Services.Security.OtpResult.Ok:
                return ApiResponse<AuthResponse>.SuccessResponse(new AuthResponse
                {
                    RequiresOtp = true, ChallengeId = r.Issued!.ChallengeId, ResendAfterSeconds = r.Issued.ResendAfterSeconds,
                    CodeValidMinutes = r.Issued.ValidMinutes, MaskedEmail = r.Issued.MaskedEmail
                }, "A new code has been sent");
            case ShmsBackend.Api.Services.Security.OtpResult.TooSoon:
                return ApiResponse<AuthResponse>.FailureResponse($"Please wait {r.WaitSeconds} seconds before asking for another code.");
            case ShmsBackend.Api.Services.Security.OtpResult.ResendLimit:
                return ApiResponse<AuthResponse>.FailureResponse("You have asked for too many codes. Please sign in again.");
            default:
                return ApiResponse<AuthResponse>.FailureResponse("This code request is no longer valid. Please sign in again.");
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