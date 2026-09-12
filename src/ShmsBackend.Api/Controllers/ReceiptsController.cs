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

        var data = BuildReceiptData(payment);
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

        var data = BuildReceiptData(payment);
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

    private bool IsAuthorized(Payment payment)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
            return false;

        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

        return role switch
        {
            "Tenant" => payment.TenantId == userId,
            "Landlord" => payment.House?.Flat?.LandlordId == userId,
            "SuperAdmin" or "Admin" or "Secretary" or "Manager" or "Accountant" => true,
            _ => false
        };
    }

    private static ReceiptData BuildReceiptData(Payment payment)
    {
        var receiptNumber = !string.IsNullOrWhiteSpace(payment.MpesaReceiptNumber)
            ? payment.MpesaReceiptNumber!
            : $"PMT-{payment.Id.ToString()[..8].ToUpperInvariant()}";

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
            ServiceChargeAmount = payment.ServiceChargeAmount,
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
