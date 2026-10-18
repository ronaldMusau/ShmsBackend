using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Api.Services.Reports;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

// Ownership check mirrors PortalPaymentController.GetMyPaymentApplications exactly: a Tenant may only
// view their own payment's receipt, a Landlord only a receipt for a payment on one of their own
// properties (via House->Flat->LandlordId), and admin-side roles may view any receipt.
[ApiController]
[Route("api/receipts")]
[Authorize]
public class ReceiptsController : ControllerBase
{
    private readonly ShmsDbContext _context;
    private readonly IReceiptRenderer _receiptRenderer;

    public ReceiptsController(ShmsDbContext context, IReceiptRenderer receiptRenderer)
    {
        _context = context;
        _receiptRenderer = receiptRenderer;
    }

    // GET /api/receipts/{paymentId}/preview
    [HttpGet("{paymentId:guid}/preview")]
    public async Task<IActionResult> PreviewReceipt(Guid paymentId)
    {
        var payment = await LoadPaymentAsync(paymentId);
        if (payment == null) return NotFound(new { success = false, message = "Payment not found." });
        if (!IsAuthorized(payment)) return StatusCode(403, new { success = false, message = "You do not have permission to view this receipt." });

        var data = BuildReceiptData(payment, CallerRole());
        var company = await GetOrCreateCompanySettingsAsync();
        return Ok(new { success = true, data, company = CompanyInfo(company) });
    }

    // GET /api/receipts/{paymentId}/export?format=pdf|excel|word
    [HttpGet("{paymentId:guid}/export")]
    public async Task<IActionResult> ExportReceipt(Guid paymentId, [FromQuery] string format = "pdf")
    {
        var payment = await LoadPaymentAsync(paymentId);
        if (payment == null) return NotFound(new { success = false, message = "Payment not found." });
        if (!IsAuthorized(payment)) return StatusCode(403, new { success = false, message = "You do not have permission to view this receipt." });

        var data = BuildReceiptData(payment, CallerRole());
        var company = await GetOrCreateCompanySettingsAsync();

        return format.ToLowerInvariant() switch
        {
            "pdf" => File(await _receiptRenderer.RenderPdfAsync(data, company), "application/pdf", $"Receipt-{data.ReceiptNumber}.pdf"),
            "excel" => File(await _receiptRenderer.RenderExcelAsync(data, company), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Receipt-{data.ReceiptNumber}.xlsx"),
            "word" => File(await _receiptRenderer.RenderWordAsync(data, company), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"Receipt-{data.ReceiptNumber}.docx"),
            _ => BadRequest(new { success = false, message = "Unsupported format. Use pdf, excel, or word." })
        };
    }

    // ── Complaint charge receipt ─────────────────────────────────────────────
    // GET /api/receipts/complaint/{complaintId}/preview | /export?format=pdf|excel|word
    // Tenant: own complaint only. Admin-side roles: any. The service charge line is admin-only.
    [HttpGet("complaint/{complaintId:guid}/preview")]
    public async Task<IActionResult> PreviewComplaintReceipt(Guid complaintId)
    {
        var (data, error) = await BuildComplaintReceiptAsync(complaintId);
        if (error != null) return error;
        var company = await GetOrCreateCompanySettingsAsync();
        return Ok(new { success = true, data, company = CompanyInfo(company) });
    }

    [HttpGet("complaint/{complaintId:guid}/export")]
    public async Task<IActionResult> ExportComplaintReceipt(Guid complaintId, [FromQuery] string format = "pdf")
    {
        var (data, error) = await BuildComplaintReceiptAsync(complaintId);
        if (error != null) return error;
        return await RenderReceiptAsync(data!, format);
    }

    // ── Vacate settlement receipt ────────────────────────────────────────────
    // GET /api/receipts/vacate-settlement/{settlementId}/preview | /export?format=pdf|excel|word
    [HttpGet("vacate-settlement/{settlementId:guid}/preview")]
    public async Task<IActionResult> PreviewVacateSettlementReceipt(Guid settlementId)
    {
        var (data, error) = await BuildVacateReceiptAsync(settlementId);
        if (error != null) return error;
        var company = await GetOrCreateCompanySettingsAsync();
        return Ok(new { success = true, data, company = CompanyInfo(company) });
    }

    [HttpGet("vacate-settlement/{settlementId:guid}/export")]
    public async Task<IActionResult> ExportVacateSettlementReceipt(Guid settlementId, [FromQuery] string format = "pdf")
    {
        var (data, error) = await BuildVacateReceiptAsync(settlementId);
        if (error != null) return error;
        return await RenderReceiptAsync(data!, format);
    }

    private bool IsAdminSide(string role) =>
        role is "SuperAdmin" or "Admin" or "Secretary" or "Manager" or "Accountant";

    private bool CanSeeTenantReceipt(Guid tenantId)
    {
        var role = CallerRole();
        if (IsAdminSide(role)) return true;
        return role == "Tenant"
            && Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) && uid == tenantId;
    }

