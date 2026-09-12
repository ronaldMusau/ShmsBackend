using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/rewards")]
public class RewardController : ControllerBase
{
    private readonly ShmsDbContext _context;

    public RewardController(ShmsDbContext context)
    {
        _context = context;
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    // GET /api/rewards/settings
    [HttpGet("settings")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await _context.RewardSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new RewardSettings
            {
                Id = Guid.NewGuid(),
                IsGlobalEnabled = false,
                InitialPaymentEarnRate = 0,
                RegularPaymentEarnRate = 0,
                RedemptionRate = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.RewardSettings.Add(settings);
            await _context.SaveChangesAsync();
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                settings.Id,
                settings.IsGlobalEnabled,
                settings.InitialPaymentEarnRate,
                settings.RegularPaymentEarnRate,
                settings.RedemptionRate,
                settings.UpdatedByUserId,
                settings.CreatedAt,
                settings.UpdatedAt
            }
        });
    }

    // PUT /api/rewards/settings
    [HttpPut("settings")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateRewardSettingsDto dto)
    {
        var settings = await _context.RewardSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new RewardSettings { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
            _context.RewardSettings.Add(settings);
        }

        settings.IsGlobalEnabled = dto.IsGlobalEnabled;
        settings.InitialPaymentEarnRate = dto.InitialPaymentEarnRate;
        settings.RegularPaymentEarnRate = dto.RegularPaymentEarnRate;
        settings.RedemptionRate = dto.RedemptionRate;
        settings.UpdatedByUserId = GetUserId();
        settings.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            data = new
            {
                settings.Id,
                settings.IsGlobalEnabled,
                settings.InitialPaymentEarnRate,
                settings.RegularPaymentEarnRate,
                settings.RedemptionRate,
                settings.UpdatedByUserId,
                settings.CreatedAt,
                settings.UpdatedAt
            }
        });
    }

    // GET /api/rewards/transactions
    [HttpGet("transactions")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] Guid? flatId = null,
        [FromQuery] Guid? houseId = null,
        [FromQuery] string? transactionType = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] decimal? minPoints = null,
        [FromQuery] decimal? maxPoints = null,
        [FromQuery] string? search = null)
    {
        var query = _context.RewardTransactions
            .Include(t => t.Tenant)
                .ThenInclude(t => t!.House)
                    .ThenInclude(h => h!.Flat)
            .AsQueryable();

        if (tenantId.HasValue)
            query = query.Where(t => t.TenantId == tenantId.Value);

        if (flatId.HasValue)
            query = query.Where(t => t.Tenant != null && t.Tenant.House != null && t.Tenant.House.FlatId == flatId.Value);

        if (houseId.HasValue)
            query = query.Where(t => t.Tenant != null && t.Tenant.HouseId == houseId.Value);

        if (!string.IsNullOrEmpty(transactionType))
            query = query.Where(t => t.TransactionType == transactionType);

        if (fromDate.HasValue)
            query = query.Where(t => t.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(t => t.CreatedAt < toDate.Value.Date.AddDays(1));

        if (minPoints.HasValue)
            query = query.Where(t => t.Points >= minPoints.Value);

        if (maxPoints.HasValue)
            query = query.Where(t => t.Points <= maxPoints.Value);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(t => t.Tenant != null &&
                (t.Tenant.FirstName.Contains(search) || t.Tenant.LastName.Contains(search)));

        var total = await query.CountAsync();
        var totalPointsEarned = await query.Where(t => t.TransactionType == "Earned").SumAsync(t => t.Points);
        var totalPointsRedeemed = await query.Where(t => t.TransactionType == "Redeemed").SumAsync(t => t.Points);
        var totalKesRedeemed = await query.Where(t => t.TransactionType == "Redeemed").SumAsync(t => t.AmountPaidOrRedeemed ?? 0);

        var paged = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var data = paged.Select(t => new
        {
            t.Id,
            t.TenantId,
            TenantName = t.Tenant != null ? $"{t.Tenant.FirstName} {t.Tenant.LastName}" : null,
            // Best-effort current-house display — the tenant's house may have changed since this
            // transaction was recorded, so this is not a historical snapshot.
            HouseNumber = t.Tenant?.House?.HouseNumber,
            FlatName = t.Tenant?.House?.Flat?.FlatName,
            t.TransactionType,
            t.Source,
            t.Points,
            t.AmountPaidOrRedeemed,
            t.BalanceAfter,
            t.RelatedPaymentId,
            t.RedemptionReference,
            t.CreatedAt
        }).ToList();

        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize),
            totals = new { totalPointsEarned, totalPointsRedeemed, totalKesRedeemed }
        });
    }
}

public class UpdateRewardSettingsDto
{
    public bool IsGlobalEnabled { get; set; }
    public decimal InitialPaymentEarnRate { get; set; }
    public decimal RegularPaymentEarnRate { get; set; }
    public decimal RedemptionRate { get; set; }
}
