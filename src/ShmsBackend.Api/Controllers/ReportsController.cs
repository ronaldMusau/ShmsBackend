using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Api.Services;
using ShmsBackend.Api.Services.Reports;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

// Routing approach: one action pair (preview + export) PER report type — api/reports/tenants/preview,
// api/reports/tenants/export — rather than a single api/reports/{reportType}/preview|export with an
// internal switch. Chosen because each report type has its own filter shape (TenantFilters here; a
// PaymentsFilters, VacateFilters, etc. for the other 14) and its own *ReportBuilder mapping business
// columns — a shared route parameter would still need type-specific query-param binding and a big
// switch/dictionary of builders per action, which is no simpler than one action per type and hides
// each report's actual parameter contract from anyone reading the routes/Swagger doc. The part that
// IS shared and must not be duplicated per type is already factored out: TenantQueryService/
// PaymentQueryService (query building) and IReportRenderer (PDF/Excel/Word rendering) are both reused
// as-is by every future *ReportBuilder — only the builder + controller action pair is new per report type.
//
// Landlord-scoped variants (api/reports/landlord/...) establish the pattern the remaining landlord-
// scoped report types (Flats, Complaints, Vacate) should follow later: resolve landlordId from
// ClaimTypes.NameIdentifier server-side exactly like PortalPaymentController.GetLandlordPayments does,
// force it into the filters object, and never bind a landlordId from the query string — the admin-wide
// preview/export actions for the same report type simply never populate that field.
//
// Authorization note: this controller mixes admin-only and Landlord-only actions, so authorization is
// applied per-action rather than once at the class level — stacking a class-level [Authorize(Roles=...)]
// with a narrower per-action one would require BOTH to pass (AND, not OR), which would lock landlords
// out of their own endpoints entirely.
[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly TenantReportBuilder _tenantReportBuilder;
    private readonly PaymentReportBuilder _paymentReportBuilder;
    private readonly HouseReportBuilder _houseReportBuilder;
    private readonly VacateReportBuilder _vacateReportBuilder;
    private readonly OverdueReportBuilder _overdueReportBuilder;
    private readonly ComplaintReportBuilder _complaintReportBuilder;
    private readonly IReportRenderer _reportRenderer;
    private readonly ShmsDbContext _context;

    public ReportsController(
        TenantReportBuilder tenantReportBuilder,
        PaymentReportBuilder paymentReportBuilder,
        HouseReportBuilder houseReportBuilder,
        VacateReportBuilder vacateReportBuilder,
        OverdueReportBuilder overdueReportBuilder,
        ComplaintReportBuilder complaintReportBuilder,
        IReportRenderer reportRenderer,
        ShmsDbContext context)
    {
        _tenantReportBuilder = tenantReportBuilder;
        _paymentReportBuilder = paymentReportBuilder;
        _houseReportBuilder = houseReportBuilder;
        _vacateReportBuilder = vacateReportBuilder;
        _overdueReportBuilder = overdueReportBuilder;
        _complaintReportBuilder = complaintReportBuilder;
        _reportRenderer = reportRenderer;
        _context = context;
    }

    private Guid? GetLandlordId()
    {
        var landlordIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(landlordIdStr, out var landlordId) ? landlordId : null;
    }

    private Guid? GetTenantId()
    {
        var tenantIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(tenantIdStr, out var tenantId) ? tenantId : null;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Tenants — admin-wide
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/reports/tenants/preview
    [HttpGet("tenants/preview")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> PreviewTenantsReport([FromQuery] TenantFilters filters)
    {
        var data = await _tenantReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return Ok(new { success = true, data, company = CompanyInfo(company) });
    }

    // GET /api/reports/tenants/export?format=pdf|excel|word
    [HttpGet("tenants/export")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> ExportTenantsReport([FromQuery] TenantFilters filters, [FromQuery] string format = "pdf")
    {
        var data = await _tenantReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return await ExportAsync(data, company, "Tenants-Report", format);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Payments — admin-wide (no landlordId query param accepted — admin sees all)
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/reports/payments/preview
    [HttpGet("payments/preview")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> PreviewPaymentsReport([FromQuery] PaymentFilters filters)
    {
        var data = await _paymentReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return Ok(new { success = true, data, company = CompanyInfo(company) });
    }

    // GET /api/reports/payments/export?format=pdf|excel|word
    [HttpGet("payments/export")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> ExportPaymentsReport([FromQuery] PaymentFilters filters, [FromQuery] string format = "pdf")
    {
        var data = await _paymentReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return await ExportAsync(data, company, "Payments-Report", format);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Houses — admin-wide (no landlordId query param accepted — admin sees all)
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/reports/houses/preview
    [HttpGet("houses/preview")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> PreviewHousesReport([FromQuery] HouseFilters filters)
    {
        var data = await _houseReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return Ok(new { success = true, data, company = CompanyInfo(company) });
    }

    // GET /api/reports/houses/export?format=pdf|excel|word
    [HttpGet("houses/export")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> ExportHousesReport([FromQuery] HouseFilters filters, [FromQuery] string format = "pdf")
    {
        var data = await _houseReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return await ExportAsync(data, company, "Houses-Report", format);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Landlord-scoped — payments
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/reports/landlord/payments/preview
    [HttpGet("landlord/payments/preview")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> PreviewLandlordPaymentsReport([FromQuery] PaymentFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;

        var data = await _paymentReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return Ok(new { success = true, data, company = CompanyInfo(company) });
    }

    // GET /api/reports/landlord/payments/export?format=pdf|excel|word
    [HttpGet("landlord/payments/export")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> ExportLandlordPaymentsReport([FromQuery] PaymentFilters filters, [FromQuery] string format = "pdf")
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;

        var data = await _paymentReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return await ExportAsync(data, company, "Payments-Report", format);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Tenant-scoped — payments (reuses PaymentReportBuilder as-is — no new builder needed)
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/reports/tenant/payments/preview
    [HttpGet("tenant/payments/preview")]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> PreviewTenantPaymentsReport([FromQuery] PaymentFilters filters)
    {
        var tenantId = GetTenantId();
        if (tenantId == null) return Unauthorized();

        var tenant = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId.Value);
        if (tenant == null) return Unauthorized();

        filters.TenantId = tenantId;
        filters.TenancyCycle = tenant.TenancyCycle;

        var data = await _paymentReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return Ok(new { success = true, data, company = CompanyInfo(company) });
    }

    // GET /api/reports/tenant/payments/export?format=pdf|excel|word
    [HttpGet("tenant/payments/export")]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> ExportTenantPaymentsReport([FromQuery] PaymentFilters filters, [FromQuery] string format = "pdf")
    {
        var tenantId = GetTenantId();
        if (tenantId == null) return Unauthorized();

        var tenant = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId.Value);
        if (tenant == null) return Unauthorized();

        filters.TenantId = tenantId;
        filters.TenancyCycle = tenant.TenancyCycle;

        var data = await _paymentReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return await ExportAsync(data, company, "Payments-Report", format);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Landlord-scoped — tenants
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/reports/landlord/tenants/preview
    [HttpGet("landlord/tenants/preview")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> PreviewLandlordTenantsReport([FromQuery] TenantFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;

        var data = await _tenantReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return Ok(new { success = true, data, company = CompanyInfo(company) });
    }

    // GET /api/reports/landlord/tenants/export?format=pdf|excel|word
    [HttpGet("landlord/tenants/export")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> ExportLandlordTenantsReport([FromQuery] TenantFilters filters, [FromQuery] string format = "pdf")
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;

        var data = await _tenantReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return await ExportAsync(data, company, "Tenants-Report", format);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Landlord-scoped — houses
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/reports/landlord/houses/preview
    [HttpGet("landlord/houses/preview")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> PreviewLandlordHousesReport([FromQuery] HouseFilters filters)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;

        var data = await _houseReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return Ok(new { success = true, data, company = CompanyInfo(company) });
    }

    // GET /api/reports/landlord/houses/export?format=pdf|excel|word
    [HttpGet("landlord/houses/export")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> ExportLandlordHousesReport([FromQuery] HouseFilters filters, [FromQuery] string format = "pdf")
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;

        var data = await _houseReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return await ExportAsync(data, company, "Houses-Report", format);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Vacate Requests — admin-wide
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/reports/vacate/preview
    [HttpGet("vacate/preview")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> PreviewVacateReport([FromQuery] VacateFilters filters)
    {
        var data = await _vacateReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return Ok(new { success = true, data, company = CompanyInfo(company) });
    }

    // GET /api/reports/vacate/export?format=pdf|excel|word
    [HttpGet("vacate/export")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> ExportVacateReport([FromQuery] VacateFilters filters, [FromQuery] string format = "pdf")
    {
        var data = await _vacateReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return await ExportAsync(data, company, "Vacate-Report", format);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Overdue Tenants — admin-wide
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/reports/overdue/preview
    [HttpGet("overdue/preview")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> PreviewOverdueReport([FromQuery] OverdueFilters filters)
    {
        var data = await _overdueReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return Ok(new { success = true, data, company = CompanyInfo(company) });
    }

    // GET /api/reports/overdue/export?format=pdf|excel|word
    [HttpGet("overdue/export")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> ExportOverdueReport([FromQuery] OverdueFilters filters, [FromQuery] string format = "pdf")
    {
        var data = await _overdueReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return await ExportAsync(data, company, "Overdue-Report", format);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Complaints — admin-wide
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/reports/complaints/preview
    [HttpGet("complaints/preview")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> PreviewComplaintsReport([FromQuery] ComplaintFilters filters)
    {
        var data = await _complaintReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return Ok(new { success = true, data, company = CompanyInfo(company) });
    }

    // GET /api/reports/complaints/export?format=pdf|excel|word
    [HttpGet("complaints/export")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> ExportComplaintsReport([FromQuery] ComplaintFilters filters, [FromQuery] string format = "pdf")
    {
        var data = await _complaintReportBuilder.BuildAsync(filters);
        var company = await GetOrCreateCompanySettingsAsync();
        return await ExportAsync(data, company, "Complaints-Report", format);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Distinct payment years (for report filter dropdowns)
    // ═══════════════════════════════════════════════════════════════════

    // GET /api/reports/years
    [HttpGet("years")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetPaymentYears()
    {
        var years = await _context.Payments
            .Where(p => !p.IsDeleted)
            .Select(p => p.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();

        if (years.Count == 0)
            years.Add(DateTime.UtcNow.Year);

        return Ok(new { success = true, data = years });
    }

    // GET /api/reports/landlord/years
    [HttpGet("landlord/years")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordPaymentYears()
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();

        var years = await _context.Payments
            .Where(p => !p.IsDeleted && p.LandlordId == landlordId)
            .Select(p => p.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();

        if (years.Count == 0)
            years.Add(DateTime.UtcNow.Year);

        return Ok(new { success = true, data = years });
    }

    // GET /api/reports/tenant/years
    [HttpGet("tenant/years")]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> GetTenantPaymentYears()
    {
        var tenantId = GetTenantId();
        if (tenantId == null) return Unauthorized();

        var years = await _context.Payments
            .Where(p => !p.IsDeleted && p.TenantId == tenantId)
            .Select(p => p.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();

        if (years.Count == 0)
            years.Add(DateTime.UtcNow.Year);

        return Ok(new { success = true, data = years });
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<IActionResult> ExportAsync(ReportData data, CompanySettings company, string fileStem, string format)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd");
        return format.ToLowerInvariant() switch
        {
            "pdf" => File(await _reportRenderer.RenderPdfAsync(data, company), "application/pdf", $"{fileStem}-{stamp}.pdf"),
            "excel" => File(await _reportRenderer.RenderExcelAsync(data, company), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{fileStem}-{stamp}.xlsx"),
            "word" => File(await _reportRenderer.RenderWordAsync(data, company), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{fileStem}-{stamp}.docx"),
            _ => BadRequest(new { success = false, message = "Unsupported format. Use pdf, excel, or word." })
        };
    }

    private static object CompanyInfo(CompanySettings company) => new
    {
        companyName = company.CompanyName,
        address = company.Address,
        email = company.Email,
        phone = company.Phone,
        website = company.Website,
        registrationNumber = company.RegistrationNumber
    };

    private async Task<CompanySettings> GetOrCreateCompanySettingsAsync()
    {
        var settings = await _context.CompanySettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new CompanySettings { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
            _context.CompanySettings.Add(settings);
            await _context.SaveChangesAsync();
        }
        return settings;
    }
}
