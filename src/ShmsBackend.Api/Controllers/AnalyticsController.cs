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
    private readonly RefundAnalyticsService _refundAnalyticsService;
    private readonly DeductionAnalyticsService _deductionAnalyticsService;
    private readonly ServiceChargeAnalyticsService _serviceChargeAnalyticsService;

    public AnalyticsController(
        ComplaintAnalyticsService complaintAnalyticsService,
        FinancialStandingService financialStandingService,
        RefundAnalyticsService refundAnalyticsService,
        DeductionAnalyticsService deductionAnalyticsService,
        ServiceChargeAnalyticsService serviceChargeAnalyticsService)
    {
        _complaintAnalyticsService = complaintAnalyticsService;
        _financialStandingService = financialStandingService;
        _refundAnalyticsService = refundAnalyticsService;
        _deductionAnalyticsService = deductionAnalyticsService;
        _serviceChargeAnalyticsService = serviceChargeAnalyticsService;
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

    private static void ApplyDefaultDateRange(RefundFilters filters)
    {
        if (!filters.FromDate.HasValue && !filters.ToDate.HasValue)
        {
            var now = DateTime.UtcNow;
            filters.FromDate = new DateTime(now.Year, now.Month, 1);
            filters.ToDate = now;
        }
    }

    private static void ApplyDefaultDateRange(DeductionFilters filters)
    {
        if (!filters.FromDate.HasValue && !filters.ToDate.HasValue)
        {
            var now = DateTime.UtcNow;
            filters.FromDate = new DateTime(now.Year, now.Month, 1);
            filters.ToDate = now;
        }
    }

    private static void ApplyDefaultDateRange(PaymentFilters filters)
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

    // ═══════════════════════════════════════════════════════════════════
    // Refunds — admin-wide (roles match ReportsController's refunds/preview,export actions exactly)
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/refunds/breakdown
    [HttpGet("refunds/breakdown")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetRefundsBreakdown([FromQuery] RefundFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _refundAnalyticsService.GetStatusBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/refunds/trend
    [HttpGet("refunds/trend")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetRefundsTrend([FromQuery] RefundFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _refundAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Refunds — landlord-scoped
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/landlord/refunds/breakdown
    [HttpGet("landlord/refunds/breakdown")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordRefundsBreakdown([FromQuery] RefundFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _refundAnalyticsService.GetStatusBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/landlord/refunds/trend
    [HttpGet("landlord/refunds/trend")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordRefundsTrend([FromQuery] RefundFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _refundAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Deductions — admin-wide (roles match ReportsController's deductions/preview,export actions exactly)
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/deductions/by-category
    [HttpGet("deductions/by-category")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetDeductionsByCategory([FromQuery] DeductionFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _deductionAnalyticsService.GetByCategoryAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/deductions/trend
    [HttpGet("deductions/trend")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetDeductionsTrend([FromQuery] DeductionFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _deductionAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Deductions — landlord-scoped
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/landlord/deductions/by-category
    [HttpGet("landlord/deductions/by-category")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordDeductionsByCategory([FromQuery] DeductionFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _deductionAnalyticsService.GetByCategoryAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/landlord/deductions/trend
    [HttpGet("landlord/deductions/trend")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordDeductionsTrend([FromQuery] DeductionFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _deductionAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Service Charges — admin-wide ONLY. Service charge is Management's own platform fee, not the
    // landlord's money passing through — confirmed no landlord-facing "service charges collected" view
    // exists anywhere in the codebase today (only the admin-wide PaymentController.GetAllServiceChargesCollected,
    // restricted to SuperAdmin,Admin,Secretary,Manager,Accountant). No landlord-scoped endpoint added here,
    // matching that existing access pattern rather than inventing a new one.
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/service-charges/breakdown
    [HttpGet("service-charges/breakdown")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetServiceChargesBreakdown([FromQuery] PaymentFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _serviceChargeAnalyticsService.GetStatusBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/service-charges/trend
    [HttpGet("service-charges/trend")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetServiceChargesTrend([FromQuery] PaymentFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _serviceChargeAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }
}