    private async Task<(ReceiptData? Data, IActionResult? Error)> BuildComplaintReceiptAsync(Guid complaintId)
    {
        var complaint = await _context.Complaints.Include(c => c.ComplaintType)
            .FirstOrDefaultAsync(c => c.Id == complaintId);
        if (complaint == null) return (null, NotFound(new { success = false, message = "Complaint not found." }));
        if (!CanSeeTenantReceipt(complaint.TenantId))
            return (null, StatusCode(403, new { success = false, message = "You do not have permission to view this receipt." }));
        if (complaint.TenantChargePaidAt == null)
            return (null, BadRequest(new { success = false, message = "No payment has been received for this complaint charge." }));

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == complaint.TenantId);
        var house = await _context.Houses.Include(h => h.Flat).FirstOrDefaultAsync(h => h.Id == complaint.HouseId);
        var attempt = await _context.ComplaintCheckoutAttempts
            .Where(a => a.ComplaintId == complaintId && a.AttemptStatus == "Success")
            .OrderByDescending(a => a.ProcessedAt).FirstOrDefaultAsync();

        var amount = attempt?.Amount ?? complaint.BillableAmount ?? 0m;
        var receiptNo = !string.IsNullOrWhiteSpace(complaint.TenantChargeReceipt)
            ? complaint.TenantChargeReceipt! : $"CMP-{complaint.TicketNumber}";

