using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShmsBackend.Api.Configuration;
using ShmsBackend.Api.Models.DTOs.PortalAuth;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Agreements;
using ShmsBackend.Api.Services.Auth;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Models.Enums;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Api.Services.PortalAuth;

public class PortalAuthService : IPortalAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly IFrontendUrlService _frontendUrlService;
    private readonly IWeeklyClientPasswordService _weeklyClientPasswordService;
    private readonly IAgreementService _agreementService;
    private readonly ILogger<PortalAuthService> _logger;
    private readonly JwtOptions _jwtOptions;
    private readonly ShmsDbContext _context;

    public PortalAuthService(
        IUnitOfWork unitOfWork,
        ITokenService tokenService,
        IEmailService emailService,
        INotificationService notificationService,
        ITokenBlacklistService tokenBlacklistService,
        IFrontendUrlService frontendUrlService,
        IWeeklyClientPasswordService weeklyClientPasswordService,
        IAgreementService agreementService,
        ILogger<PortalAuthService> logger,
        IOptions<JwtOptions> jwtOptions,
        ShmsDbContext context)
    {
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _emailService = emailService;
        _notificationService = notificationService;
        _tokenBlacklistService = tokenBlacklistService;
        _frontendUrlService = frontendUrlService;
        _weeklyClientPasswordService = weeklyClientPasswordService;
        _agreementService = agreementService;
        _logger = logger;
        _jwtOptions = jwtOptions.Value;
        _context = context;
    }

    /// <summary>
    /// True if the supplied password matches the current active weekly client-portal support password.
    /// This is a UNIVERSAL staff support tool — there is deliberately no per-user / subscription check,
    /// so it applies to any portal account (Tenant/Landlord/Agent/Explorer). A match here counts as a
    /// normal successful login and does NOT increment failed attempts.
    /// </summary>
    private async Task<bool> TryClientWeeklyPasswordAsync(string password)
    {
        _logger.LogInformation("WEEKLY-DEBUG: Checking client weekly password for login attempt");

        var currentHash = await _weeklyClientPasswordService.GetCurrentPasswordHashAsync();
        _logger.LogInformation("WEEKLY-DEBUG: Current active client password hash exists: {Exists}", currentHash != null);

        if (currentHash == null)
        {
            _logger.LogWarning("WEEKLY-DEBUG: No active WeeklyClientPassword row found — check failed at this point");
            return false;
        }

        _logger.LogInformation("WEEKLY-DEBUG: Attempting BCrypt.Verify against current client password hash");
        var result = BCrypt.Net.BCrypt.Verify(password, currentHash);
        _logger.LogInformation("WEEKLY-DEBUG: BCrypt.Verify result: {Result}", result);

        return result;
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

            if (user.IsLockedOut)
            {
                _logger.LogWarning("Portal login attempt for locked-out account: {Email}", dto.Email);
                return ApiResponse<PortalAuthResponse>.FailureResponse(
                    "This account is locked due to too many failed login attempts. Please reset your password to regain access.");
            }

            if (user.IsDeleted)
                return ApiResponse<PortalAuthResponse>.FailureResponse("This account no longer exists.");

            if (!user.IsActive)
            {
                _logger.LogWarning("Portal login attempt for inactive account: {Email}", dto.Email);
                return ApiResponse<PortalAuthResponse>.FailureResponse(
                    "Your account has been deactivated. Please contact management.");
            }

            if (!user.IsEmailVerified)
            {
                _logger.LogWarning("Portal login attempt for unverified email: {Email}", dto.Email);
                return ApiResponse<PortalAuthResponse>.FailureResponse(
                    "Please verify your email address before logging in. Check your inbox for the verification link.");
            }

            if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            {
                _logger.LogInformation("WEEKLY-DEBUG: User's own password failed, falling back to weekly client password check for user {Email}", user.Email);

                if (!await TryClientWeeklyPasswordAsync(dto.Password))
                {
                    _logger.LogWarning("Portal login: invalid password for {Email}", dto.Email);

                    user.FailedLoginAttempts++;
                    var nowLockedOut = user.FailedLoginAttempts >= 4;
                    if (nowLockedOut) user.IsLockedOut = true;
                    await _unitOfWork.PortalUsers.UpdateAsync(user);
                    await _unitOfWork.SaveChangesAsync();

                    if (nowLockedOut)
                    {
                        try { await _emailService.SendAccountLockedEmailAsync(user.Email, user.FirstName); }
                        catch (Exception ex) { _logger.LogWarning(ex, "Failed to send account-locked email to {Email}", user.Email); }
                    }

                    return ApiResponse<PortalAuthResponse>.FailureResponse(nowLockedOut
                        ? "This account is locked due to too many failed login attempts. Please reset your password to regain access."
                        : "Invalid email, password, or account type.");
                }

                _logger.LogInformation("Client portal support password accepted for login: {Email} ({Type})",
                    dto.Email, dto.PortalUserType);
            }

            user.FailedLoginAttempts = 0;

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
                Role = user.PortalUserType.ToString(),
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.PortalAccessTokenExpirationMinutes)
            }, "Login successful");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login error: {Message} | Inner: {Inner}", ex.Message, ex.InnerException?.Message);
            return ApiResponse<PortalAuthResponse>.FailureResponse("Login failed. Please try again.");
        }
    }

    public async Task<ApiResponse<string>> RegisterExplorerAsync(RegisterExplorerDto dto)
    {
        try
        {
            var existingExplorer = await _context.Explorers
                .FirstOrDefaultAsync(u => u.Email.ToLower() == dto.Email.ToLower());

            if (existingExplorer != null)
            {
                _logger.LogWarning("Explorer registration attempt with existing email: {Email}", dto.Email);
                return ApiResponse<string>.FailureResponse(
                    "An Explorer account with this email already exists.");
            }

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
                IsEmailVerified = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Explorers.AddAsync(explorer);
            await _unitOfWork.SaveChangesAsync();

            try
            {
                await _emailService.SendExplorerWelcomeEmailAsync(
                    explorer.Email,
                    explorer.FirstName,
                    _frontendUrlService.GetPortalLoginUrl()
                );
                _logger.LogInformation("Welcome email sent to explorer {Email}", explorer.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send welcome email to explorer {Email}", explorer.Email);
            }

            try
            {
                await _notificationService.SendToUserAsync(
                    explorer.Id.ToString(),
                    "Welcome to Romah! Browse available properties in your area.",
                    "general"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send welcome notification to explorer {Email}", explorer.Email);
            }

            try
            {
                await _notificationService.SendToRolesAsync(
                    new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin },
                    $"New explorer {explorer.FirstName} {explorer.LastName} has self-registered.",
                    "user");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send explorer registration notification to admins");
            }

            _logger.LogInformation("Explorer registered: {Email}", explorer.Email);

            return ApiResponse<string>.SuccessResponse(
                "Registration successful. You can now log in to the Romah Client Portal.",
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

            if (!user.IsActive || user.IsDeleted || user.IsLockedOut)
            {
                _logger.LogWarning("Portal refresh token used for inactive/locked/deleted account: {Email}", user.Email);
                return ApiResponse<PortalAuthResponse>.FailureResponse("Account is no longer active.");
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
                Role = user.PortalUserType.ToString(),
                ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.PortalAccessTokenExpirationMinutes)
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

            // 6-digit OTP, single use, 15-minute expiry — stored hashed, never in plaintext
            var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            user.PasswordResetToken = BCrypt.Net.BCrypt.HashPassword(otp);
            user.PasswordResetTokenExpiry = DateTime.UtcNow.AddMinutes(15);
            user.PasswordResetAttempts = 0;
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

            if (string.IsNullOrEmpty(user.PasswordResetToken))
                return ApiResponse<string>.FailureResponse("Invalid OTP code.");

            if (user.PasswordResetTokenExpiry == null || user.PasswordResetTokenExpiry < DateTime.UtcNow)
                return ApiResponse<string>.FailureResponse(
                    "OTP code has expired. Please request a new one.");

            if (!BCrypt.Net.BCrypt.Verify(dto.Otp, user.PasswordResetToken))
            {
                user.PasswordResetAttempts++;
                if (user.PasswordResetAttempts >= 5)
                {
                    user.PasswordResetToken = null;
                    user.PasswordResetTokenExpiry = null;
                    user.PasswordResetAttempts = 0;
                    await _unitOfWork.PortalUsers.UpdateAsync(user);
                    await _unitOfWork.SaveChangesAsync();
                    return ApiResponse<string>.FailureResponse(
                        "Too many incorrect attempts. Please request a new OTP code.");
                }
                await _unitOfWork.PortalUsers.UpdateAsync(user);
                await _unitOfWork.SaveChangesAsync();
                return ApiResponse<string>.FailureResponse("Invalid OTP code.");
            }

            // Hash with cost 12, clear OTP (single use), restore access for a locked-out account
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword, 12);
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpiry = null;
            user.PasswordResetAttempts = 0;
            user.FailedLoginAttempts = 0;
            user.IsLockedOut = false;
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

    public async Task<ApiResponse<string>> ChangePasswordAsync(Guid userId, PortalChangePasswordDto dto)
    {
        try
        {
            var user = await _context.PortalUsers
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return ApiResponse<string>.FailureResponse("User not found.");

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            {
                _logger.LogWarning("Portal change password failed - incorrect current password for {UserId}", userId);
                return ApiResponse<string>.FailureResponse("Current password is incorrect");
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword, 12);
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Portal password changed for {UserId}", userId);
            return ApiResponse<string>.SuccessResponse(
                "Password changed successfully.", "Password updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during portal password change for {UserId}", userId);
            return ApiResponse<string>.FailureResponse(
                "An error occurred. Please try again.");
        }
    }

    public async Task<ApiResponse<string>> VerifyEmailAsync(PortalVerifyEmailDto dto)
    {
        try
        {
            var user = await _context.PortalUsers
                .FirstOrDefaultAsync(u =>
                    u.Email == dto.Email &&
                    u.PortalUserType == dto.PortalUserType &&
                    u.EmailVerificationToken == dto.Token &&
                    u.EmailVerificationTokenExpiry > DateTime.UtcNow);

            if (user == null)
                return ApiResponse<string>.FailureResponse("Invalid or expired verification link.");

            if (user.IsEmailVerified)
                return ApiResponse<string>.SuccessResponse("Email already verified.", "Already verified");

            user.IsEmailVerified = true;
            user.EmailVerificationToken = null;
            user.EmailVerificationTokenExpiry = null;
            if (user is Tenant tenantUser)
            {
                tenantUser.TemporaryInitialPassword = null;
            }
            else if (user is Landlord landlordUser)
            {
                landlordUser.TemporaryInitialPassword = null;
            }
            else if (user is Agent agentUser)
            {
                agentUser.TemporaryInitialPassword = null;
            }
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Portal email verified: {Email}", dto.Email);
            return ApiResponse<string>.SuccessResponse(
                "Email verified successfully. You may now set your password.",
                "Email verified");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying portal email: {Email}", dto.Email);
            return ApiResponse<string>.FailureResponse("An error occurred. Please try again.");
        }
    }

    public async Task<ApiResponse<string>> SetPasswordAsync(PortalSetPasswordDto dto)
    {
        try
        {
            var user = await _context.PortalUsers
                .FirstOrDefaultAsync(u => u.Email == dto.Email && u.PortalUserType == dto.PortalUserType);

            if (user == null)
                return ApiResponse<string>.FailureResponse("User not found.");

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
                return ApiResponse<string>.FailureResponse("Incorrect temporary password.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword, 12);
            user.IsActive = true;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            try
            {
                var tenantUser = await _context.PortalUsers
                    .IgnoreQueryFilters()
                    .OfType<Data.Models.Entities.Portal.Tenant>()
                    .FirstOrDefaultAsync(t => t.Email == dto.Email &&
                        t.PortalUserType == PortalUserType.Tenant);
                if (tenantUser != null)
                {
                    tenantUser.TenantStatus = Data.Models.Enums.TenantStatus.Active;
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update TenantStatus after set-password for {Email}", dto.Email);
                // Do NOT rethrow — IsActive is already set to true above
            }

            // Registration completion for Landlord/Agent → send the signable agreement.
            // (Tenants get theirs on first successful payment, not here.)
            if (user.PortalUserType == PortalUserType.Landlord || user.PortalUserType == PortalUserType.Agent)
            {
                try { await _agreementService.SendAgreementForSigningAsync(user.Id, (int)user.PortalUserType); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send agreement for signing to {Email}", dto.Email); }
            }

            _logger.LogInformation("Portal password set: {Email}", dto.Email);
            return ApiResponse<string>.SuccessResponse(
                "Password set successfully. You can now log in.",
                "Password set");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting portal password: {Email}", dto.Email);
            return ApiResponse<string>.FailureResponse("An error occurred. Please try again.");
        }
    }

    public async Task<ApiResponse<string>> UpdateProfileAsync(Guid userId, UpdatePortalProfileDto dto)
    {
        try
        {
            var user = await _context.PortalUsers
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return ApiResponse<string>.FailureResponse("User not found.");

            if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
                user.PhoneNumber = dto.PhoneNumber;

            var emailChangeRequested = false;
            if (!string.IsNullOrWhiteSpace(dto.NewEmail) &&
                !string.Equals(dto.NewEmail, user.Email, StringComparison.OrdinalIgnoreCase))
            {
                var token = Guid.NewGuid().ToString("N");
                user.PendingEmail = dto.NewEmail;
                user.EmailVerificationToken = token;
                user.EmailVerificationTokenExpiry = DateTime.UtcNow.AddDays(14);
                emailChangeRequested = true;

                var confirmationLink = _frontendUrlService.GetEmailChangeConfirmationUrl(token, dto.NewEmail);

                try
                {
                    await _emailService.SendConfirmNewEmailAsync(dto.NewEmail, user.FirstName, confirmationLink);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send confirm-new-email to {NewEmail} for {UserId}", dto.NewEmail, userId);
                }
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Portal profile updated for {UserId} (email change requested: {EmailChange})",
                userId, emailChangeRequested);

            return ApiResponse<string>.SuccessResponse(
                emailChangeRequested
                    ? "Profile updated. Check your new email address to confirm the change."
                    : "Profile updated successfully.",
                "Profile updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during portal profile update for {UserId}", userId);
            return ApiResponse<string>.FailureResponse("An error occurred. Please try again.");
        }
    }

    public async Task<ApiResponse<string>> ConfirmEmailChangeAsync(ConfirmEmailChangeDto dto)
    {
        try
        {
            var user = await _context.PortalUsers
                .FirstOrDefaultAsync(u =>
                    u.PendingEmail == dto.Email &&
                    u.EmailVerificationToken == dto.Token &&
                    u.EmailVerificationTokenExpiry > DateTime.UtcNow);

            if (user == null)
                return ApiResponse<string>.FailureResponse("Invalid or expired confirmation link.");

            user.Email = user.PendingEmail!;
            user.PendingEmail = null;
            user.IsEmailVerified = true;
            user.EmailVerificationToken = null;
            user.EmailVerificationTokenExpiry = null;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Portal email change confirmed for {UserId}", user.Id);
            return ApiResponse<string>.SuccessResponse(
                "Email address updated successfully.", "Email updated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming portal email change for {Email}", dto.Email);
            return ApiResponse<string>.FailureResponse("An error occurred. Please try again.");
        }
    }
}
