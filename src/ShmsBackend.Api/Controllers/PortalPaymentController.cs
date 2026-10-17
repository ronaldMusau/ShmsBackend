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

    private static bool IsAfterMonth(int year, int month, int anchorYear, int anchorMonth)
        => year > anchorYear || (year == anchorYear && month > anchorMonth);

    private BadRequestObjectResult VacateCapExceeded(VacateOwedBreakdown owed)
        => BadRequest(new
        {
            success = false,
            errorType = "VacateCapExceeded",
            message = $"You can pay up to KES {owed.Total:N2} before your vacate month — this would exceed that.",
            vacateOwed = owed
        });

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
        var payments = allPayments
            .Where(p => p.TenancyCycle == tenant.TenancyCycle && !p.IsDeleted
                     && (p.PaymentStatus == PaymentTransactionStatus.Paid
                         || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid
                         || p.PaymentStatus == PaymentTransactionStatus.Overdue))
            .ToList();

        var totalCollected = payments.Where(p => p.PaymentStatus == PaymentTransactionStatus.Paid).Sum(p => p.AmountPaid);
        var totalOverdue = payments.Where(p => p.PaymentStatus == PaymentTransactionStatus.Overdue).Sum(p => p.Balance);
        var totalPaidCount = payments.Count(p => p.PaymentStatus == PaymentTransactionStatus.Paid);

        return Ok(new
        {
            success = true,
            totals = new { totalCollected, totalOverdue, totalPaidCount },
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
                Method = p.PaymentMethod.ToString(),
                p.MpesaReceiptNumber,
                p.RedemptionReference,
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

        // With an approved vacate, also return what's left to pay through the vacate month (overdue / still to
        // come / total) so the Pay Rent tab can show it. Response is unchanged for everyone else.
        var activeVacate = await _paymentService.GetActiveApprovedVacateRequestAsync(userId);
        var vacateOwed = activeVacate == null
            ? null
            : await _paymentService.GetOwedThroughMonthAsync(
                userId, activeVacate.HouseId, activeVacate.TenancyCycle, activeVacate.VacateMonth, activeVacate.VacateYear);

        if (payment == null)
        {
            if (vacateOwed != null)
                return Ok(new { success = true, data = (object?)null, message = "No payment due this month.", vacateOwed });
            return Ok(new { success = true, data = (object?)null, message = "No payment due this month." });
        }

        var data = new
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
        };

        if (vacateOwed != null)
            return Ok(new { success = true, data, vacateOwed });
        return Ok(new { success = true, data });
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

        using var tenantLock = await ShmsBackend.Api.Helpers.TenantPaymentLock.AcquireAsync(userId);
        try
        {
            if (dto.Amount.HasValue && dto.Amount.Value <= 0)
                return BadRequest(new { success = false, message = "Amount must be positive." });

            var payingTenant = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == userId);
            if (payingTenant != null && TenantAccessHelper.IsReadOnlyUntilMoveIn(payingTenant))
                return BadRequest(new { success = false, message = "Your account is read-only until your move-in month arrives." });

            var existingProcessing = await _context.PaymentCheckoutAttempts
                .Where(a => a.AttemptStatus == "Processing" && a.ProcessedAt == null && a.Payment!.TenantId == userId && !a.Payment.IsDeleted)
                .FirstOrDefaultAsync();
            if (existingProcessing != null)
                return BadRequest(new { success = false, message = "A payment is already being processed. Please wait a moment or check your phone." });

            // Once a vacate is approved, the tenant can pay at most what's still owed through the vacate month.
            var activeVacate = await _paymentService.GetActiveApprovedVacateRequestAsync(userId);
            VacateOwedBreakdown? vacateOwed = activeVacate == null
                ? null
                : await _paymentService.GetOwedThroughMonthAsync(
                    userId, activeVacate.HouseId, activeVacate.TenancyCycle, activeVacate.VacateMonth, activeVacate.VacateYear);

            if (vacateOwed != null && dto.Amount.HasValue && dto.Amount.Value > vacateOwed.Total)
                return VacateCapExceeded(vacateOwed);

            // The oldest row still owing up to the current month (or the vacate month, if that's earlier) anchors
            // the STK push, with or without an amount — ProcessCallbackAsync fills the anchor, then
            // PaymentDistributionService's arrears loop settles the remaining rows oldest-first (same mechanism
            // as PaymentController's pay-on-behalf), so an entered amount clears arrears before later months.
            // No amount means "pay what's currently due": with several rows owing, charge the combined balance
            // as a lump sum.
            Payment? payment = null;
            decimal? arrearsLumpSum = null;
            if (payingTenant?.HouseId != null)
            {
                var now = DateTime.UtcNow;
                var (boundYear, boundMonth) = (now.Year, now.Month);
                if (activeVacate != null && IsAfterMonth(boundYear, boundMonth, activeVacate.VacateYear, activeVacate.VacateMonth))
                    (boundYear, boundMonth) = (activeVacate.VacateYear, activeVacate.VacateMonth);

                // "PAYABLE NOW" (calendar month <= bound, no DueDate check) — deliberately looser than the STRICT
                // overdue test in OverdueController/VacateController (DueDate has passed); picks every self-pay's anchor.
                var duePayments = await _context.Payments
                    .Where(p => p.TenantId == userId && p.HouseId == payingTenant.HouseId
                             && p.TenancyCycle == payingTenant.TenancyCycle
                             && p.Balance > 0 && !p.IsInitialPayment && !p.IsDeleted
                             && (p.Year < boundYear || (p.Year == boundYear && p.Month <= boundMonth)))
                    .OrderBy(p => p.Year).ThenBy(p => p.Month)
                    .ToListAsync();

                if (duePayments.Count > 0)
                {
                    payment = duePayments[0];
                    if (!dto.Amount.HasValue && duePayments.Count > 1)
                        arrearsLumpSum = duePayments.Sum(p => p.Balance);
                }
            }

            if (payment == null)
            {
                // Nothing owing through the current/vacate month. With an amount that's paying ahead, so a named
                // row is ignored and the advance-payment row is used; without one, a named unpaid row still applies.
                Payment? existing = null;
                if (dto.PaymentId.HasValue && !dto.Amount.HasValue)
                {
                    existing = await _context.Payments
                        .FirstOrDefaultAsync(p => p.Id == dto.PaymentId.Value && p.TenantId == userId);
                }

                var useExisting = existing != null && existing.PaymentStatus != PaymentTransactionStatus.Paid;

                // Nothing left owing through the vacate month — refuse before the advance row gets created.
                if (vacateOwed != null && !useExisting && vacateOwed.Total <= 0)
                    return VacateCapExceeded(vacateOwed);

                // A named row that's already Paid means the tenant is paying ahead (the prepay flow sends the
                // current, settled month's id) — route to the advance-payment row exactly as if no id was sent,
                // rather than rejecting, and never apply new money onto the settled row itself.
                if (useExisting)
                    payment = existing!;
                else
                {
                    var (advanceRow, pastVacateMonth) = await _paymentService.CreateAdvancePaymentRowAsync(userId);
                    if (pastVacateMonth && vacateOwed != null)
                        return VacateCapExceeded(vacateOwed);
                    payment = advanceRow ?? throw new Exception("No house assigned to your account.");
                }
            }

            var distributionAmount = dto.Amount ?? arrearsLumpSum;

            // Final check on what this push would actually charge (excluding service charge): the explicit amount,
            // the lump sum, or the single row's own balance — and never anchor on a row after the vacate month.
            if (activeVacate != null && vacateOwed != null)
            {
                var charge = distributionAmount ?? (payment.Balance > 0 ? payment.Balance : payment.Amount);
                if (charge > vacateOwed.Total
                    || IsAfterMonth(payment.Year, payment.Month, activeVacate.VacateYear, activeVacate.VacateMonth))
                    return VacateCapExceeded(vacateOwed);
            }

            decimal? actualChargeAmount = null;
            if (distributionAmount.HasValue)
            {
                var serviceCharge = payment.ServiceChargeAmount ?? 0m;
                actualChargeAmount = distributionAmount.Value + serviceCharge;
                payment.RequestedDistributionAmount = distributionAmount.Value;
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

        var attempt = await _context.PaymentCheckoutAttempts
            .Include(a => a.Payment)
            .FirstOrDefaultAsync(a => a.CheckoutRequestId == checkoutRequestId);
        if (attempt == null)
            return NotFound(new { success = false, message = "Checkout attempt not found." });

        if (attempt.Payment == null || attempt.Payment.TenantId != userId)
            return StatusCode(403, new { success = false, message = "You do not have permission to update this payment." });

        if (attempt.AttemptStatus == "Processing")
        {
            attempt.AttemptStatus = "Failed";
            attempt.ResultDesc = "Timed out — client gave up waiting";
            attempt.RetryCount++;
            // ProcessedAt stays null — reserved for a real M-Pesa callback, which may still arrive.
            await _context.SaveChangesAsync();
        }

        return Ok(new { success = true, attemptStatus = attempt.AttemptStatus, paymentStatus = attempt.Payment?.PaymentStatus.ToString() });
    }

    // GET /api/portalpayments/landlord/my-payments — landlord views payments across their properties
    [HttpGet("landlord/my-payments")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordPayments(
        [FromQuery] Guid? flatId,
        [FromQuery] Guid? houseId,
        [FromQuery] string? status,
        [FromQuery] string? stage,
        [FromQuery] int? month,
        [FromQuery] int? year,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] decimal? minAmount,
        [FromQuery] decimal? maxAmount,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
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

        if (houseId.HasValue)
            query = query.Where(p => p.HouseId == houseId.Value);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<PaymentTransactionStatus>(status, out var ps))
            query = query.Where(p => p.PaymentStatus == ps);
        else
            query = query.Where(p => p.PaymentStatus == PaymentTransactionStatus.Paid
                                  || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid
                                  || p.PaymentStatus == PaymentTransactionStatus.Overdue);

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

        // Free-text search — same convention/field set as PaymentController.GetAllPayments: plain
        // .Contains(), no .ToLower(). Tenant/House/Flat are already Include()'d above for this query,
        // so no fresh Include is needed. PaymentStatus is excluded — it's an enum, not a string, and
        // there's already a dedicated `status` exact-match filter above.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(p =>
                (p.Tenant != null && (p.Tenant.FirstName.Contains(s) || p.Tenant.LastName.Contains(s) || p.Tenant.Email.Contains(s))) ||
                (p.House != null && (p.House.HouseNumber.Contains(s) || (p.House.Flat != null && p.House.Flat.FlatName.Contains(s)))) ||
                (p.MpesaReceiptNumber != null && p.MpesaReceiptNumber.Contains(s)) ||
                (p.RedemptionReference != null && p.RedemptionReference.Contains(s)));
        }

        var total = await query.CountAsync();
        var totalCollected = await query.Where(p => p.PaymentStatus == PaymentTransactionStatus.Paid).SumAsync(p => p.AmountPaid);
        var totalPending = await query.Where(p => p.PaymentStatus == PaymentTransactionStatus.Pending || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid).SumAsync(p => p.Balance);
        var totalOverdue = await query.Where(p => p.PaymentStatus == PaymentTransactionStatus.Overdue).SumAsync(p => p.Balance);
        var totalPaidCount = await query.CountAsync(p => p.PaymentStatus == PaymentTransactionStatus.Paid);

        var pagedPayments = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            success = true,
            data = new
            {
                data = pagedPayments.Select(p => new
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
                    Method = p.PaymentMethod.ToString(),
                    p.MpesaReceiptNumber,
                    p.RedemptionReference,
                    p.PaidAt,
                    p.FlatId,
                    HouseNumber = p.House?.HouseNumber,
                    FlatName = p.House?.Flat?.FlatName,
                    TenantName = p.Tenant != null ? $"{p.Tenant.FirstName} {p.Tenant.LastName}" : null
                }),
                totalCount = total,
                totalPages = (int)Math.Ceiling((double)total / pageSize),
                totals = new { totalCollected, totalPending, totalOverdue, totalPaidCount }
            }
        });
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
}

public class TenantPayDto
{
    public Guid? PaymentId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}
