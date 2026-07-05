using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Tenant;
using ShmsBackend.Api.Services.Auth;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Api.Services.Portal;

public class TenantService : ITenantService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TenantService> _logger;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly IFrontendUrlService _frontendUrlService;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly ShmsDbContext _context;

    public TenantService(
        IUnitOfWork unitOfWork,
        ILogger<TenantService> logger,
        IEmailService emailService,
        INotificationService notificationService,
        IFrontendUrlService frontendUrlService,
        ITokenBlacklistService tokenBlacklistService,
        ShmsDbContext context)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _emailService = emailService;
        _notificationService = notificationService;
        _frontendUrlService = frontendUrlService;
        _tokenBlacklistService = tokenBlacklistService;
        _context = context;
    }

    public async Task<Tenant> CreateAsync(CreateTenantDto dto)
    {
        var existing = await _unitOfWork.Tenants.GetByEmailAsync(dto.Email);
        if (existing != null)
            throw new InvalidOperationException($"Tenant with email {dto.Email} already exists");

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Email = dto.Email.ToLower().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, 12),
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            PhoneNumber = dto.PhoneNumber,
            NationalId = dto.NationalId,
            DateOfBirth = dto.DateOfBirth,
            EmergencyContactName = dto.EmergencyContactName,
            EmergencyContactPhone = dto.EmergencyContactPhone,
            IsActive = false,
            IsEmailVerified = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Tenants.AddAsync(tenant);
        await _unitOfWork.SaveChangesAsync();

        var verificationToken = Guid.NewGuid().ToString("N");
        tenant.EmailVerificationToken = verificationToken;
        tenant.EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(48);
        await _unitOfWork.SaveChangesAsync();

        var verificationLink = _frontendUrlService.GetPortalEmailVerificationUrl(verificationToken, tenant.Email, PortalUserType.Tenant);
        try
        {
            await _emailService.SendPortalVerifyWithPasswordEmailAsync(tenant.Email, tenant.FirstName, verificationLink, dto.Password);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send verification email to tenant {Email}", tenant.Email);
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
                $"New tenant {tenant.FirstName} {tenant.LastName} has been registered.",
                "user"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification for tenant creation {Email}", tenant.Email);
        }

        _logger.LogInformation("Tenant created: {Email}", tenant.Email);
        return tenant;
    }

    public async Task<Tenant?> GetByIdAsync(Guid id)
    {
        return await _unitOfWork.Tenants.GetByIdAsync(id);
    }

    public async Task<IEnumerable<Tenant>> GetAllAsync()
    {
        return await _unitOfWork.Tenants.GetAllAsync();
    }

    public async Task<Tenant> UpdateAsync(Guid id, UpdateTenantDto dto)
    {
        var tenant = await _unitOfWork.Tenants.GetByIdAsync(id);
        if (tenant == null)
            throw new InvalidOperationException("Tenant not found");

        var oldHouseId = tenant.HouseId;

        if (!string.IsNullOrEmpty(dto.Email) && dto.Email.ToLower() != tenant.Email)
        {
            var duplicate = await _unitOfWork.Tenants.GetByEmailAsync(dto.Email);
            if (duplicate != null)
                throw new InvalidOperationException($"Email {dto.Email} is already in use");
            tenant.Email = dto.Email.ToLower().Trim();
        }

        if (!string.IsNullOrEmpty(dto.FirstName)) tenant.FirstName = dto.FirstName;
        if (!string.IsNullOrEmpty(dto.LastName)) tenant.LastName = dto.LastName;
        if (!string.IsNullOrEmpty(dto.PhoneNumber)) tenant.PhoneNumber = dto.PhoneNumber;
        if (dto.IsActive.HasValue) tenant.IsActive = dto.IsActive.Value;
        if (!string.IsNullOrEmpty(dto.NationalId)) tenant.NationalId = dto.NationalId;
        if (dto.DateOfBirth.HasValue) tenant.DateOfBirth = dto.DateOfBirth.Value;
        if (!string.IsNullOrEmpty(dto.EmergencyContactName)) tenant.EmergencyContactName = dto.EmergencyContactName;
        if (!string.IsNullOrEmpty(dto.EmergencyContactPhone)) tenant.EmergencyContactPhone = dto.EmergencyContactPhone;
        if (dto.HouseId.HasValue) tenant.HouseId = dto.HouseId.Value;

        tenant.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Tenants.UpdateAsync(tenant);
        await _unitOfWork.SaveChangesAsync();

        if (dto.HouseId.HasValue && dto.HouseId != oldHouseId)
        {
            try
            {
                var house = await _context.Houses
                    .Include(h => h.Flat)
                    .FirstOrDefaultAsync(h => h.Id == dto.HouseId.Value);

                if (house != null)
                {
                    await _notificationService.SendToUserAsync(
                        tenant.Id.ToString(),
                        $"You have been assigned to House {house.HouseNumber} in {house.Flat?.FlatName ?? ""}. Welcome!",
                        "housing");

                    if (house.Flat?.LandlordId != null)
                    {
                        await _notificationService.SendToUserAsync(
                            house.Flat.LandlordId.ToString(),
                            $"Tenant {tenant.FirstName} {tenant.LastName} has been assigned to House {house.HouseNumber} in {house.Flat.FlatName}.",
                            "housing");
                    }

                    await _notificationService.SendToRolesAsync(
                        new[]
                        {
                            NotificationAudience.SuperAdmin,
                            NotificationAudience.Admin,
                            NotificationAudience.Secretary,
                            NotificationAudience.Manager,
                            NotificationAudience.Accountant
                        },
                        $"Tenant {tenant.FirstName} {tenant.LastName} has been assigned to House {house.HouseNumber} in {house.Flat?.FlatName ?? ""}.",
                        "housing");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send house assignment notifications for tenant {Id}", tenant.Id);
            }
        }

        _logger.LogInformation("Tenant updated: {Id}", id);
        return tenant;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var tenant = await _unitOfWork.Tenants.GetByIdAsync(id);
        if (tenant == null) return false;

        await _unitOfWork.Tenants.DeleteAsync(tenant);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Tenant deleted: {Id}", id);
        return true;
    }

    public async Task<bool> ToggleStatusAsync(Guid id)
    {
        var tenant = await _unitOfWork.Tenants.GetByIdAsync(id);
        if (tenant == null) return false;

        tenant.IsActive = !tenant.IsActive;
        tenant.UpdatedAt = DateTime.UtcNow;

        if (!tenant.IsActive)
        {
            if (!string.IsNullOrEmpty(tenant.RefreshToken))
                await _tokenBlacklistService.BlacklistTokenAsync(tenant.RefreshToken, TimeSpan.FromDays(30));

            try
            {
                await _emailService.SendAccountDeactivatedEmailAsync(tenant.Email, tenant.FirstName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send deactivation email to {Email}", tenant.Email);
            }
        }
        else
        {
            try
            {
                await _emailService.SendAccountReactivatedEmailAsync(tenant.Email, tenant.FirstName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send reactivation email to {Email}", tenant.Email);
            }
        }

        try
        {
            await _notificationService.SendToUserAsync(
                tenant.Id.ToString(),
                tenant.IsActive
                    ? "Your account has been reactivated. You can now log in."
                    : "Your account has been deactivated. Please contact your administrator.",
                "account");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send status change notification for tenant {Id}", tenant.Id);
        }

        await _unitOfWork.Tenants.UpdateAsync(tenant);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Tenant status toggled: {Id}, IsActive: {IsActive}", id, tenant.IsActive);
        return true;
    }
}
