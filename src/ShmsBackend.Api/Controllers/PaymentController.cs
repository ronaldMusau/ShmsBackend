using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Payment;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IMpesaService _mpesaService;
    private readonly ShmsDbContext _context;
    private readonly ILogger<PaymentController> _logger;

    public PaymentController(
        IPaymentService paymentService,
        IMpesaService mpesaService,
        ShmsDbContext context,
        ILogger<PaymentController> logger)
    {
        _paymentService = paymentService;
        _mpesaService = mpesaService;
        _context = context;
        _logger = logger;
    }

    // POST /api/payments/initiate — create payment record and trigger STK push
    [HttpPost("initiate")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Agent")]
    public async Task<IActionResult> InitiatePayment([FromBody] InitiatePaymentDto dto)
    {
        try
        {
            if (User.IsInRole("Agent"))
            {
                var agentIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!Guid.TryParse(agentIdStr, out var agentId))
                    return Unauthorized();

                var house = await _context.Houses.FindAsync(dto.HouseId);
                if (house == null)
                    return BadRequest(new { success = false, message = "House not found." });

                var authorized = await _context.AgentFlats
                    .AnyAsync(af => af.AgentId == agentId && af.FlatId == house.FlatId);
                if (!authorized)
                    return StatusCode(403, new { success = false, message = "You are not authorized to initiate payments for this flat." });
            }

            var existingProcessing = await _context.Payments
                .Where(p => p.TenantId == dto.TenantId && p.PaymentStatus == PaymentTransactionStatus.Processing && !p.IsDeleted)
                .FirstOrDefaultAsync();
            if (existingProcessing != null)
                return BadRequest(new { success = false, message = "A payment is already being processed for this tenant. Please wait a moment or check their phone." });

            Payment payment;
            if (dto.Amount.HasValue)
            {
                var tenant = await _context.Tenants
                    .FirstOrDefaultAsync(t => t.Id == dto.TenantId);
                if (tenant == null)
                    return BadRequest(new { success = false, message = "Tenant not found." });

                var targetPayment = await _context.Payments
                    .Where(p => p.TenantId == dto.TenantId && p.HouseId == dto.HouseId
                             && p.TenancyCycle == tenant.TenancyCycle
                             && p.Balance > 0 && !p.IsInitialPayment && !p.IsDeleted)
                    .OrderBy(p => p.Year).ThenBy(p => p.Month)
                    .FirstOrDefaultAsync();

                payment = targetPayment
                    ?? await _paymentService.CreateInitialPaymentAsync(dto.TenantId, dto.HouseId, dto.PhoneNumber);
            }
            else
            {
                payment = await _paymentService.CreateInitialPaymentAsync(
                    dto.TenantId, dto.HouseId, dto.PhoneNumber);
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
                    merchantRequestId = stkResponse.MerchantRequestID,
                    amount = actualChargeAmount ?? payment.Amount,
                    rentAmount = payment.RentAmount,
                    depositAmount = payment.DepositAmount,
                    serviceChargeAmount = payment.ServiceChargeAmount
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate payment");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    // POST /api/payments/retry/{paymentId} — retry a failed/cancelled payment
    [HttpPost("retry/{paymentId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> RetryPayment(Guid paymentId, [FromBody] RetryPaymentDto dto)
    {
        try
        {
            var payment = await _context.Payments.FindAsync(paymentId);
            if (payment == null)
                return NotFound(new { success = false, message = "Payment not found." });

            if (payment.PaymentStatus == PaymentTransactionStatus.Paid)
                return BadRequest(new { success = false, message = "Payment is already completed." });

            payment.PaymentStatus = PaymentTransactionStatus.Pending;
            payment.CheckoutRequestId = null;
            payment.MerchantRequestId = null;
            payment.MpesaResultCode = null;
            payment.MpesaResultDesc = null;
            payment.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var stkResponse = await _paymentService.InitiatePaymentAsync(paymentId, dto.PhoneNumber);

            return Ok(new
            {
                success = true,
                message = "Payment retry initiated. Please check your phone.",
                data = new
                {
                    paymentId,
                    checkoutRequestId = stkResponse.CheckoutRequestID,
                    merchantRequestId = stkResponse.MerchantRequestID
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry payment {PaymentId}", paymentId);
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    // POST /api/payments/mpesa/callback — M-Pesa callback (no auth)
    [HttpPost("mpesa/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> MpesaCallback([FromBody] MpesaCallback callback)
    {
        try
        {
            _logger.LogInformation("M-Pesa callback received: {ResultCode}",
                callback.Body.stkCallback.ResultCode);

            await _paymentService.ProcessCallbackAsync(callback);

            return Ok(new { ResultCode = 0, ResultDesc = "Accepted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing M-Pesa callback");
            return Ok(new { ResultCode = 0, ResultDesc = "Accepted" });
        }
    }

    // GET /api/payments/status/{checkoutRequestId} — poll payment status
    [HttpGet("status/{checkoutRequestId}")]
    [Authorize]
    public async Task<IActionResult> GetPaymentStatus(string checkoutRequestId)
    {
        try
        {
            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.CheckoutRequestId == checkoutRequestId);

            if (payment != null)
            {
                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        status = payment.PaymentStatus.ToString(),
                        paymentId = payment.Id,
                        amountPaid = payment.AmountPaid,
                        balance = payment.Balance,
                        mpesaReceiptNumber = payment.MpesaReceiptNumber,
                        resultCode = payment.MpesaResultCode,
                        resultDesc = payment.MpesaResultDesc,
                        retryCount = payment.RetryCount
                    }
                });
            }

            // If not in DB yet, query M-Pesa directly
            var stkStatus = await _paymentService.QueryPaymentStatusAsync(checkoutRequestId);
            return Ok(new
            {
                success = true,
                data = new
                {
                    status = stkStatus.ResultCode == "0" ? "Paid" : "Pending",
                    resultCode = stkStatus.ResultCode,
                    resultDesc = stkStatus.ResultDesc
                }
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    // GET /api/payments/tenant/{tenantId} — get payment history for a tenant
    [HttpGet("tenant/{tenantId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetTenantPayments(Guid tenantId)
    {
        var payments = await _paymentService.GetTenantPaymentHistoryAsync(tenantId);
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
                p.ServiceChargeAmount,
                p.CreditApplied,
                Status = p.PaymentStatus.ToString(),
                Type = p.PaymentType.ToString(),
                Method = p.PaymentMethod?.ToString(),
                p.MpesaReceiptNumber,
                p.PhoneNumber,
                p.DueDate,
                p.PaidAt,
                p.Month,
                p.Year,
                p.IsInitialPayment,
                p.Description,
                p.RetryCount,
                p.CreatedAt
            })
        });
    }

    // GET /api/payments/house/{houseId} — get payment history for a house
    [HttpGet("house/{houseId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetHousePayments(Guid houseId)
    {
        var payments = await _paymentService.GetHousePaymentHistoryAsync(houseId);
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
                p.ServiceChargeAmount,
                Status = p.PaymentStatus.ToString(),
                Type = p.PaymentType.ToString(),
                p.MpesaReceiptNumber,
                p.DueDate,
                p.PaidAt,
                p.Month,
                p.Year,
                p.IsInitialPayment,
                TenantName = p.Tenant != null
                    ? $"{p.Tenant.FirstName} {p.Tenant.LastName}"
                    : null,
                p.CreatedAt
            })
        });
    }

    // GET /api/payments/house/{houseId}/summary — payment summary for house modal
    [HttpGet("house/{houseId:guid}/summary")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant,Landlord")]
    public async Task<IActionResult> GetHousePaymentSummary(Guid houseId)
    {
        var payments = await _context.Payments
            .Include(p => p.Tenant)
            .Where(p => p.HouseId == houseId)
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => new {
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
                TenantName = p.Tenant != null
                    ? $"{p.Tenant.FirstName} {p.Tenant.LastName}"
                    : null,
                p.CreatedAt
            })
            .ToListAsync();

        var summary = new
        {
            TotalCollected = payments.Where(p => p.Status == "Paid")
                .Sum(p => p.AmountPaid),
            TotalPending = payments.Where(p => p.Status == "Pending" || p.Status == "PartiallyPaid")
                .Sum(p => (decimal)p.Balance),
            TotalOverdue = payments.Where(p => p.Status == "Overdue")
                .Sum(p => (decimal)p.Balance),
            PaymentCount = payments.Count,
            Payments = payments
        };

        return Ok(new { success = true, data = summary });
    }

    [HttpGet("all")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Accountant")]
    public async Task<IActionResult> GetAllPayments(
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] Guid? flatId,
        [FromQuery] Guid? houseId,
        [FromQuery] Guid? tenantId,
        [FromQuery] string? method,
        [FromQuery] int? month,
        [FromQuery] int? year,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] decimal? minAmount,
        [FromQuery] decimal? maxAmount,
        [FromQuery] bool? isInitialPayment,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var query = _context.Payments
            .Include(p => p.House)
            .ThenInclude(h => h!.Flat)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<PaymentTransactionStatus>(status, out var ps))
            query = query.Where(p => p.PaymentStatus == ps);

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<PaymentType>(type, out var pt))
            query = query.Where(p => p.PaymentType == pt);

        if (flatId.HasValue)
            query = query.Where(p => p.FlatId == flatId.Value);

        if (houseId.HasValue)
            query = query.Where(p => p.HouseId == houseId.Value);

        if (tenantId.HasValue)
            query = query.Where(p => p.TenantId == tenantId.Value);

        if (!string.IsNullOrEmpty(method) && Enum.TryParse<PaymentMethod>(method, out var pm))
            query = query.Where(p => p.PaymentMethod == pm);

        if (month.HasValue)
            query = query.Where(p => p.Month == month.Value);

        if (year.HasValue)
            query = query.Where(p => p.Year == year.Value);

        if (fromDate.HasValue)
            query = query.Where(p => p.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(p => p.CreatedAt <= toDate.Value);

        if (minAmount.HasValue)
            query = query.Where(p => p.Amount >= minAmount.Value);

        if (maxAmount.HasValue)
            query = query.Where(p => p.Amount <= maxAmount.Value);

        if (isInitialPayment.HasValue)
            query = query.Where(p => p.IsInitialPayment == isInitialPayment.Value);

        var total = await query.CountAsync();

        var totalCollected = await query.Where(p => p.PaymentStatus == PaymentTransactionStatus.Paid).SumAsync(p => p.AmountPaid);
        var totalPending = await query.Where(p => p.PaymentStatus == PaymentTransactionStatus.Pending || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid).SumAsync(p => p.Balance);
        var totalOverdue = await query.Where(p => p.PaymentStatus == PaymentTransactionStatus.Overdue).SumAsync(p => p.Balance);
        var totalServiceCharge = await query.SumAsync(p => p.ServiceChargeAmount ?? 0);

        var pagedPayments = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Load tenant names ignoring soft-delete so deleted tenants still show their identity
        var tenantIds = pagedPayments.Select(p => p.TenantId).Distinct().ToList();
        var tenantLookup = await _context.Tenants
            .IgnoreQueryFilters()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.FirstName, t.LastName, t.Email })
            .ToDictionaryAsync(t => t.Id);

        var payments = pagedPayments.Select(p =>
        {
            tenantLookup.TryGetValue(p.TenantId, out var tenant);
            return new
            {
                p.Id,
                p.Amount,
                p.AmountPaid,
                p.Balance,
                p.RentAmount,
                p.DepositAmount,
                p.ServiceChargeAmount,
                p.CreditApplied,
                Status = p.PaymentStatus.ToString(),
                Type = p.PaymentType.ToString(),
                Method = p.PaymentMethod.ToString(),
                p.MpesaReceiptNumber,
                p.PhoneNumber,
                p.DueDate,
                p.PaidAt,
                p.Month,
                p.Year,
                p.IsInitialPayment,
                p.RetryCount,
                p.CreatedAt,
                TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}" : null,
                TenantEmail = tenant?.Email,
                HouseNumber = p.House != null ? p.House.HouseNumber : null,
                FlatName = p.House != null && p.House.Flat != null
                    ? p.House.Flat.FlatName : null,
                p.HouseId,
                p.FlatId,
                p.TenantId
            };
        }).ToList();

        return Ok(new
        {
            success = true,
            data = payments,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize),
            totals = new { totalCollected, totalPending, totalOverdue, totalServiceCharge }
        });
    }

    // GET /api/payments/service-charges — get service charge settings
    [HttpGet("service-charges")]
    [Authorize]
    public async Task<IActionResult> GetServiceCharges()
    {
        var settings = await _context.ServiceChargeSettings
            .Where(s => s.IsActive)
            .OrderBy(s => s.MinRent)
            .ToListAsync();

        return Ok(new { success = true, data = settings });
    }

    // POST /api/payments/service-charges — create service charge setting
    [HttpPost("service-charges")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> CreateServiceCharge([FromBody] ServiceChargeDto dto)
    {
        var setting = new Data.Models.Entities.ServiceChargeSetting
        {
            MinRent = dto.MinRent,
            MaxRent = dto.MaxRent,
            ServiceCharge = dto.ServiceCharge,
            Description = dto.Description,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.ServiceChargeSettings.AddAsync(setting);
        await _context.SaveChangesAsync();
        return Ok(new { success = true, data = setting });
    }

    // PUT /api/payments/service-charges/{id} — update service charge setting
    [HttpPut("service-charges/{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> UpdateServiceCharge(Guid id, [FromBody] ServiceChargeDto dto)
    {
        var setting = await _context.ServiceChargeSettings.FindAsync(id);
        if (setting == null) return NotFound(new { success = false, message = "Not found." });

        setting.MinRent = dto.MinRent;
        setting.MaxRent = dto.MaxRent;
        setting.ServiceCharge = dto.ServiceCharge;
        setting.Description = dto.Description;
        setting.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { success = true, data = setting });
    }

    // DELETE /api/payments/service-charges/{id} — delete service charge setting
    [HttpDelete("service-charges/{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> DeleteServiceCharge(Guid id)
    {
        var setting = await _context.ServiceChargeSettings.FindAsync(id);
        if (setting == null) return NotFound(new { success = false, message = "Not found." });

        setting.IsDeleted = true;
        setting.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Deleted." });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> DeletePayment(Guid id)
    {
        var payment = await _context.Payments.FindAsync(id);
        if (payment == null)
            return NotFound(new { success = false, message = "Payment not found." });

        payment.IsDeleted = true;
        payment.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = "Payment deleted." });
    }
}

public class InitiatePaymentDto
{
    public Guid TenantId { get; set; }
    public Guid HouseId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public class RetryPaymentDto
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ServiceChargeDto
{
    public decimal MinRent { get; set; }
    public decimal MaxRent { get; set; }
    public decimal ServiceCharge { get; set; }
    public string? Description { get; set; }
}
