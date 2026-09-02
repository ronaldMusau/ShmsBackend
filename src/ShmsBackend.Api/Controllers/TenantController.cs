using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Tenant;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Agreements;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Payment;
using ShmsBackend.Api.Services.Portal;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TenantController : ControllerBase
{
    private readonly ITenantService _tenantService;
    private readonly ILogger<TenantController> _logger;
    private readonly ShmsDbContext _context;
    private readonly IFrontendUrlService _frontendUrlService;
    private readonly IEmailService _emailService;
    private readonly IPaymentService _paymentService;
    private readonly IAgreementService _agreementService;

    public TenantController(
        ITenantService tenantService,
        ILogger<TenantController> logger,
        ShmsDbContext context,
        IFrontendUrlService frontendUrlService,
        IEmailService emailService,
        IPaymentService paymentService,
        IAgreementService agreementService)
    {
        _tenantService = tenantService;
        _logger = logger;
        _context = context;
        _frontendUrlService = frontendUrlService;
        _emailService = emailService;
        _paymentService = paymentService;
        _agreementService = agreementService;
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Create([FromBody] CreateTenantDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var tenant = await _tenantService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = tenant.Id },
                ApiResponse<object>.SuccessResponse(new
                {
                    tenant.Id,
                    tenant.Email,
                    tenant.FirstName,
                    tenant.LastName,
                    tenant.PhoneNumber,
                    tenant.DateOfBirth,
                    tenant.EmergencyContactName,
                    tenant.EmergencyContactPhone,
                    tenant.IsActive,
                    tenant.PortalUserType
                }, "Tenant created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tenant");
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while creating the tenant"));
        }
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Landlord,Tenant,Agent")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var tenant = await _tenantService.GetByIdAsync(id);
            if (tenant == null)
                return NotFound(ApiResponse<object>.FailureResponse("Tenant not found"));

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                tenant.Id,
                tenant.Email,
                tenant.FirstName,
                tenant.LastName,
                tenant.PhoneNumber,
                tenant.DateOfBirth,
                tenant.EmergencyContactName,
                tenant.EmergencyContactPhone,
                tenant.IsActive,
                tenant.IsEmailVerified,
                tenant.PortalUserType,
                tenant.CreatedAt,
                tenant.UpdatedAt,
                tenant.NationalId,
                tenant.County,
                tenant.Constituency,
                tenant.Ward,
                TenantStatus = tenant.TenantStatus.ToString()
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tenant: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving the tenant"));
        }
    }

    // GET /api/tenant/{id}/detail
    // Composite admin view: profile + house/flat + financial standing + complaints raised + agreement/ID status.
    [HttpGet("{id:guid}/detail")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetDetail(Guid id)
    {
        try
        {
            // IgnoreQueryFilters so a soft-deleted (vacated) tenant's full history is still viewable;
            // a genuinely non-existent id still falls through to the 404 below.
            var tenant = await _context.Tenants
                .IgnoreQueryFilters()
                .Include(t => t.House).ThenInclude(h => h!.Flat)
                .Include(t => t.House).ThenInclude(h => h!.HouseTypeRef)
                .FirstOrDefaultAsync(t => t.Id == id);
            if (tenant == null)
                return NotFound(ApiResponse<object>.FailureResponse("Tenant not found"));

            // ── Financial standing — reuses the portal payment-summary query + filter ──
            var allPayments = await _paymentService.GetTenantPaymentHistoryAsync(id);
            var cyclePayments = allPayments
                .Where(p => p.TenancyCycle == tenant.TenancyCycle && !p.IsDeleted)
                .ToList();
            var visiblePayments = cyclePayments
                .Where(p => p.PaymentStatus == PaymentTransactionStatus.Paid
                         || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid
                         || p.PaymentStatus == PaymentTransactionStatus.Overdue)
                .ToList();
            var totalCollected = visiblePayments
                .Where(p => p.PaymentStatus == PaymentTransactionStatus.Paid)
                .Sum(p => p.AmountPaid);
            var totalOverdue = visiblePayments
                .Where(p => p.PaymentStatus == PaymentTransactionStatus.Overdue)
                .Sum(p => p.Balance);
            var outstandingBalance = cyclePayments
                .Where(p => p.PaymentStatus != PaymentTransactionStatus.Paid
                         && p.PaymentStatus != PaymentTransactionStatus.Cancelled)
                .Sum(p => p.Balance);
            var recentPayments = visiblePayments
                .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
                .Take(5)
                .Select(p => new
                {
                    p.Id,
                    p.Amount,
                    p.AmountPaid,
                    p.Balance,
                    Status = p.PaymentStatus.ToString(),
                    Type = p.PaymentType.ToString(),
                    p.MpesaReceiptNumber,
                    p.DueDate,
                    p.PaidAt,
                    p.Month,
                    p.Year,
                    p.IsInitialPayment,
                    p.Description
                })
                .ToList();

            // ── Complaints raised by this tenant (clickable list) ──
            var complaints = await _context.Complaints
                .Include(c => c.ComplaintType)
                .Where(c => c.TenantId == id)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new
                {
                    c.Id,
                    c.TicketNumber,
                    ComplaintTypeName = c.ComplaintType != null ? c.ComplaintType.Name : null,
                    c.Status,
                    c.CreatedAt
                })
                .ToListAsync();

            // ── Agreement + ID-document status (delegated to IAgreementService) ──
            var agreement = await _agreementService.GetMyAgreementAsync(id);
            var idDocument = await _agreementService.GetMyIdDocumentAsync(id);

            var h = tenant.House;
            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                Profile = new
                {
                    tenant.Id,
                    tenant.Email,
                    tenant.FirstName,
                    tenant.LastName,
                    tenant.PhoneNumber,
                    tenant.DateOfBirth,
                    tenant.NationalId,
                    tenant.County,
                    tenant.Constituency,
                    tenant.Ward,
                    tenant.EmergencyContactName,
                    tenant.EmergencyContactPhone,
                    tenant.IsActive,
                    tenant.IsEmailVerified,
                    tenant.HasCompletedInitialPayment,
                    tenant.TenancyCycle,
                    tenant.CreatedAt,
                    tenant.UpdatedAt,
                    PortalUserType = tenant.PortalUserType.ToString()
                },
                House = h == null ? null : new
                {
                    h.Id,
                    h.HouseNumber,
                    HouseTypeName = h.HouseTypeRef != null ? h.HouseTypeRef.Name : null,
                    h.RentFee,
                    h.DepositFee,
                    OccupancyStatus = h.OccupancyStatus.ToString(),
                    PaymentStatus = h.PaymentStatus.ToString(),
                    h.FlatId,
                    Flat = h.Flat == null ? null : new
                    {
                        h.Flat.Id,
                        h.Flat.FlatName,
                        h.Flat.County,
                        h.Flat.Constituency,
                        h.Flat.Ward,
                        h.Flat.LandlordId
                    }
                },
                Financials = new
                {
                    CurrentStatus = tenant.TenantStatus.ToString(),
                    TotalCollected = totalCollected,
                    TotalOverdue = totalOverdue,
                    OutstandingBalance = outstandingBalance,
                    RecentPayments = recentPayments
                },
                Complaints = complaints,
                Agreement = agreement,
                IdDocument = idDocument
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building tenant detail: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving the tenant detail"));
        }
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Landlord,Tenant,Agent")]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? status = null)
    {
        try
        {
            // status == "vacated" (case-insensitive) → soft-deleted tenants; otherwise the normal filtered list.
            var isVacated = string.Equals(status, "vacated", StringComparison.OrdinalIgnoreCase);

            var tenants = isVacated
                ? await _context.Tenants
                    .IgnoreQueryFilters()
                    .Include(t => t.House)
                        .ThenInclude(h => h!.Flat)
                    .Where(t => t.IsDeleted)
                    .ToListAsync()
                : (await _tenantService.GetAllAsync()).ToList();

            var total = tenants.Count;
            var pagedTenants = tenants.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // For any house whose flat was soft-deleted, look up the real name
            var deletedFlatIds = pagedTenants
                .Where(t => t.House != null && t.House.Flat == null)
                .Select(t => t.House!.FlatId)
                .Distinct()
                .ToList();

            var deletedFlatNames = new Dictionary<Guid, string>();
            if (deletedFlatIds.Count > 0)
            {
                deletedFlatNames = await _context.Flats
                    .IgnoreQueryFilters()
                    .Where(f => deletedFlatIds.Contains(f.Id))
                    .ToDictionaryAsync(f => f.Id, f => f.FlatName);
            }

            var data = pagedTenants.Select(t => new
            {
                t.Id,
                t.Email,
                t.FirstName,
                t.LastName,
                t.PhoneNumber,
                t.IsActive,
                t.IsEmailVerified,
                TenantStatus = t.TenantStatus.ToString(),
                Status = t.TenantStatus.ToString(),
                t.HasCompletedInitialPayment,
                t.HouseId,
                HouseNumber = t.House != null ? t.House.HouseNumber : null,
                HouseName = t.House != null
                    ? (t.House.Flat != null
                        ? $"{t.House.HouseNumber} - {t.House.Flat.FlatName}"
                        : (deletedFlatNames.TryGetValue(t.House.FlatId, out var fn)
                            ? $"{t.House.HouseNumber} - {fn}"
                            : $"{t.House.HouseNumber} - (Flat Deleted)"))
                    : null,
                t.CreatedAt,
                t.NationalId,
                t.County,
                t.Constituency,
                t.Ward,
                t.DateOfBirth,
                t.EmergencyContactName,
                t.EmergencyContactPhone,
                t.UpdatedAt,
                PortalUserType = t.PortalUserType.ToString()
            }).ToList();

            return Ok(new
            {
                success = true,
                data,
                total,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling((double)total / pageSize)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all tenants");
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving tenants"));
        }
    }

    [HttpPut("{id}")]
    [HttpPatch("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenantDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var tenant = await _tenantService.UpdateAsync(id, dto);
            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                tenant.Id,
                tenant.Email,
                tenant.FirstName,
                tenant.LastName,
                tenant.PhoneNumber,
                tenant.DateOfBirth,
                tenant.EmergencyContactName,
                tenant.EmergencyContactPhone,
                tenant.IsActive,
                tenant.UpdatedAt
            }, "Tenant updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tenant: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while updating the tenant"));
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var result = await _tenantService.DeleteAsync(id);
            if (!result)
                return NotFound(ApiResponse<object>.FailureResponse("Tenant not found"));

            return Ok(ApiResponse<object?>.SuccessResponse(null, "Tenant deleted successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting tenant: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while deleting the tenant"));
        }
    }

    [HttpPatch("{id}/toggle-status")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> ToggleStatus(Guid id)
    {
        try
        {
            var result = await _tenantService.ToggleStatusAsync(id);
            if (!result)
                return NotFound(ApiResponse<object>.FailureResponse("Tenant not found"));

            return Ok(ApiResponse<object?>.SuccessResponse(null, "Tenant status updated successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling tenant status: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while updating tenant status"));
        }
    }

    [HttpPost("{id:guid}/resend-verification")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Agent")]
    public async Task<IActionResult> ResendVerificationEmail(Guid id)
    {
        var tenant = await _context.Tenants
            .Include(t => t.House)
                .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        if (User.IsInRole("Agent"))
        {
            var agentIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(agentIdStr, out var agentId))
                return Unauthorized();

            var flatId = tenant.House?.FlatId;
            if (flatId == null)
                return StatusCode(403, new { success = false, message = "You are not authorized to resend verification for this tenant." });

            var authorized = await _context.AgentFlats
                .AnyAsync(af => af.AgentId == agentId && af.FlatId == flatId);
            if (!authorized)
                return StatusCode(403, new { success = false, message = "You are not authorized to resend verification for this tenant." });
        }

        if (!tenant.HasCompletedInitialPayment)
            return BadRequest(new { success = false, message = "This tenant has not completed their initial payment yet." });

        if (tenant.IsEmailVerified)
            return BadRequest(new { success = false, message = "This tenant has already verified their email." });

        if (string.IsNullOrEmpty(tenant.TemporaryInitialPassword))
            return BadRequest(new { success = false, message = "No temporary password on record — cannot resend. Contact support." });

        tenant.EmailVerificationToken = Guid.NewGuid().ToString("N");
        tenant.EmailVerificationTokenExpiry = DateTime.UtcNow.AddDays(14);
        await _context.SaveChangesAsync();

        var verificationLink = _frontendUrlService.GetPortalEmailVerificationUrl(
            tenant.EmailVerificationToken, tenant.Email, PortalUserType.Tenant);

        var emailSent = false;
        for (var attempt = 1; attempt <= 3 && !emailSent; attempt++)
        {
            try
            {
                await _emailService.SendPortalVerifyWithPasswordEmailAsync(
                    tenant.Email, tenant.FirstName, verificationLink, tenant.TemporaryInitialPassword);
                emailSent = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Resend verification email failed for tenant {Email} (attempt {Attempt}/3)", tenant.Email, attempt);
                if (attempt < 3) await Task.Delay(2000);
            }
        }

        if (!emailSent)
            return BadRequest(new { success = false, message = "Failed to send verification email after 3 attempts." });

        tenant.VerificationEmailSentAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = "Verification email sent." });
    }
}
