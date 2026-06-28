using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Tenant;
using ShmsBackend.Api.Services.Auth;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
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

    public TenantService(
        IUnitOfWork unitOfWork,
        ILogger<TenantService> logger,
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

        var verificationLink = _frontendUrlService.GetPortalEmailVerificationUrl(verificationToken, tenant.Email);
        try
        {
            await _emailService.SendEmailVerificationEmailAsync(tenant.Email, tenant.FirstName, verificationLink);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send verification email to tenant {Email}", tenant.Email);
        }

        try
        {
            await _emailService.SendPortalWelcomeEmailAsync(tenant.Email, tenant.FirstName, dto.Password);
            _logger.LogInformation("Welcome email sent to tenant {Email}", tenant.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send welcome email to tenant {Email}", tenant.Email);
        }

        try
        {
            await _notificationService.SendToRolesAsync(
                new[]
                {
                    NotificationAudience.SuperAdmin,
                    NotificationAudience.Admin,
                    NotificationAudience.Secretary
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

        tenant.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Tenants.UpdateAsync(tenant);
        await _unitOfWork.SaveChangesAsync();

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

        await _unitOfWork.Tenants.UpdateAsync(tenant);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Tenant status toggled: {Id}, IsActive: {IsActive}", id, tenant.IsActive);
        return true;
    }
}
