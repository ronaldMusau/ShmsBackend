using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Reward;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/portalrewards")]
[Authorize(Roles = "Tenant")]
public class PortalRewardController : ControllerBase
{
    private readonly ShmsDbContext _context;
    private readonly IRewardService _rewardService;
    private readonly ILogger<PortalRewardController> _logger;

    public PortalRewardController(ShmsDbContext context, IRewardService rewardService, ILogger<PortalRewardController> logger)
    {
        _context = context;
        _rewardService = rewardService;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    // GET /api/portalrewards/my-points
    [HttpGet("my-points")]
    public async Task<IActionResult> GetMyPoints()
    {
        var tenantId = GetUserId();
        var tenant = await _context.Tenants
            .Include(t => t.House)
                .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        var settings = await _context.RewardSettings.FirstOrDefaultAsync();
        var canRedeem = settings != null && settings.IsGlobalEnabled
            && tenant.House?.Flat != null && tenant.House.Flat.RewardEnabled;

        // Tenant self-service view: scoped to the CURRENT tenancy cycle only, so a tenant who has been
        // revived across multiple delete/re-register cycles doesn't see points/history from a prior
        // cycle mixed into their current balance's history. This filter is deliberately NOT applied to
        // RewardReportBuilder or RewardController.GetTransactions (the admin audit trail) — those must
        // keep showing full cross-cycle history. Do not copy this filter over there.
        var history = await _context.RewardTransactions
            .Where(t => t.TenantId == tenantId && t.TenancyCycle == tenant.TenancyCycle)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                t.TransactionType,
                t.Source,
                t.Points,
                t.AmountPaidOrRedeemed,
                t.BalanceAfter,
                t.RedemptionReference,
                t.CreatedAt
            })
            .ToListAsync();

        return Ok(new
        {
            success = true,
            pointsBalance = tenant.PointsBalance,
            canRedeem,
            redemptionRate = settings?.RedemptionRate ?? 0,
            history
        });
    }

    // POST /api/portalrewards/redeem
    [HttpPost("redeem")]
    public async Task<IActionResult> Redeem([FromBody] RedeemPointsDto dto)
    {
        var tenantId = GetUserId();
        var result = await _rewardService.RedeemPointsAsync(tenantId, dto.Points);

        return Ok(new
        {
            result.Success,
            result.Message,
            result.RedemptionReference,
            result.KesAmount,
            itemized = result.Itemized?.Select(i => new { month = i.month, year = i.year, applied = i.applied })
        });
    }
}

public class RedeemPointsDto
{
    public decimal Points { get; set; }
}
