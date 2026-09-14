using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShmsBackend.Api.Services;
using ShmsBackend.Api.Services.Analytics;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly ComplaintAnalyticsService _complaintAnalyticsService;
    private readonly FinancialStandingService _financialStandingService;

    public AnalyticsController(ComplaintAnalyticsService complaintAnalyticsService, FinancialStandingService financialStandingService)
    {
        _complaintAnalyticsService = complaintAnalyticsService;
        _financialStandingService = financialStandingService;
    }

    private Guid? GetLandlordId()
    {
        var landlordIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(landlordIdStr, out var landlordId) ? landlordId : null;
    }

    // Never run these queries with a genuinely unbounded date range by accident — default to the
    // current month when the caller supplied neither bound.
    private static void ApplyDefaultDateRange(ComplaintFilters filters)
    {
        if (!filters.FromDate.HasValue && !filters.ToDate.HasValue)
        {
            var now = DateTime.UtcNow;
            filters.FromDate = new DateTime(now.Year, now.Month, 1);
            filters.ToDate = now;
        }
    }

    private static void ApplyDefaultDateRange(FinancialStandingFilters filters)
    {
        if (!filters.FromDate.HasValue && !filters.ToDate.HasValue)
        {
            var now = DateTime.UtcNow;
            filters.FromDate = new DateTime(now.Year, now.Month, 1);
            filters.ToDate = now;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Complaints — admin-wide
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/complaints/breakdown
    [HttpGet("complaints/breakdown")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetComplaintsBreakdown([FromQuery] ComplaintFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _complaintAnalyticsService.GetStatusBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/complaints/by-type
    [HttpGet("complaints/by-type")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetComplaintsByType([FromQuery] ComplaintFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _complaintAnalyticsService.GetTypeBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/complaints/trend
    [HttpGet("complaints/trend")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetComplaintsTrend([FromQuery] ComplaintFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _complaintAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Complaints — landlord-scoped
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/landlord/complaints/breakdown
    [HttpGet("landlord/complaints/breakdown")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordComplaintsBreakdown([FromQuery] ComplaintFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _complaintAnalyticsService.GetStatusBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/landlord/complaints/by-type
    [HttpGet("landlord/complaints/by-type")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordComplaintsByType([FromQuery] ComplaintFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _complaintAnalyticsService.GetTypeBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/landlord/complaints/trend
    [HttpGet("landlord/complaints/trend")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordComplaintsTrend([FromQuery] ComplaintFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _complaintAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Financial Standing
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/financial-standing — admin-wide (LandlordId never bound; null = whole portfolio)
    [HttpGet("financial-standing")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetFinancialStanding([FromQuery] FinancialStandingFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _financialStandingService.GetFinancialStandingAsync(filters, includeManagementOnlyFigures: true);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/landlord/financial-standing
    [HttpGet("landlord/financial-standing")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordFinancialStanding([FromQuery] FinancialStandingFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _financialStandingService.GetFinancialStandingAsync(filters, includeManagementOnlyFigures: false);
        return Ok(new { success = true, data });
    }
}
