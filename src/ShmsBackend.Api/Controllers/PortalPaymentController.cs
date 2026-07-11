using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Payment;
using ShmsBackend.Data.Context;
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
            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.Id == dto.PaymentId && p.TenantId == userId);

            if (payment == null)
                return NotFound(new { success = false, message = "Payment not found." });

            if (payment.PaymentStatus == PaymentTransactionStatus.Paid)
                return BadRequest(new { success = false, message = "Payment already completed." });

            if (dto.Amount.HasValue && dto.Amount.Value <= 0)
                return BadRequest(new { success = false, message = "Amount must be positive." });

            var stkResponse = await _paymentService.InitiatePaymentAsync(payment.Id, dto.PhoneNumber);

            return Ok(new
            {
                success = true,
                message = "Payment initiated. Please check your phone.",
                data = new
                {
                    paymentId = payment.Id,
                    checkoutRequestId = stkResponse.CheckoutRequestID,
                    amount = dto.Amount ?? payment.Balance
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate payment");
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}

public class TenantPayDto
{
    public Guid PaymentId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}