        var data = new ReceiptData
        {
            ReceiptNumber = receiptNo,
            PaidAt = complaint.TenantChargePaidAt,
            TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}".Trim() : "",
            TenantPhone = tenant?.PhoneNumber,
            TenantEmail = tenant?.Email,
            FlatName = house?.Flat?.FlatName ?? "",
            HouseNumber = house?.HouseNumber ?? "",
            PaymentType = "Complaint Charge",
            PeriodLabel = $"Complaint {complaint.TicketNumber}",
            Description = complaint.ComplaintType?.Name,
            Amount = amount,
            AmountPaid = amount,
            Balance = 0m,
            PaymentMethod = "Mpesa",
            MpesaReceiptNumber = complaint.TenantChargeReceipt,
            Status = "Paid",
            ServiceChargeAmount = IsAdminSide(CallerRole()) ? attempt?.ServiceChargeAmount : null
        };
        return (data, null);
    }

    private async Task<(ReceiptData? Data, IActionResult? Error)> BuildVacateReceiptAsync(Guid settlementId)
    {
        var settlement = await _context.VacateSettlements.FirstOrDefaultAsync(s => s.Id == settlementId);
        if (settlement == null) return (null, NotFound(new { success = false, message = "Settlement not found." }));
        if (!CanSeeTenantReceipt(settlement.TenantId))
            return (null, StatusCode(403, new { success = false, message = "You do not have permission to view this receipt." }));
        if (settlement.Direction != "TenantOwes" || settlement.PaidAt == null)
            return (null, BadRequest(new { success = false, message = "No payment has been received for this settlement." }));

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == settlement.TenantId);
        var house = await _context.Houses.Include(h => h.Flat).FirstOrDefaultAsync(h => h.Id == settlement.HouseId);
        var attempt = await _context.VacateCheckoutAttempts
            .Where(a => a.VacateSettlementId == settlementId && a.AttemptStatus == "Success")
            .OrderByDescending(a => a.ProcessedAt).FirstOrDefaultAsync();

        var receiptNo = !string.IsNullOrWhiteSpace(attempt?.MpesaReceiptNumber)
            ? attempt!.MpesaReceiptNumber! : $"VAC-{settlement.Id.ToString()[..8].ToUpperInvariant()}";

        var data = new ReceiptData
        {
            ReceiptNumber = receiptNo,
            PaidAt = settlement.PaidAt,
            TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}".Trim() : "",
            TenantPhone = tenant?.PhoneNumber,
            TenantEmail = tenant?.Email,
            FlatName = house?.Flat?.FlatName ?? "",
            HouseNumber = house?.HouseNumber ?? "",
            PaymentType = "Vacate Settlement",
            PeriodLabel = $"Vacate settlement — {house?.HouseNumber}",
            Description = settlement.Description,
            Amount = settlement.Amount,
            AmountPaid = settlement.Amount,
            Balance = 0m,
            PaymentMethod = "Mpesa",
            MpesaReceiptNumber = attempt?.MpesaReceiptNumber,
            Status = "Paid",
            ServiceChargeAmount = IsAdminSide(CallerRole()) ? attempt?.ServiceChargeAmount : null
        };
        return (data, null);
    }

    private async Task<IActionResult> RenderReceiptAsync(ReceiptData data, string format)
    {
        var company = await GetOrCreateCompanySettingsAsync();
        return format.ToLowerInvariant() switch
        {
            "pdf" => File(await _receiptRenderer.RenderPdfAsync(data, company), "application/pdf", $"Receipt-{data.ReceiptNumber}.pdf"),
            "excel" => File(await _receiptRenderer.RenderExcelAsync(data, company), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Receipt-{data.ReceiptNumber}.xlsx"),
            "word" => File(await _receiptRenderer.RenderWordAsync(data, company), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"Receipt-{data.ReceiptNumber}.docx"),
            _ => BadRequest(new { success = false, message = "Unsupported format. Use pdf, excel, or word." })
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<Payment?> LoadPaymentAsync(Guid paymentId)
    {
        return await _context.Payments
            .Include(p => p.Tenant)
            .Include(p => p.House)
                .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(p => p.Id == paymentId);
    }

    private string CallerRole() => User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

    private bool IsAuthorized(Payment payment)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
            return false;

        var role = CallerRole();

        return role switch
        {
            "Tenant" => payment.TenantId == userId,
            "Landlord" => payment.House?.Flat?.LandlordId == userId,
            "SuperAdmin" or "Admin" or "Secretary" or "Manager" or "Accountant" => true,
            _ => false
        };
    }

    // Service charge is an internal cost breakdown — Tenant/Landlord should only see what they paid,
    // not this line item. Admin-side roles keep seeing it (same set IsAuthorized grants unrestricted
    // access to).
    private static ReceiptData BuildReceiptData(Payment payment, string callerRole)
    {
        var receiptNumber = !string.IsNullOrWhiteSpace(payment.MpesaReceiptNumber)
            ? payment.MpesaReceiptNumber!
            : $"PMT-{payment.Id.ToString()[..8].ToUpperInvariant()}";

        var showServiceCharge = callerRole is "SuperAdmin" or "Admin" or "Secretary" or "Manager" or "Accountant";

        return new ReceiptData
        {
            ReceiptNumber = receiptNumber,
            PaidAt = payment.PaidAt,
            TenantName = payment.Tenant != null ? $"{payment.Tenant.FirstName} {payment.Tenant.LastName}".Trim() : "",
            TenantPhone = payment.Tenant?.PhoneNumber,
            TenantEmail = payment.Tenant?.Email,
            FlatName = payment.House?.Flat?.FlatName ?? "",
            HouseNumber = payment.House?.HouseNumber ?? "",
            PaymentType = payment.PaymentType.ToString(),
            Month = payment.Month,
            Year = payment.Year,
            Amount = payment.Amount,
            RentAmount = payment.RentAmount,
            DepositAmount = payment.DepositAmount,
            ServiceChargeAmount = showServiceCharge ? payment.ServiceChargeAmount : null,
            CreditApplied = payment.CreditApplied,
            AmountPaid = payment.AmountPaid,
            Balance = payment.Balance,
            PaymentMethod = payment.PaymentMethod?.ToString(),
            MpesaReceiptNumber = payment.MpesaReceiptNumber,
            Status = payment.PaymentStatus.ToString()
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
