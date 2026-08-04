using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using System.Security.Claims;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/vacate")]
[Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Tenant")]
public class VacateController : ControllerBase
{
    private readonly ShmsDbContext _context;
    private readonly ILogger<VacateController> _logger;

    public VacateController(ShmsDbContext context, ILogger<VacateController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private Guid GetCallerId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    // GET /api/vacate/eligibility/{tenantId}
    [HttpGet("eligibility/{tenantId:guid}")]
    public async Task<IActionResult> GetEligibility(Guid tenantId)
    {
        var callerRole = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (callerRole == "Tenant" && GetCallerId() != tenantId)
            return Forbid();

        var tenant = await _context.Tenants
            .Include(t => t.House)
                .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted);

        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        if (tenant.House == null || tenant.House.Flat == null)
            return BadRequest(new { success = false, message = "Tenant is not assigned to a house." });

        var arrears = await _context.Payments
            .Where(p => p.TenantId == tenantId
                && p.TenancyCycle == tenant.TenancyCycle
                && !p.IsDeleted
                && (p.PaymentStatus == PaymentTransactionStatus.Overdue
                    || p.PaymentStatus == PaymentTransactionStatus.Pending
                    || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid))
            .SumAsync(p => p.Balance);

        var totalPaid = await _context.Payments
            .Where(p => p.TenantId == tenantId && !p.IsDeleted)
            .SumAsync(p => p.AmountPaid);

        var totalDue = await _context.Payments
            .Where(p => p.TenantId == tenantId
                && !p.IsDeleted
                && p.PaymentStatus != PaymentTransactionStatus.Cancelled
                && p.PaymentStatus != PaymentTransactionStatus.Failed)
            .SumAsync(p => p.Amount);

        var advanceCredit = Math.Max(0, totalPaid - totalDue);

        var today = DateTime.UtcNow.Day;
        var deadlineDay = tenant.House.Flat.VacateNoticeDeadlineDay;
        var windowOpen = today <= deadlineDay;

        return Ok(new
        {
            success = true,
            data = new
            {
                hasArrears = arrears > 0,
                arrearsAmount = arrears,
                hasAdvanceCredit = advanceCredit > 0,
                advanceCreditAmount = advanceCredit,
                windowOpen,
                vacateNoticeDeadlineDay = deadlineDay,
                sitDeposit = tenant.House.Flat.SitDeposit,
                depositFee = tenant.House.DepositFee
            }
        });
    }
}
