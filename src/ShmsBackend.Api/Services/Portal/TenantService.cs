using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Tenant;
using ShmsBackend.Api.Services.Agreements;
using ShmsBackend.Api.Services.Auth;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Api.Services.Payment;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Enums;
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
    private readonly IAgreementService _agreementService;
    private readonly IPaymentService _paymentService;
    private readonly ShmsDbContext _context;

    public TenantService(
        IUnitOfWork unitOfWork,
        ILogger<TenantService> logger,
        IEmailService emailService,
        INotificationService notificationService,
        IFrontendUrlService frontendUrlService,
        ITokenBlacklistService tokenBlacklistService,
        IAgreementService agreementService,
        IPaymentService paymentService,
        ShmsDbContext context)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _emailService = emailService;
        _notificationService = notificationService;
        _frontendUrlService = frontendUrlService;
        _tokenBlacklistService = tokenBlacklistService;
        _agreementService = agreementService;
        _paymentService = paymentService;
        _context = context;
    }

    public async Task<Tenant> CreateAsync(CreateTenantDto dto)
    {
        var existing = await _unitOfWork.Tenants.GetByEmailAsync(dto.Email);
        if (existing != null)
            throw new InvalidOperationException($"Tenant with email {dto.Email} already exists");

        var deleted = await _unitOfWork.Tenants.GetDeletedByEmailAsync(dto.Email);
        if (deleted != null)
        {
            deleted.FirstName = dto.FirstName;
            deleted.LastName = dto.LastName;
            deleted.PhoneNumber = dto.PhoneNumber;
            deleted.NationalId = dto.NationalId;
            deleted.DateOfBirth = dto.DateOfBirth;
            deleted.EmergencyContactName = dto.EmergencyContactName;
            deleted.EmergencyContactPhone = dto.EmergencyContactPhone;
            if (dto.HouseId.HasValue)
            {
                var houseTaken = await _context.Tenants.AnyAsync(t => t.HouseId == dto.HouseId && !t.IsDeleted && t.TenantStatus != TenantStatus.SettlingVacate);
                if (houseTaken)
                    throw new InvalidOperationException("This house already has an active or pending tenant assigned to it.");
            }
            deleted.HouseId = dto.HouseId;
            deleted.IsDeleted = false;
            deleted.DeletedAt = null;
            deleted.IsActive = false;
            deleted.TenantStatus = TenantStatus.Inactive;
            deleted.HasCompletedInitialPayment = false;
            deleted.IsEmailVerified = false;
            deleted.TenancyCycle += 1;
            deleted.TemporaryInitialPassword = dto.Password;
            deleted.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, 12);
            deleted.EmailVerificationToken = null;
            deleted.EmailVerificationTokenExpiry = null;

            // A revived tenant begins a genuinely new tenancy cycle — no state from a prior cycle
            // (lockout counters, stale tokens, old lease/deposit figures, accrued points) should carry
            // forward, even though the underlying row is reused for referential-integrity reasons.
            deleted.PointsBalance = 0;
            deleted.VerificationEmailSentAt = null;
            deleted.DepositAlreadySitting = null;
            deleted.ExternalDepositAmount = null;
            deleted.FailedLoginAttempts = 0;
            deleted.IsLockedOut = false;
            deleted.PasswordResetAttempts = 0;
            deleted.PasswordResetToken = null;
            deleted.PasswordResetTokenExpiry = null;
            deleted.RefreshToken = null;
            deleted.RefreshTokenExpiryTime = null;
            deleted.PendingEmail = null;

            // LeaseStartMonth/LeaseStartYear: mirror the fresh-signup branch's exact derivation
            // (explicit dto value first, else the day after an approved vacate's month, else null) —
            // not left over from whatever house/lease the prior cycle was on.
            VacateRequest? revivalApprovedVacate = null;
            if (dto.HouseId.HasValue)
                revivalApprovedVacate = await _context.VacateRequests
                    .FirstOrDefaultAsync(r => r.HouseId == dto.HouseId && !r.IsDeleted && r.Status == "Approved");

            if (dto.LeaseStartMonth.HasValue && dto.LeaseStartYear.HasValue)
            {
                deleted.LeaseStartMonth = dto.LeaseStartMonth;
                deleted.LeaseStartYear = dto.LeaseStartYear;
            }
            else if (revivalApprovedVacate != null)
            {
                deleted.LeaseStartMonth = revivalApprovedVacate.VacateMonth == 12 ? 1 : revivalApprovedVacate.VacateMonth + 1;
                deleted.LeaseStartYear = revivalApprovedVacate.VacateMonth == 12 ? revivalApprovedVacate.VacateYear + 1 : revivalApprovedVacate.VacateYear;
            }
            else
            {
                deleted.LeaseStartMonth = null;
                deleted.LeaseStartYear = null;
            }

            // A new tenancy cycle requires signing a fresh agreement — a stale Verified/Rejected status
            // (with its old rejection reason, uploaded file, or verification timestamps) from a prior,
            // unrelated lease should never carry forward into the new one. UserIdDocuments is
            // deliberately NOT touched here — ID photos are permanent identity data for this person,
            // not per-tenancy state, so they're correct to persist across cycles.
            var existingAgreement = await _context.UserAgreements.FirstOrDefaultAsync(a => a.PortalUserId == deleted.Id);
            if (existingAgreement != null)
            {
                existingAgreement.Status = AgreementStatus.NotSent;
                existingAgreement.RejectionReason = null;
                existingAgreement.UploadedFilePath = null;
                existingAgreement.UploadedAt = null;
                existingAgreement.VerifiedByAdminId = null;
                existingAgreement.VerifiedAt = null;
                existingAgreement.LastReminderSentAt = null;
            }

            deleted.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.Tenants.UpdateAsync(deleted);
            await _unitOfWork.SaveChangesAsync();
            var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                .Replace("+", "-").Replace("/", "_").Replace("=", "");
            deleted.EmailVerificationToken = token;
            deleted.EmailVerificationTokenExpiry = DateTime.UtcNow.AddDays(14);
            await _unitOfWork.SaveChangesAsync();

            return deleted;
        }

        if (dto.HouseId.HasValue)
        {
            var houseTaken = await _context.Tenants.AnyAsync(t => t.HouseId == dto.HouseId && !t.IsDeleted && t.TenantStatus != TenantStatus.SettlingVacate);
            if (houseTaken)
                throw new InvalidOperationException("This house already has an active or pending tenant assigned to it.");
        }

        VacateRequest? approvedVacate = null;
        if (dto.HouseId.HasValue)
            approvedVacate = await _context.VacateRequests
                .FirstOrDefaultAsync(r => r.HouseId == dto.HouseId && !r.IsDeleted && r.Status == "Approved");

        int? leaseStartMonth = null;
        int? leaseStartYear = null;
        if (dto.LeaseStartMonth.HasValue && dto.LeaseStartYear.HasValue)
        {
            leaseStartMonth = dto.LeaseStartMonth;
            leaseStartYear = dto.LeaseStartYear;
        }
        else if (approvedVacate != null)
        {
            leaseStartMonth = approvedVacate.VacateMonth == 12 ? 1 : approvedVacate.VacateMonth + 1;
            leaseStartYear = approvedVacate.VacateMonth == 12 ? approvedVacate.VacateYear + 1 : approvedVacate.VacateYear;
        }

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
            HouseId = dto.HouseId,
            IsActive = false,
            TenantStatus = TenantStatus.Inactive,
            HasCompletedInitialPayment = false,
            IsEmailVerified = false,
            TemporaryInitialPassword = dto.Password,
            LeaseStartMonth = leaseStartMonth,
            LeaseStartYear = leaseStartYear,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Tenants.AddAsync(tenant);
        await _unitOfWork.SaveChangesAsync();

        var verificationToken = Guid.NewGuid().ToString("N");
        tenant.EmailVerificationToken = verificationToken;
        tenant.EmailVerificationTokenExpiry = DateTime.UtcNow.AddDays(14);
        await _unitOfWork.SaveChangesAsync();

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

        if (dto.HouseId.HasValue)
        {
            var house = await _context.Houses
                .Include(h => h.Flat)
                .FirstOrDefaultAsync(h => h.Id == dto.HouseId.Value);

            if (house != null && house.IsAwaitingExistingTenant)
            {
                tenant.HasCompletedInitialPayment = true;
                tenant.TenantStatus = TenantStatus.Pending;
                tenant.DepositAlreadySitting = dto.DepositAlreadySitting;
                tenant.ExternalDepositAmount = dto.ExternalDepositAmount;

                try
                {
                    await _context.TenantHouseHistories.AddAsync(new TenantHouseHistory
                    {
                        Id = Guid.NewGuid(),
                        HouseId = house.Id,
                        TenantId = tenant.Id,
                        TenantFirstName = tenant.FirstName,
                        TenantLastName = tenant.LastName,
                        TenantEmail = tenant.Email,
                        TenantPhone = tenant.PhoneNumber,
                        HouseNumber = house.HouseNumber,
                        FlatName = house.Flat?.FlatName ?? "",
                        AssignedAt = DateTime.UtcNow,
                        RemovedAt = null,
                        TenancyCycle = tenant.TenancyCycle
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to write TenantHouseHistory for existing tenant {TenantId}", tenant.Id);
                }

                string? verificationLink = null;
                var tempPassword = tenant.TemporaryInitialPassword;
                if (!string.IsNullOrEmpty(tenant.EmailVerificationToken) && !string.IsNullOrEmpty(tempPassword))
                {
                    verificationLink = _frontendUrlService.GetPortalEmailVerificationUrl(
                        tenant.EmailVerificationToken, tenant.Email, PortalUserType.Tenant);
                }

                if (verificationLink != null)
                {
                    var emailSent = false;
                    for (var attempt = 1; attempt <= 3 && !emailSent; attempt++)
                    {
                        try
                        {
                            await _emailService.SendPortalVerifyWithPasswordEmailAsync(
                                tenant.Email, tenant.FirstName, verificationLink, tempPassword!);
                            emailSent = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to send verification email to existing tenant {Email} (attempt {Attempt}/3)", tenant.Email, attempt);
                            if (attempt < 3) await Task.Delay(2000);
                        }
                    }
                    if (emailSent) { tenant.VerificationEmailSentAt = DateTime.UtcNow; }
                }

                try
                {
                    await _agreementService.SendAgreementForSigningAsync(tenant.Id, (int)PortalUserType.Tenant);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send agreement for signing to existing tenant {TenantId}", tenant.Id);
                }

                house.OccupancyStatus = OccupancyStatus.Occupied;
                house.IsAwaitingExistingTenant = false;

                var paymentMonth = tenant.LeaseStartMonth ?? DateTime.UtcNow.Month;
                var paymentYear = tenant.LeaseStartYear ?? DateTime.UtcNow.Year;
                var serviceCharge = await _paymentService.GetServiceChargeAsync(house.RentFee);
                var rentDueDay = house.Flat != null
                    ? Math.Min(house.Flat.RentDueDay, DateTime.DaysInMonth(paymentYear, paymentMonth))
                    : DateTime.DaysInMonth(paymentYear, paymentMonth);
                var dueDate = new DateTime(paymentYear, paymentMonth, rentDueDay);
                var monthName = new DateTime(paymentYear, paymentMonth, 1).ToString("MMMM yyyy");

                await _context.Payments.AddAsync(new ShmsBackend.Data.Models.Entities.Portal.Payment
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    HouseId = house.Id,
                    FlatId = house.FlatId,
                    LandlordId = house.Flat?.LandlordId ?? Guid.Empty,
                    Amount = house.RentFee,
                    AmountPaid = 0,
                    Balance = house.RentFee,
                    RentAmount = house.RentFee,
                    ServiceChargeAmount = serviceCharge,
                    Month = paymentMonth,
                    Year = paymentYear,
                    IsInitialPayment = false,
                    PaymentType = PaymentType.Rent,
                    PhoneNumber = tenant.PhoneNumber,
                    DueDate = dueDate,
                    PaymentStatus = PaymentTransactionStatus.Pending,
                    Description = $"Monthly rent - {monthName}",
                    TenancyCycle = tenant.TenancyCycle,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
            }
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
        return await _context.Tenants
            .Include(t => t.House)
            .ThenInclude(h => h!.Flat)
            .Where(t => !t.IsDeleted)
            .ToListAsync();
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
        if (dto.HouseId.HasValue)
        {
            var houseTaken = await _context.Tenants.AnyAsync(t => t.HouseId == dto.HouseId && !t.IsDeleted && t.Id != tenant.Id && t.TenantStatus != TenantStatus.SettlingVacate);
            if (houseTaken)
                throw new InvalidOperationException("This house already has an active or pending tenant assigned to it.");
            tenant.HouseId = dto.HouseId.Value;
        }

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

        if (dto.HouseId.HasValue && dto.HouseId != oldHouseId)
        {
            try
            {
                if (oldHouseId.HasValue)
                {
                    var openHistory = await _context.TenantHouseHistories
                        .FirstOrDefaultAsync(h =>
                            h.TenantId == tenant.Id &&
                            h.HouseId == oldHouseId.Value &&
                            h.RemovedAt == null);
                    if (openHistory != null)
                    {
                        openHistory.RemovedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }
                }

                var newHouse = await _context.Houses
                    .Include(h => h.Flat)
                    .FirstOrDefaultAsync(h => h.Id == dto.HouseId.Value);

                if (newHouse != null)
                {
                    var history = new TenantHouseHistory
                    {
                        Id = Guid.NewGuid(),
                        HouseId = newHouse.Id,
                        TenantId = tenant.Id,
                        TenantFirstName = tenant.FirstName,
                        TenantLastName = tenant.LastName,
                        TenantEmail = tenant.Email,
                        TenantPhone = tenant.PhoneNumber,
                        HouseNumber = newHouse.HouseNumber,
                        FlatName = newHouse.Flat?.FlatName ?? "",
                        AssignedAt = DateTime.UtcNow,
                        RemovedAt = null,
                        TenancyCycle = tenant.TenancyCycle
                    };
                    await _context.TenantHouseHistories.AddAsync(history);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write TenantHouseHistory for tenant {TenantId}, house {HouseId}", tenant.Id, dto.HouseId);
            }
        }

        _logger.LogInformation("Tenant updated: {Id}", id);
        return tenant;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var tenant = await _context.Tenants
            .Include(t => t.House)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (tenant == null) return false;

        if (!tenant.HasCompletedInitialPayment)
        {
            var ownHistory = await _context.TenantHouseHistories.Where(h => h.TenantId == id).ToListAsync();
            if (ownHistory.Count > 0)
                _context.TenantHouseHistories.RemoveRange(ownHistory);

            if (tenant.HouseId.HasValue && tenant.House != null)
            {
                tenant.House.OccupancyStatus = OccupancyStatus.Vacant;
                tenant.House.PaymentStatus = PaymentStatus.NotPaid;
                tenant.House.UpdatedAt = DateTime.UtcNow;
            }

            _context.Tenants.Remove(tenant);
            await _context.SaveChangesAsync();
            return true;
        }

        if (tenant.House != null)
        {
            var hasOtherActiveTenant = await _context.Tenants.AnyAsync(t =>
                t.Id != id
                && t.HouseId == tenant.House.Id
                && !t.IsDeleted
                && t.TenantStatus != TenantStatus.SettlingVacate);

            if (!hasOtherActiveTenant)
            {
                tenant.House.OccupancyStatus = OccupancyStatus.Vacant;
                tenant.House.PaymentStatus = PaymentStatus.NotPaid;
                tenant.House.UpdatedAt = DateTime.UtcNow;
            }
        }

        var openHistory = await _context.TenantHouseHistories
            .Where(h => h.TenantId == id && h.RemovedAt == null)
            .FirstOrDefaultAsync();
        if (openHistory != null)
            openHistory.RemovedAt = DateTime.UtcNow;

        tenant.IsDeleted = true;
        tenant.DeletedAt = DateTime.UtcNow;
        tenant.IsActive = false;
        tenant.PointsBalance = 0;

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
                await _emailService.SendAccountDeactivatedEmailAsync(tenant.Email, tenant.FirstName, tenant.Id.ToString(), true);
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
                await _emailService.SendAccountReactivatedEmailAsync(tenant.Email, tenant.FirstName, tenant.Id.ToString(), true);
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

    private async Task WriteHouseHistoryAsync(Tenant tenant, Guid houseId)
    {
        var house = await _context.Houses
            .Include(h => h.Flat)
            .FirstOrDefaultAsync(h => h.Id == houseId);

        if (house != null)
        {
            var history = new TenantHouseHistory
            {
                Id = Guid.NewGuid(),
                HouseId = house.Id,
                TenantId = tenant.Id,
                TenantFirstName = tenant.FirstName,
                TenantLastName = tenant.LastName,
                TenantEmail = tenant.Email,
                TenantPhone = tenant.PhoneNumber,
                HouseNumber = house.HouseNumber,
                FlatName = house.Flat?.FlatName ?? "",
                AssignedAt = DateTime.UtcNow,
                RemovedAt = null,
                TenancyCycle = tenant.TenancyCycle
            };
            await _context.TenantHouseHistories.AddAsync(history);
            await _context.SaveChangesAsync();
        }
    }
}
