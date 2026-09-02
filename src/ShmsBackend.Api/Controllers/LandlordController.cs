using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Landlord;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Agreements;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Portal;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LandlordController : ControllerBase
{
    private readonly ILandlordService _landlordService;
    private readonly ILogger<LandlordController> _logger;
    private readonly ShmsDbContext _context;
    private readonly IFrontendUrlService _frontendUrlService;
    private readonly IEmailService _emailService;
    private readonly IAgreementService _agreementService;

    public LandlordController(
        ILandlordService landlordService,
        ILogger<LandlordController> logger,
        ShmsDbContext context,
        IFrontendUrlService frontendUrlService,
        IEmailService emailService,
        IAgreementService agreementService)
    {
        _landlordService = landlordService;
        _logger = logger;
        _context = context;
        _frontendUrlService = frontendUrlService;
        _emailService = emailService;
        _agreementService = agreementService;
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Create([FromBody] CreateLandlordDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var landlord = await _landlordService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = landlord.Id },
                ApiResponse<object>.SuccessResponse(new
                {
                    landlord.Id,
                    landlord.Email,
                    landlord.FirstName,
                    landlord.LastName,
                    landlord.PhoneNumber,
                    landlord.NationalId,
                    landlord.AgencyName,
                    landlord.IsActive,
                    landlord.PortalUserType
                }, "Landlord created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating landlord");
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while creating the landlord"));
        }
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Landlord,Tenant,Agent")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var landlord = await _landlordService.GetByIdAsync(id);
            if (landlord == null)
                return NotFound(ApiResponse<object>.FailureResponse("Landlord not found"));

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                landlord.Id,
                landlord.Email,
                landlord.FirstName,
                landlord.LastName,
                landlord.PhoneNumber,
                landlord.NationalId,
                landlord.AgencyName,
                landlord.IsActive,
                landlord.IsEmailVerified,
                landlord.PortalUserType,
                landlord.CreatedAt,
                landlord.UpdatedAt
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting landlord: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving the landlord"));
        }
    }

    // GET /api/landlord/{id}/detail
    // Composite admin view: profile + flats owned + complaints on their properties + payments collected + agreement/ID status.
    [HttpGet("{id:guid}/detail")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetDetail(Guid id)
    {
        try
        {
            var landlord = await _landlordService.GetByIdAsync(id);
            if (landlord == null)
                return NotFound(ApiResponse<object>.FailureResponse("Landlord not found"));

            // ── Flats owned (clickable) ──
            var flats = await _context.Flats
                .Include(f => f.Houses)
                .Where(f => f.LandlordId == id)
                .Select(f => new
                {
                    f.Id,
                    f.FlatName,
                    f.County,
                    f.Constituency,
                    f.Ward,
                    TotalHouses = f.Houses.Count,
                    OccupiedHouses = f.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Occupied)
                })
                .ToListAsync();

            // ── Complaints on their properties (clickable) ──
            var complaints = await _context.Complaints
                .Include(c => c.ComplaintType)
                .Where(c => c.LandlordId == id)
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

            // ── Payments collected summary — mirrors PortalPaymentController.GetLandlordPayments aggregation ──
            var payQuery = _context.Payments.Where(p => p.LandlordId == id && !p.IsDeleted);
            var totalCollected = await payQuery
                .Where(p => p.PaymentStatus == PaymentTransactionStatus.Paid)
                .SumAsync(p => p.AmountPaid);
            var totalPending = await payQuery
                .Where(p => p.PaymentStatus == PaymentTransactionStatus.Pending
                         || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid)
                .SumAsync(p => p.Balance);
            var totalOverdue = await payQuery
                .Where(p => p.PaymentStatus == PaymentTransactionStatus.Overdue)
                .SumAsync(p => p.Balance);
            var totalPaidCount = await payQuery.CountAsync(p => p.PaymentStatus == PaymentTransactionStatus.Paid);
            var recentPayments = await payQuery
                .Include(p => p.House).ThenInclude(h => h!.Flat)
                .Include(p => p.Tenant)
                .OrderByDescending(p => p.CreatedAt)
                .Take(5)
                .Select(p => new
                {
                    p.Id,
                    p.Amount,
                    p.AmountPaid,
                    p.Balance,
                    Status = p.PaymentStatus.ToString(),
                    p.PaidAt,
                    p.Month,
                    p.Year,
                    p.IsInitialPayment,
                    HouseNumber = p.House != null ? p.House.HouseNumber : null,
                    FlatName = p.House != null && p.House.Flat != null ? p.House.Flat.FlatName : null,
                    TenantName = p.Tenant != null ? p.Tenant.FirstName + " " + p.Tenant.LastName : null
                })
                .ToListAsync();

            // ── Agreement + ID-document status (delegated to IAgreementService) ──
            var agreement = await _agreementService.GetMyAgreementAsync(id);
            var idDocument = await _agreementService.GetMyIdDocumentAsync(id);

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                Profile = new
                {
                    landlord.Id,
                    landlord.Email,
                    landlord.FirstName,
                    landlord.LastName,
                    landlord.PhoneNumber,
                    landlord.NationalId,
                    landlord.AgencyName,
                    landlord.County,
                    landlord.Constituency,
                    landlord.Ward,
                    landlord.IsActive,
                    landlord.IsEmailVerified,
                    landlord.CreatedAt,
                    landlord.UpdatedAt,
                    PortalUserType = landlord.PortalUserType.ToString()
                },
                Flats = flats,
                Complaints = complaints,
                PaymentsCollected = new
                {
                    TotalCollected = totalCollected,
                    TotalPending = totalPending,
                    TotalOverdue = totalOverdue,
                    TotalPaidCount = totalPaidCount,
                    RecentPayments = recentPayments
                },
                Agreement = agreement,
                IdDocument = idDocument
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building landlord detail: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving the landlord detail"));
        }
    }

    // GET /api/landlord/{id}/payments
    // Same payments-collected summary as GetDetail, with optional flat / date-range / year filters.
    [HttpGet("{id:guid}/payments")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetPayments(
        Guid id,
        [FromQuery] Guid? flatId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int? year = null)
    {
        try
        {
            var payQuery = _context.Payments.Where(p => p.LandlordId == id && !p.IsDeleted);

            if (flatId.HasValue)
                payQuery = payQuery.Where(p => p.House != null && p.House.FlatId == flatId.Value);
            if (fromDate.HasValue)
                payQuery = payQuery.Where(p => p.CreatedAt >= fromDate.Value);
            if (toDate.HasValue)
                payQuery = payQuery.Where(p => p.CreatedAt <= toDate.Value);
            if (year.HasValue)
                payQuery = payQuery.Where(p => p.Year == year.Value);

            var totalCollected = await payQuery
                .Where(p => p.PaymentStatus == PaymentTransactionStatus.Paid)
                .SumAsync(p => p.AmountPaid);
            var totalPending = await payQuery
                .Where(p => p.PaymentStatus == PaymentTransactionStatus.Pending
                         || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid)
                .SumAsync(p => p.Balance);
            var totalOverdue = await payQuery
                .Where(p => p.PaymentStatus == PaymentTransactionStatus.Overdue)
                .SumAsync(p => p.Balance);
            var totalPaidCount = await payQuery.CountAsync(p => p.PaymentStatus == PaymentTransactionStatus.Paid);
            var recentPayments = await payQuery
                .Include(p => p.House).ThenInclude(h => h!.Flat)
                .Include(p => p.Tenant)
                .OrderByDescending(p => p.CreatedAt)
                .Take(5)
                .Select(p => new
                {
                    p.Id,
                    p.Amount,
                    p.AmountPaid,
                    p.Balance,
                    Status = p.PaymentStatus.ToString(),
                    p.PaidAt,
                    p.Month,
                    p.Year,
                    p.IsInitialPayment,
                    HouseNumber = p.House != null ? p.House.HouseNumber : null,
                    FlatName = p.House != null && p.House.Flat != null ? p.House.Flat.FlatName : null,
                    TenantName = p.Tenant != null ? p.Tenant.FirstName + " " + p.Tenant.LastName : null
                })
                .ToListAsync();

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                TotalCollected = totalCollected,
                TotalPending = totalPending,
                TotalOverdue = totalOverdue,
                TotalPaidCount = totalPaidCount,
                RecentPayments = recentPayments
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building landlord payments: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving landlord payments"));
        }
    }

    // GET /api/landlord/{id}/payment-years
    // Distinct years present in this landlord's (non-deleted) payments, plus the current year, sorted descending.
    [HttpGet("{id:guid}/payment-years")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetPaymentYears(Guid id)
    {
        var years = await _context.Payments
            .Where(p => p.LandlordId == id && !p.IsDeleted)
            .Select(p => p.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();

        var currentYear = DateTime.UtcNow.Year;
        if (!years.Contains(currentYear)) years.Insert(0, currentYear);
        years = years.OrderByDescending(y => y).ToList();

        return Ok(years);
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Landlord,Tenant,Agent")]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var landlords = await _landlordService.GetAllAsync();
            return Ok(ApiResponse<object>.SuccessResponse(landlords));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all landlords");
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving landlords"));
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLandlordDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var landlord = await _landlordService.UpdateAsync(id, dto);
            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                landlord.Id,
                landlord.Email,
                landlord.FirstName,
                landlord.LastName,
                landlord.PhoneNumber,
                landlord.NationalId,
                landlord.AgencyName,
                landlord.IsActive,
                landlord.UpdatedAt
            }, "Landlord updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating landlord: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while updating the landlord"));
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var result = await _landlordService.DeleteAsync(id);
            if (!result)
                return NotFound(ApiResponse<object>.FailureResponse("Landlord not found"));

            return Ok(ApiResponse<object?>.SuccessResponse(null, "Landlord deleted successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting landlord: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while deleting the landlord"));
        }
    }

    [HttpPatch("{id}/toggle-status")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> ToggleStatus(Guid id)
    {
        try
        {
            var result = await _landlordService.ToggleStatusAsync(id);
            if (!result)
                return NotFound(ApiResponse<object>.FailureResponse("Landlord not found"));

            return Ok(ApiResponse<object?>.SuccessResponse(null, "Landlord status updated successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling landlord status: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while updating landlord status"));
        }
    }

    [HttpPost("{id:guid}/resend-verification")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> ResendVerificationEmail(Guid id)
    {
        var landlord = await _context.Landlords.FirstOrDefaultAsync(l => l.Id == id);
        if (landlord == null)
            return NotFound(new { success = false, message = "Landlord not found." });

        if (landlord.IsEmailVerified)
            return BadRequest(new { success = false, message = "This landlord has already verified their email." });

        if (string.IsNullOrEmpty(landlord.TemporaryInitialPassword))
            return BadRequest(new { success = false, message = "No temporary password on record — cannot resend. Contact support." });

        landlord.EmailVerificationToken = Guid.NewGuid().ToString("N");
        landlord.EmailVerificationTokenExpiry = DateTime.UtcNow.AddDays(14);
        await _context.SaveChangesAsync();

        var verificationLink = _frontendUrlService.GetPortalEmailVerificationUrl(
            landlord.EmailVerificationToken, landlord.Email, PortalUserType.Landlord);

        var emailSent = false;
        for (var attempt = 1; attempt <= 3 && !emailSent; attempt++)
        {
            try
            {
                await _emailService.SendPortalVerifyWithPasswordEmailAsync(
                    landlord.Email, landlord.FirstName, verificationLink, landlord.TemporaryInitialPassword);
                emailSent = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Resend verification email failed for landlord {Email} (attempt {Attempt}/3)", landlord.Email, attempt);
                if (attempt < 3) await Task.Delay(2000);
            }
        }

        if (!emailSent)
            return BadRequest(new { success = false, message = "Failed to send verification email after 3 attempts." });

        landlord.VerificationEmailSentAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = "Verification email sent." });
    }
}
