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
    private readonly ExpenseAnalyticsService _expenseAnalyticsService;
    private readonly SessionAnalyticsService _sessionAnalyticsService;
    private readonly RewardAnalyticsService _rewardAnalyticsService;
    private readonly ForfeitedAdvanceAnalyticsService _forfeitedAdvanceAnalyticsService;
    private readonly AgreementAnalyticsService _agreementAnalyticsService;

    public AnalyticsController(
        ComplaintAnalyticsService complaintAnalyticsService,
        FinancialStandingService financialStandingService,
        RefundAnalyticsService refundAnalyticsService,
        DeductionAnalyticsService deductionAnalyticsService,
        ServiceChargeAnalyticsService serviceChargeAnalyticsService,
        ExpenseAnalyticsService expenseAnalyticsService,
        SessionAnalyticsService sessionAnalyticsService,
        RewardAnalyticsService rewardAnalyticsService,
        ForfeitedAdvanceAnalyticsService forfeitedAdvanceAnalyticsService,
        AgreementAnalyticsService agreementAnalyticsService)
    {
        _complaintAnalyticsService = complaintAnalyticsService;
        _financialStandingService = financialStandingService;
        _refundAnalyticsService = refundAnalyticsService;
        _deductionAnalyticsService = deductionAnalyticsService;
        _serviceChargeAnalyticsService = serviceChargeAnalyticsService;
        _expenseAnalyticsService = expenseAnalyticsService;
        _sessionAnalyticsService = sessionAnalyticsService;
        _rewardAnalyticsService = rewardAnalyticsService;
        _forfeitedAdvanceAnalyticsService = forfeitedAdvanceAnalyticsService;
        _agreementAnalyticsService = agreementAnalyticsService;
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

    private static void ApplyDefaultDateRange(ExpenseFilters filters)
    {
        if (!filters.FromDate.HasValue && !filters.ToDate.HasValue)
        {
            var now = DateTime.UtcNow;
            filters.FromDate = new DateTime(now.Year, now.Month, 1);
            filters.ToDate = now;
        }
    }

    private static void ApplyDefaultDateRange(SessionFilters filters)
    {
        if (!filters.FromDate.HasValue && !filters.ToDate.HasValue)
        {
            var now = DateTime.UtcNow;
            filters.FromDate = new DateTime(now.Year, now.Month, 1);
            filters.ToDate = now;
        }
    }

    private static void ApplyDefaultDateRange(RewardTransactionFilters filters)
    {
        if (!filters.FromDate.HasValue && !filters.ToDate.HasValue)
        {
            var now = DateTime.UtcNow;
            filters.FromDate = new DateTime(now.Year, now.Month, 1);
            filters.ToDate = now;
        }
    }

    private static void ApplyDefaultDateRange(ForfeitedAdvanceFilters filters)
    {
        if (!filters.FromDate.HasValue && !filters.ToDate.HasValue)
        {
            var now = DateTime.UtcNow;
            filters.FromDate = new DateTime(now.Year, now.Month, 1);
            filters.ToDate = now;
        }
    }

    private static (DateTime FromDate, DateTime ToDate) ApplyDefaultDateRange(DateTime? fromDate, DateTime? toDate)
    {
        if (fromDate.HasValue || toDate.HasValue)
            return (fromDate ?? toDate!.Value, toDate ?? fromDate!.Value);

        var now = DateTime.UtcNow;
        return (new DateTime(now.Year, now.Month, 1), now);
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

    // ═══════════════════════════════════════════════════════════════════
    // Expenses — admin-wide (roles match ReportsController's expenses/preview,export actions exactly)
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/expenses/by-flat
    [HttpGet("expenses/by-flat")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetExpensesByFlat([FromQuery] ExpenseFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _expenseAnalyticsService.GetByFlatAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/expenses/trend
    [HttpGet("expenses/trend")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetExpensesTrend([FromQuery] ExpenseFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _expenseAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Expenses — landlord-scoped
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/landlord/expenses/by-flat
    [HttpGet("landlord/expenses/by-flat")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordExpensesByFlat([FromQuery] ExpenseFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _expenseAnalyticsService.GetByFlatAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/landlord/expenses/trend
    [HttpGet("landlord/expenses/trend")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordExpensesTrend([FromQuery] ExpenseFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _expenseAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Bookings (Sessions) — admin-wide (roles match ReportsController's sessions/preview,export
    // actions exactly — note: no Accountant here, unlike Refunds/Deductions/Rewards/Service Charges)
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/bookings/breakdown
    [HttpGet("bookings/breakdown")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetBookingsBreakdown([FromQuery] SessionFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _sessionAnalyticsService.GetStatusBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/bookings/trend
    [HttpGet("bookings/trend")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetBookingsTrend([FromQuery] SessionFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _sessionAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Bookings (Sessions) — landlord-scoped
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/landlord/bookings/breakdown
    [HttpGet("landlord/bookings/breakdown")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordBookingsBreakdown([FromQuery] SessionFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _sessionAnalyticsService.GetStatusBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/landlord/bookings/trend
    [HttpGet("landlord/bookings/trend")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordBookingsTrend([FromQuery] SessionFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _sessionAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Rewards — admin-wide (roles match ReportsController's rewards/preview,export actions exactly)
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/rewards/by-type
    [HttpGet("rewards/by-type")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetRewardsByType([FromQuery] RewardTransactionFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _rewardAnalyticsService.GetTypeBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/rewards/trend
    [HttpGet("rewards/trend")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetRewardsTrend([FromQuery] RewardTransactionFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _rewardAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Rewards — landlord-scoped
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/landlord/rewards/by-type
    [HttpGet("landlord/rewards/by-type")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordRewardsByType([FromQuery] RewardTransactionFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _rewardAnalyticsService.GetTypeBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/landlord/rewards/trend
    [HttpGet("landlord/rewards/trend")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordRewardsTrend([FromQuery] RewardTransactionFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _rewardAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Forfeited Advances — admin-wide (roles match ReportsController's forfeited-advance/preview,export
    // actions exactly)
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/forfeited-advances/breakdown
    [HttpGet("forfeited-advances/breakdown")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetForfeitedAdvancesBreakdown([FromQuery] ForfeitedAdvanceFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _forfeitedAdvanceAnalyticsService.GetBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/forfeited-advances/trend
    [HttpGet("forfeited-advances/trend")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetForfeitedAdvancesTrend([FromQuery] ForfeitedAdvanceFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _forfeitedAdvanceAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Forfeited Advances — landlord-scoped
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/landlord/forfeited-advances/breakdown
    [HttpGet("landlord/forfeited-advances/breakdown")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordForfeitedAdvancesBreakdown([FromQuery] ForfeitedAdvanceFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _forfeitedAdvanceAnalyticsService.GetBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/landlord/forfeited-advances/trend
    [HttpGet("landlord/forfeited-advances/trend")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordForfeitedAdvancesTrend([FromQuery] ForfeitedAdvanceFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _forfeitedAdvanceAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Agreements — Management-only (roles match AgreementController's exact class-level restriction:
    // SuperAdmin,Admin ONLY, no Secretary/Manager/Accountant). No landlord-scoped version — confirmed
    // no landlord-facing precedent for agreement status data exists anywhere in the codebase.
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/agreements/status-breakdown
    [HttpGet("agreements/status-breakdown")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> GetAgreementsStatusBreakdown([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        var (from, to) = ApplyDefaultDateRange(fromDate, toDate);
        var data = await _agreementAnalyticsService.GetStatusBreakdownAsync(from, to);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/agreements/role-breakdown
    [HttpGet("agreements/role-breakdown")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> GetAgreementsRoleBreakdown([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        var (from, to) = ApplyDefaultDateRange(fromDate, toDate);
        var data = await _agreementAnalyticsService.GetRoleBreakdownAsync(from, to);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/agreements/trend
    [HttpGet("agreements/trend")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> GetAgreementsTrend([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        var (from, to) = ApplyDefaultDateRange(fromDate, toDate);
        var data = await _agreementAnalyticsService.GetTrendAsync(from, to);
        return Ok(new { success = true, data });
    }
}
