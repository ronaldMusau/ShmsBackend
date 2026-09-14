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
    private readonly PaymentAnalyticsService _paymentAnalyticsService;
    private readonly OverdueAnalyticsService _overdueAnalyticsService;
    private readonly AgentAnalyticsService _agentAnalyticsService;
    private readonly LandlordAnalyticsService _landlordAnalyticsService;

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
        AgreementAnalyticsService agreementAnalyticsService,
        PaymentAnalyticsService paymentAnalyticsService,
        OverdueAnalyticsService overdueAnalyticsService,
        AgentAnalyticsService agentAnalyticsService,
        LandlordAnalyticsService landlordAnalyticsService)
    {
        _complaintAnalyticsService = complaintAnalyticsService;
        _financialStandingService = financialStandingService;
        _refundAnalyticsService = refundAnalyticsService;
        _deductionAnalyticsService = deductionAnalyticsService;
        _serviceChargeAnalyticsService = serviceChargeAnalyticsService;
        _expenseAnalyticsService = expenseAnalyticsService;
        _sessionAnalyticsService = sessionAnalyticsService;
        _rewardAnalyticsService = rewardAnalyticsService;
        _paymentAnalyticsService = paymentAnalyticsService;
        _overdueAnalyticsService = overdueAnalyticsService;
        _agentAnalyticsService = agentAnalyticsService;
        _landlordAnalyticsService = landlordAnalyticsService;
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
    // Bookings (Sessions) — admin-wide. Deliberately SuperAdmin,Admin,Accountant — Analytics' own
    // access boundary, NOT SessionController's broader Secretary/Manager-inclusive role set (that
    // controller does record management, not analytics viewing, so its role set doesn't apply here).
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/bookings/breakdown
    [HttpGet("bookings/breakdown")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetBookingsBreakdown([FromQuery] SessionFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _sessionAnalyticsService.GetStatusBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/bookings/trend
    [HttpGet("bookings/trend")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetBookingsTrend([FromQuery] SessionFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _sessionAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // Bookings has no landlord-scoped endpoint by design — session/viewing activity is an
    // operational, Agent-driven concern Management runs, not something a Landlord has a direct
    // stake in or currently sees anywhere else in the portal.

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

    // Rewards has no landlord-scoped endpoint by design — reward points are a cost Management funds
    // and controls (earn rates, redemption rates, global enable/disable), not a Landlord-facing figure.

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

    // Forfeited Advances has no landlord-scoped endpoint by design — these are funds Management
    // holds and forfeits (applied to damages or retained unused), not money the Landlord ever
    // directly touches or has a claim on.

    // ═══════════════════════════════════════════════════════════════════
    // Agreements — Management-only. Deliberately SuperAdmin,Admin,Accountant — Analytics' own access
    // boundary, NOT AgreementController's narrower class-level SuperAdmin,Admin restriction (that
    // controller does record management — template uploads, verify/reject — not analytics viewing,
    // so its role set doesn't apply here). No landlord-scoped version — confirmed no landlord-facing
    // precedent for agreement status data exists anywhere in the codebase.
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/agreements/status-breakdown
    [HttpGet("agreements/status-breakdown")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetAgreementsStatusBreakdown([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        var (from, to) = ApplyDefaultDateRange(fromDate, toDate);
        var data = await _agreementAnalyticsService.GetStatusBreakdownAsync(from, to);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/agreements/role-breakdown
    [HttpGet("agreements/role-breakdown")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetAgreementsRoleBreakdown([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        var (from, to) = ApplyDefaultDateRange(fromDate, toDate);
        var data = await _agreementAnalyticsService.GetRoleBreakdownAsync(from, to);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/agreements/trend
    [HttpGet("agreements/trend")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetAgreementsTrend([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        var (from, to) = ApplyDefaultDateRange(fromDate, toDate);
        var data = await _agreementAnalyticsService.GetTrendAsync(from, to);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Rent Payments — admin-wide (matches Complaints/Refunds/Deductions/Expenses precedent)
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/payments/breakdown
    [HttpGet("payments/breakdown")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetPaymentsBreakdown([FromQuery] PaymentFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _paymentAnalyticsService.GetStatusBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/payments/trend
    [HttpGet("payments/trend")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetPaymentsTrend([FromQuery] PaymentFilters filters)
    {
        filters.LandlordId = null;
        ApplyDefaultDateRange(filters);
        var data = await _paymentAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Rent Payments — landlord-scoped
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/landlord/payments/breakdown
    [HttpGet("landlord/payments/breakdown")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordPaymentsBreakdown([FromQuery] PaymentFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _paymentAnalyticsService.GetStatusBreakdownAsync(filters);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/landlord/payments/trend
    [HttpGet("landlord/payments/trend")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordPaymentsTrend([FromQuery] PaymentFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;
        ApplyDefaultDateRange(filters);
        var data = await _paymentAnalyticsService.GetTrendAsync(filters);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Overdue — admin-wide. No dedicated entity/filters class (mirrors OverdueReportBuilder's own
    // shape) — plain query params instead of a bound filters object.
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/overdue/breakdown
    [HttpGet("overdue/breakdown")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetOverdueBreakdown([FromQuery] Guid? flatId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        var (from, to) = ApplyDefaultDateRange(fromDate, toDate);
        var data = await _overdueAnalyticsService.GetWarningStageBreakdownAsync(flatId, null, from, to);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/overdue/trend
    [HttpGet("overdue/trend")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetOverdueTrend([FromQuery] Guid? flatId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        var (from, to) = ApplyDefaultDateRange(fromDate, toDate);
        var data = await _overdueAnalyticsService.GetTrendAsync(flatId, null, from, to);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Overdue — landlord-scoped
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/landlord/overdue/breakdown
    [HttpGet("landlord/overdue/breakdown")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordOverdueBreakdown([FromQuery] Guid? flatId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        var (from, to) = ApplyDefaultDateRange(fromDate, toDate);
        var data = await _overdueAnalyticsService.GetWarningStageBreakdownAsync(flatId, landlordId, from, to);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/landlord/overdue/trend
    [HttpGet("landlord/overdue/trend")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordOverdueTrend([FromQuery] Guid? flatId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        var (from, to) = ApplyDefaultDateRange(fromDate, toDate);
        var data = await _overdueAnalyticsService.GetTrendAsync(flatId, landlordId, from, to);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Agent performance — admin-wide ONLY (confirmed no landlord-facing precedent exists anywhere)
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/agents/performance?agentId=... (optional — omit for portfolio-wide)
    [HttpGet("agents/performance")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetAgentPerformance([FromQuery] Guid? agentId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        var (from, to) = ApplyDefaultDateRange(fromDate, toDate);
        var data = await _agentAnalyticsService.GetPerformanceAsync(agentId, from, to);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/agents/sessions-trend?agentId=... (optional — omit for portfolio-wide)
    [HttpGet("agents/sessions-trend")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetAgentSessionsTrend([FromQuery] Guid? agentId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        var (from, to) = ApplyDefaultDateRange(fromDate, toDate);
        var data = await _agentAnalyticsService.GetSessionsTrendAsync(agentId, from, to);
        return Ok(new { success = true, data });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Landlord portfolio — admin-wide ONLY (confirmed no landlord-facing precedent exists anywhere —
    // this is Management viewing a landlord's portfolio, not the landlord viewing their own)
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/analytics/landlords/portfolio?landlordId=... (optional — omit for portfolio-wide)
    [HttpGet("landlords/portfolio")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetLandlordPortfolio([FromQuery] Guid? landlordId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        var (from, to) = ApplyDefaultDateRange(fromDate, toDate);
        var data = await _landlordAnalyticsService.GetPortfolioAsync(landlordId, from, to);
        return Ok(new { success = true, data });
    }

    // GET /api/analytics/landlords/collection-trend?landlordId=... (optional — omit for portfolio-wide)
    [HttpGet("landlords/collection-trend")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetLandlordCollectionTrend([FromQuery] Guid? landlordId, [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate)
    {
        var (from, to) = ApplyDefaultDateRange(fromDate, toDate);
        var data = await _landlordAnalyticsService.GetCollectionTrendAsync(landlordId, from, to);
        return Ok(new { success = true, data });
    }
}
