using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Payment;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities.Portal;
using System.Security.Claims;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/portalpayments")]
[Authorize]
public class PortalPaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ShmsDbContext _context;
    private readonly ILogger<PortalPaymentController> _logger;

    public PortalPaymentController(
        IPaymentService paymentService,
        ShmsDbContext context,
        ILogger<PortalPaymentController> logger)
    {
        _paymentService = paymentService;
        _context = context;
        _logger = logger;
    }

    // GET /api/portalpayments/my-payments — tenant views their own payments
    [HttpGet("my-payments")]
    public async Task<IActionResult> GetMyPayments()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var tenant = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == userId);
        if (tenant == null) return Unauthorized();

        var allPayments = await _paymentService.GetTenantPaymentHistoryAsync(userId);
        var payments = allPayments.Where(p => p.TenancyCycle == tenant.TenancyCycle && !p.IsDeleted).ToList();
        return Ok(new
        {
            success = true,
            data = payments.Select(p => new
            {
                p.Id,
                p.Amount,
                p.AmountPaid,
                p.Balance,
                p.RentAmount,
                p.DepositAmount,
                p.CreditApplied,
                Status = p.PaymentStatus.ToString(),
                Type = p.PaymentType.ToString(),
                p.MpesaReceiptNumber,
                p.DueDate,
                p.PaidAt,
                p.Month,
                p.Year,
                p.IsInitialPayment,
                p.Description,
                p.RetryCount,
                HouseNumber = p.House?.HouseNumber,
                FlatName = p.House?.Flat?.FlatName,
                p.CreatedAt
            })
        });
    }

    // GET /api/portalpayments/current-month — get current month payment
    [HttpGet("current-month")]
    public async Task<IActionResult> GetCurrentMonthPayment()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var tenant = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == userId);
        if (tenant == null) return Unauthorized();

        var now = DateTime.UtcNow;
        var payment = await _context.Payments
            .Where(p => p.TenantId == userId &&
                        p.Month == now.Month &&
                        p.Year == now.Year &&
                        !p.IsInitialPayment &&
                        p.TenancyCycle == tenant.TenancyCycle &&
                        !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (payment == null)
            return Ok(new { success = true, data = (object?)null, message = "No payment due this month." });

        return Ok(new
        {
            success = true,
            data = new
            {
                payment.Id,
                payment.Amount,
                payment.AmountPaid,
                payment.Balance,
                payment.RentAmount,
                Status = payment.PaymentStatus.ToString(),
                payment.DueDate,
                payment.Month,
                payment.Year,
                payment.Description
            }
        });
    }

    // GET /api/portalpayments/house/{houseId}/current-status — agent views current-month payment status for a house
    [HttpGet("house/{houseId:guid}/current-status")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> GetHouseCurrentPaymentStatus(Guid houseId)
    {
        var agentIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(agentIdStr, out var agentId))
            return Unauthorized();

        var house = await _context.Houses.FindAsync(houseId);
        if (house == null)
            return NotFound(new { success = false, message = "House not found." });

        var authorized = await _context.AgentFlats
            .AnyAsync(af => af.AgentId == agentId && af.FlatId == house.FlatId);
        if (!authorized)
            return StatusCode(403, new { success = false, message = "Not authorized for this house." });

        var now = DateTime.UtcNow;
        var currentPayment = await _context.Payments
            .Where(p => p.HouseId == houseId && p.Month == now.Month && p.Year == now.Year && !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                Status = p.PaymentStatus.ToString(),
                p.Amount,
                p.AmountPaid,
                p.Balance,
                p.DueDate
            })
            .FirstOrDefaultAsync();

        return Ok(new { success = true, data = currentPayment });
    }

    // POST /api/portalpayments/pay — tenant initiates their own payment
    [HttpPost("pay")]
    public async Task<IActionResult> Pay([FromBody] TenantPayDto dto)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        try
        {
            if (dto.Amount.HasValue && dto.Amount.Value <= 0)
                return BadRequest(new { success = false, message = "Amount must be positive." });

            var existingProcessing = await _context.Payments
                .Where(p => p.TenantId == userId && p.PaymentStatus == PaymentTransactionStatus.Processing && !p.IsDeleted)
                .FirstOrDefaultAsync();
            if (existingProcessing != null)
                return BadRequest(new { success = false, message = "A payment is already being processed. Please wait a moment or check your phone." });

            Payment payment;

            if (dto.PaymentId.HasValue)
            {
                var existing = await _context.Payments
                    .FirstOrDefaultAsync(p => p.Id == dto.PaymentId.Value && p.TenantId == userId);

                if (existing != null && existing.PaymentStatus == PaymentTransactionStatus.Paid)
                    return BadRequest(new { success = false, message = "Payment already completed." });

                payment = existing ?? await CreateAdvancePaymentRowAsync(userId)
                    ?? throw new Exception("No house assigned to your account.");
            }
            else
            {
                payment = await CreateAdvancePaymentRowAsync(userId)
                    ?? throw new Exception("No house assigned to your account.");
            }

            decimal? actualChargeAmount = null;
            if (dto.Amount.HasValue)
            {
                var serviceCharge = payment.ServiceChargeAmount ?? 0m;
                actualChargeAmount = dto.Amount.Value + serviceCharge;
                payment.RequestedDistributionAmount = dto.Amount.Value;
                payment.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            var stkResponse = await _paymentService.InitiatePaymentAsync(payment.Id, dto.PhoneNumber, actualChargeAmount);

            return Ok(new
            {
                success = true,
                message = "Payment initiated. Please check your phone.",
                data = new
                {
                    paymentId = payment.Id,
                    checkoutRequestId = stkResponse.CheckoutRequestID,
                    amount = actualChargeAmount ?? payment.Balance
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate payment");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    // POST /api/portalpayments/mark-timeout/{checkoutRequestId} — tenant marks their own stuck payment as failed
    [HttpPost("mark-timeout/{checkoutRequestId}")]
    public async Task<IActionResult> MarkPaymentTimeout(string checkoutRequestId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var payment = await _context.Payments.FirstOrDefaultAsync(p => p.CheckoutRequestId == checkoutRequestId);
        if (payment == null)
            return NotFound(new { success = false, message = "Payment not found." });

        if (payment.TenantId != userId)
            return StatusCode(403, new { success = false, message = "You do not have permission to update this payment." });

        if (payment.PaymentStatus == PaymentTransactionStatus.Processing)
        {
            payment.PaymentStatus = PaymentTransactionStatus.Failed;
            payment.MpesaResultDesc = "No response from user.";
            payment.RetryCount++;
            payment.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return Ok(new { success = true, status = payment.PaymentStatus.ToString() });
    }

    // GET /api/portalpayments/landlord/my-payments — landlord views payments across their properties
    [HttpGet("landlord/my-payments")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordPayments(
        [FromQuery] Guid? flatId,
        [FromQuery] string? status,
        [FromQuery] string? stage,
        [FromQuery] int? month,
        [FromQuery] int? year,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] decimal? minAmount,
        [FromQuery] decimal? maxAmount)
    {
        var landlordIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(landlordIdStr, out var landlordId))
            return Unauthorized();

        var query = _context.Payments
            .Include(p => p.House)
                .ThenInclude(h => h!.Flat)
            .Include(p => p.Tenant)
            .Where(p => p.LandlordId == landlordId && !p.IsDeleted)
            .AsQueryable();

        if (flatId.HasValue)
            query = query.Where(p => p.FlatId == flatId.Value);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<PaymentTransactionStatus>(status, out var ps))
            query = query.Where(p => p.PaymentStatus == ps);

        if (!string.IsNullOrEmpty(stage))
        {
            if (stage.Equals("Initial", StringComparison.OrdinalIgnoreCase))
                query = query.Where(p => p.IsInitialPayment);
            else if (stage.Equals("Monthly", StringComparison.OrdinalIgnoreCase))
                query = query.Where(p => !p.IsInitialPayment);
        }

        if (month.HasValue)
            query = query.Where(p => p.Month == month.Value);

        if (year.HasValue)
            query = query.Where(p => p.Year == year.Value);

        if (fromDate.HasValue)
            query = query.Where(p => p.PaidAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(p => p.PaidAt <= toDate.Value);

        if (minAmount.HasValue)
            query = query.Where(p => p.Amount >= minAmount.Value);

        if (maxAmount.HasValue)
            query = query.Where(p => p.Amount <= maxAmount.Value);

        var allPayments = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();

        var totalCollected = allPayments
            .Where(p => p.PaymentStatus == PaymentTransactionStatus.Paid)
            .Sum(p => p.AmountPaid);
        var totalOverdue = allPayments
            .Where(p => p.PaymentStatus == PaymentTransactionStatus.Overdue)
            .Sum(p => p.Balance);
        var totalPaid = allPayments
            .Count(p => p.PaymentStatus == PaymentTransactionStatus.Paid);

        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            totals = new { totalCollected, totalOverdue, totalPaid },
            data = allPayments.Select(p => new
            {
                p.Id,
                p.Month,
                p.Year,
                p.IsInitialPayment,
                p.Amount,
                p.AmountPaid,
                p.Balance,
                p.RentAmount,
                p.DepositAmount,
                Status = p.PaymentStatus.ToString(),
                p.MpesaReceiptNumber,
                p.PaidAt,
                p.FlatId,
                HouseNumber = p.House?.HouseNumber,
                FlatName = p.House?.Flat?.FlatName,
                TenantName = p.Tenant != null ? $"{p.Tenant.FirstName} {p.Tenant.LastName}" : null
            })
        }));
    }

    [HttpGet("{id}/applications")]
    public async Task<IActionResult> GetMyPaymentApplications(Guid id)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var payment = await _context.Payments
            .Include(p => p.House)
                .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (payment == null)
            return NotFound(new { success = false, message = "Payment not found." });

        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        var authorized = (role == "Tenant" && payment.TenantId == userId)
            || (role == "Landlord" && payment.House?.Flat?.LandlordId == userId);

        if (!authorized)
            return StatusCode(403, new { success = false, message = "You do not have permission to view this payment." });

        var applications = await _context.PaymentApplications
            .Where(a => a.PaymentId == id)
            .OrderBy(a => a.AppliedAt)
            .Select(a => new
            {
                a.Id,
                a.MpesaReceiptNumber,
                a.AmountApplied,
                a.AppliedAt
            })
            .ToListAsync();

        return Ok(new { success = true, data = applications });
    }

    private async Task<Payment?> CreateAdvancePaymentRowAsync(Guid userId)
    {
        var tenant = await _context.Tenants
            .Include(t => t.House)
                .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(t => t.Id == userId);

        if (tenant?.House == null || tenant.House.Flat == null)
            return null;

        var house = tenant.House;
        var flat = house.Flat;
        var serviceCharge = await _paymentService.GetServiceChargeAsync(house.RentFee);
        var totalDue = house.RentFee;
        var now = DateTime.UtcNow;

        // Find earliest month with no existing non-initial payment row for this tenant+cycle
        var cursorMonth = now.Month;
        var cursorYear = now.Year;
        for (var i = 0; i < 12; i++)
        {
            var already = await _context.Payments.AnyAsync(p =>
                p.TenantId == userId && p.HouseId == house.Id
                && p.TenancyCycle == tenant.TenancyCycle
                && p.Month == cursorMonth && p.Year == cursorYear
                && !p.IsDeleted);

            if (!already) break;

            cursorMonth++;
            if (cursorMonth > 12) { cursorMonth = 1; cursorYear++; }
        }

        var rentDueDay = Math.Min(flat.RentDueDay, DateTime.DaysInMonth(cursorYear, cursorMonth));
        var newPayment = new Payment
        {
            Id = Guid.NewGuid(),
            TenantId = userId,
            HouseId = house.Id,
            FlatId = flat.Id,
            LandlordId = flat.LandlordId,
            Amount = totalDue,
            AmountPaid = 0,
            Balance = totalDue,
            RentAmount = house.RentFee,
            ServiceChargeAmount = serviceCharge,
            PaymentStatus = PaymentTransactionStatus.Pending,
            PaymentType = PaymentType.Rent,
            PhoneNumber = tenant.PhoneNumber,
            DueDate = new DateTime(cursorYear, cursorMonth, rentDueDay),
            Month = cursorMonth,
            Year = cursorYear,
            TenancyCycle = tenant.TenancyCycle,
            IsInitialPayment = false,
            Description = $"Monthly rent - {new DateTime(cursorYear, cursorMonth, 1):MMMM yyyy}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Payments.AddAsync(newPayment);
        await _context.SaveChangesAsync();
        return newPayment;
    }
}

public class TenantPayDto
{
    public Guid? PaymentId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}
