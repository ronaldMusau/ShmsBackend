using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Landlord;
using ShmsBackend.Api.Services.Auth;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Api.Services.Portal;

public class LandlordService : ILandlordService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LandlordService> _logger;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly IFrontendUrlService _frontendUrlService;
    private readonly ITokenBlacklistService _tokenBlacklistService;

    public LandlordService(
        IUnitOfWork unitOfWork,
        ILogger<LandlordService> logger,
        IEmailService emailService,
        INotificationService notificationService,
        IFrontendUrlService frontendUrlService,
        ITokenBlacklistService tokenBlacklistService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _emailService = emailService;
        _notificationService = notificationService;
        _frontendUrlService = frontendUrlService;
        _tokenBlacklistService = tokenBlacklistService;
    }

    public async Task<Landlord> CreateAsync(CreateLandlordDto dto)
    {
        var existing = await _unitOfWork.Landlords.GetByEmailAsync(dto.Email);
        if (existing != null)
            throw new InvalidOperationException($"Landlord with email {dto.Email} already exists");

        var deleted = await _unitOfWork.Landlords.GetDeletedByEmailAsync(dto.Email);
        if (deleted != null)
        {
            deleted.FirstName = dto.FirstName;
            deleted.LastName = dto.LastName;
            deleted.PhoneNumber = dto.PhoneNumber;
            deleted.NationalId = dto.NationalId;
            deleted.DateOfBirth = dto.DateOfBirth;
            deleted.IsDeleted = false;
            deleted.DeletedAt = null;
            deleted.IsActive = false;
            deleted.IsEmailVerified = false;
            deleted.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, 12);
            deleted.EmailVerificationToken = null;
            deleted.EmailVerificationTokenExpiry = null;
            deleted.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.Landlords.UpdateAsync(deleted);
            await _unitOfWork.SaveChangesAsync();
            var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                .Replace("+", "-").Replace("/", "_").Replace("=", "");
            deleted.EmailVerificationToken = token;
            deleted.EmailVerificationTokenExpiry = DateTime.UtcNow.AddDays(14);
            deleted.TemporaryInitialPassword = dto.Password;
            await _unitOfWork.SaveChangesAsync();
            var link = _frontendUrlService.GetPortalEmailVerificationUrl(token, deleted.Email, PortalUserType.Landlord);
            var deletedEmailSent = false;
            for (var attempt = 1; attempt <= 3 && !deletedEmailSent; attempt++)
            {
                try
                {
                    await _emailService.SendPortalVerifyWithPasswordEmailAsync(deleted.Email, deleted.FirstName, link, dto.Password);
                    deletedEmailSent = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send verification email to landlord {Email} (attempt {Attempt}/3)", deleted.Email, attempt);
                    if (attempt < 3) await Task.Delay(2000);
                }
            }
            if (deletedEmailSent)
            {
                deleted.VerificationEmailSentAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
            }
            return deleted;
        }

        var landlord = new Landlord
        {
            Id = Guid.NewGuid(),
            Email = dto.Email.ToLower().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, 12),
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            PhoneNumber = dto.PhoneNumber,
            NationalId = dto.NationalId,
            DateOfBirth = dto.DateOfBirth,
            IsActive = false,
            IsEmailVerified = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Landlords.AddAsync(landlord);
        await _unitOfWork.SaveChangesAsync();

        var verificationToken = Guid.NewGuid().ToString("N");
        landlord.EmailVerificationToken = verificationToken;
        landlord.EmailVerificationTokenExpiry = DateTime.UtcNow.AddDays(14);
        landlord.TemporaryInitialPassword = dto.Password;
        await _unitOfWork.SaveChangesAsync();

        var verificationLink = _frontendUrlService.GetPortalEmailVerificationUrl(verificationToken, landlord.Email, PortalUserType.Landlord);
        var landlordEmailSent = false;
        for (var attempt = 1; attempt <= 3 && !landlordEmailSent; attempt++)
        {
            try
            {
                await _emailService.SendPortalVerifyWithPasswordEmailAsync(landlord.Email, landlord.FirstName, verificationLink, dto.Password);
                landlordEmailSent = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send verification email to landlord {Email} (attempt {Attempt}/3)", landlord.Email, attempt);
                if (attempt < 3) await Task.Delay(2000);
            }
        }
        if (landlordEmailSent)
        {
            landlord.VerificationEmailSentAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
        }

        try
        {
            await _notificationService.SendToRolesAsync(
                new[]
                {
                    NotificationAudience.SuperAdmin,
                    NotificationAudience.Admin,
                    NotificationAudience.Secretary,
                    NotificationAudience.Manager,
                    NotificationAudience.Accountant
                },
                $"New landlord {landlord.FirstName} {landlord.LastName} has been registered.",
                "user"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification for landlord creation {Email}", landlord.Email);
        }

        _logger.LogInformation("Landlord created: {Email}", landlord.Email);
        return landlord;
    }

    public async Task<Landlord?> GetByIdAsync(Guid id)
    {
        return await _unitOfWork.Landlords.GetByIdAsync(id);
    }

    public async Task<IEnumerable<Landlord>> GetAllAsync()
    {
        return await _unitOfWork.Landlords.GetAllAsync();
    }

    public async Task<Landlord> UpdateAsync(Guid id, UpdateLandlordDto dto)
    {
        var landlord = await _unitOfWork.Landlords.GetByIdAsync(id);
        if (landlord == null)
            throw new InvalidOperationException("Landlord not found");

        if (!string.IsNullOrEmpty(dto.Email) && dto.Email.ToLower() != landlord.Email)
        {
            var duplicate = await _unitOfWork.Landlords.GetByEmailAsync(dto.Email);
            if (duplicate != null)
                throw new InvalidOperationException($"Email {dto.Email} is already in use");
            landlord.Email = dto.Email.ToLower().Trim();
        }

        if (!string.IsNullOrEmpty(dto.FirstName)) landlord.FirstName = dto.FirstName;
        if (!string.IsNullOrEmpty(dto.LastName)) landlord.LastName = dto.LastName;
        if (!string.IsNullOrEmpty(dto.PhoneNumber)) landlord.PhoneNumber = dto.PhoneNumber;
        if (dto.IsActive.HasValue) landlord.IsActive = dto.IsActive.Value;
        if (!string.IsNullOrEmpty(dto.NationalId)) landlord.NationalId = dto.NationalId;
        if (dto.DateOfBirth.HasValue) landlord.DateOfBirth = dto.DateOfBirth.Value;

        landlord.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Landlords.UpdateAsync(landlord);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Landlord updated: {Id}", id);
        return landlord;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var landlord = await _unitOfWork.Landlords.GetByIdAsync(id);
        if (landlord == null) return false;

        landlord.IsDeleted = true;
        landlord.DeletedAt = DateTime.UtcNow;
        landlord.IsActive = false;
        await _unitOfWork.Landlords.UpdateAsync(landlord);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Landlord deleted: {Id}", id);
        return true;
    }

    public async Task<bool> ToggleStatusAsync(Guid id)
    {
        var landlord = await _unitOfWork.Landlords.GetByIdAsync(id);
        if (landlord == null) return false;

        landlord.IsActive = !landlord.IsActive;
        landlord.UpdatedAt = DateTime.UtcNow;

        if (!landlord.IsActive)
        {
            if (!string.IsNullOrEmpty(landlord.RefreshToken))
                await _tokenBlacklistService.BlacklistTokenAsync(landlord.RefreshToken, TimeSpan.FromDays(30));

            try
            {
                await _emailService.SendAccountDeactivatedEmailAsync(landlord.Email, landlord.FirstName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send deactivation email to {Email}", landlord.Email);
            }
        }
        else
        {
            try
            {
                await _emailService.SendAccountReactivatedEmailAsync(landlord.Email, landlord.FirstName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send reactivation email to {Email}", landlord.Email);
            }
        }

        try
        {
            await _notificationService.SendToUserAsync(
                landlord.Id.ToString(),
                landlord.IsActive
                    ? "Your account has been reactivated. You can now log in."
                    : "Your account has been deactivated. Please contact your administrator.",
                "account");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send status change notification for landlord {Id}", landlord.Id);
        }

        await _unitOfWork.Landlords.UpdateAsync(landlord);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Landlord status toggled: {Id}, IsActive: {IsActive}", id, landlord.IsActive);
        return true;
    }
}
