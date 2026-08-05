using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Models.Enums;
using System.Security.Claims;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/vacate")]
[Authorize]
public class VacateController : ControllerBase
{
    private readonly ShmsDbContext _context;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<VacateController> _logger;

    public VacateController(
        ShmsDbContext context,
        IEmailService emailService,
        INotificationService notificationService,
        ILogger<VacateController> logger)
    {
        _context = context;
        _emailService = emailService;
        _notificationService = notificationService;
        _logger = logger;
    }

    private Guid GetCallerId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    // GET /api/vacate/eligibility/{tenantId}
    [HttpGet("eligibility/{tenantId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Tenant")]
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

        var activeRequest = await _context.VacateRequests.FirstOrDefaultAsync(r =>
            r.TenantId == tenantId && !r.IsDeleted && r.Status != "Closed" && r.Status != "Cancelled");

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
                depositFee = tenant.House.DepositFee,
                activeRequestId = activeRequest?.Id
            }
        });
    }

    // POST /api/vacate/request
    [HttpPost("request")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Agent,Tenant")]
    public async Task<IActionResult> CreateRequest([FromBody] CreateVacateRequestDto dto)
    {
        var callerId = GetCallerId();
        var callerRole = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

        if (callerRole == "Tenant" && callerId != dto.TenantId)
            return Forbid();

        var tenant = await _context.Tenants
            .Include(t => t.House)
                .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(t => t.Id == dto.TenantId && !t.IsDeleted);

        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        if (tenant.House == null || tenant.House.Flat == null)
            return BadRequest(new { success = false, message = "Tenant is not assigned to a house." });

        if (callerRole == "Tenant" && DateTime.UtcNow.Day > tenant.House.Flat.VacateNoticeDeadlineDay)
            return BadRequest(new { success = false, message = "Vacate notice window has closed for this month." });

        var arrears = await _context.Payments
            .Where(p => p.TenantId == dto.TenantId
                && p.TenancyCycle == tenant.TenancyCycle
                && !p.IsDeleted
                && (p.PaymentStatus == PaymentTransactionStatus.Overdue
                    || p.PaymentStatus == PaymentTransactionStatus.Pending
                    || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid))
            .SumAsync(p => p.Balance);

        if (arrears > 0)
        {
            try { await _emailService.SendVacateArrearsBlockEmailAsync(tenant.Email, tenant.FirstName, arrears); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send arrears block email"); }

            try { await _notificationService.SendToUserAsync(dto.TenantId.ToString(), $"You have KES {arrears:N2} in arrears. Please clear this before your vacate request can proceed.", "property"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify tenant of arrears block"); }

            return BadRequest(new
            {
                success = false,
                errorType = "ArrearsBlock",
                arrearsAmount = arrears,
                message = "Outstanding arrears must be cleared before vacating."
            });
        }

        if (callerRole == "Tenant")
        {
            var totalPaid = await _context.Payments
                .Where(p => p.TenantId == dto.TenantId && !p.IsDeleted)
                .SumAsync(p => p.AmountPaid);

            var totalDue = await _context.Payments
                .Where(p => p.TenantId == dto.TenantId
                    && !p.IsDeleted
                    && p.PaymentStatus != PaymentTransactionStatus.Cancelled
                    && p.PaymentStatus != PaymentTransactionStatus.Failed)
                .SumAsync(p => p.Amount);

            var advanceCredit = Math.Max(0, totalPaid - totalDue);
            if (advanceCredit > 0)
                return BadRequest(new
                {
                    success = false,
                    errorType = "AdvanceCreditBlock",
                    advanceCreditAmount = advanceCredit,
                    message = "You have a credit balance — please contact management to arrange vacating; advance payments are not refunded."
                });
        }

        DateTime requestDate;
        try { requestDate = new DateTime(dto.VacateYear, dto.VacateMonth, 1); }
        catch { return BadRequest(new { success = false, message = "Invalid vacate month or year." }); }

        var now = DateTime.UtcNow;
        var minDate = new DateTime(now.Year, now.Month, 1);
        var maxDate = minDate.AddMonths(5);
        if (requestDate < minDate || requestDate > maxDate)
            return BadRequest(new { success = false, message = "Vacate month must be between now and 5 months from today." });

        var existingRequest = await _context.VacateRequests.AnyAsync(r =>
            r.TenantId == dto.TenantId && r.Status != "Closed" && r.Status != "Cancelled" && !r.IsDeleted);
        if (existingRequest)
            return BadRequest(new { success = false, message = "A vacate request already exists for this tenancy." });

        var agentAssignment = await _context.AgentFlats
            .Include(af => af.Agent)
            .FirstOrDefaultAsync(af => af.FlatId == tenant.House.FlatId);

        if (agentAssignment == null)
            return BadRequest(new
            {
                success = false,
                errorType = "NoAgentAssigned",
                message = "No agent is currently assigned to this flat. Please raise a complaint or contact management before proceeding with vacating."
            });

        var vacateRequest = new VacateRequest
        {
            Id = Guid.NewGuid(),
            TenantId = dto.TenantId,
            HouseId = tenant.House.Id,
            FlatId = tenant.House.Flat.Id,
            LandlordId = tenant.House.Flat.LandlordId,
            Status = "Open",
            VacateMonth = dto.VacateMonth,
            VacateYear = dto.VacateYear,
            SitDeposit = tenant.House.Flat.SitDeposit,
            AssignedAgentId = agentAssignment.AgentId,
            InspectionAssignedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.VacateRequests.Add(vacateRequest);
        await _context.SaveChangesAsync();

        var agent = agentAssignment.Agent;
        var houseNumber = tenant.House.HouseNumber;

        try { await _emailService.SendVacateAssignedAgentEmailAsync(agent.Email, agent.FirstName, houseNumber); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send vacate inspection email to agent {AgentId}", agent.Id); }

        try { await _notificationService.SendToUserAsync(agent.Id.ToString(), $"You have a new vacate inspection assigned for house {houseNumber}.", "property"); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify agent of vacate inspection assignment"); }

        try
        {
            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"A new vacate request has been raised for house {houseNumber}.",
                "property");
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of new vacate request"); }

        return Ok(new { success = true, data = new { vacateRequest.Id } });
    }

    // PATCH /api/vacate/{id}/cancel
    [HttpPatch("{id:guid}/cancel")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Agent,Tenant")]
    public async Task<IActionResult> CancelRequest(Guid id, [FromBody] CancelVacateRequestDto dto)
    {
        var callerId = GetCallerId();
        var callerRole = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

        var vacateRequest = await _context.VacateRequests
            .FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted);

        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        if (callerRole == "Tenant" && callerId != vacateRequest.TenantId)
            return Forbid();

        if (vacateRequest.Status == "Closed" || vacateRequest.Status == "Cancelled")
            return BadRequest(new { success = false, message = "This vacate request cannot be cancelled." });

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == vacateRequest.TenantId);
        var house = await _context.Houses
            .Include(h => h.Flat)
            .FirstOrDefaultAsync(h => h.Id == vacateRequest.HouseId);
        var houseNumber = house?.HouseNumber ?? "";

        // Settlement-exists gate
        var existingSettlement = await _context.VacateSettlements.AnyAsync(s => s.VacateRequestId == id && !s.IsVoided);

        if (existingSettlement)
        {
            if (callerRole == "Tenant")
                return BadRequest(new { success = false, message = "This request has already been finalized. Please contact management to cancel it." });

            if (string.IsNullOrWhiteSpace(dto?.Reason))
                return BadRequest(new { success = false, message = "A cancellation reason is required for post-settlement cancellation." });

            var settlements = await _context.VacateSettlements
                .Where(s => s.VacateRequestId == id && !s.IsVoided)
                .ToListAsync();
            foreach (var s in settlements)
                s.IsVoided = true;

            var forfeited = await _context.VacateForfeitedAdvances
                .Where(f => f.VacateRequestId == id && !f.IsVoided)
                .ToListAsync();
            foreach (var f in forfeited)
                f.IsVoided = true;

            var reasonNote = $"Cancelled post-settlement by management. Reason: {dto.Reason}";
            vacateRequest.FinalRemarks = string.IsNullOrWhiteSpace(vacateRequest.FinalRemarks)
                ? reasonNote
                : $"{vacateRequest.FinalRemarks}\n{reasonNote}";

            if (tenant != null)
            {
                try { await _emailService.SendVacateSettlementReversedEmailAsync(tenant.Email, tenant.FirstName, houseNumber); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send settlement reversal email to tenant"); }

                try { await _notificationService.SendToUserAsync(vacateRequest.TenantId.ToString(), "Your vacate request was cancelled and any forfeited amounts have been restored to your account.", "property"); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify tenant of settlement reversal"); }
            }
        }

        // Revert SettlingVacate → Active
        if (tenant != null && tenant.TenantStatus == TenantStatus.SettlingVacate)
            tenant.TenantStatus = TenantStatus.Active;

        // Backfill missing payment rows from VacateMonth/Year through current month

        if (tenant != null && house?.Flat != null)
        {
            var flat = house.Flat;
            var now = DateTime.UtcNow;

            var serviceCharge = await _context.ServiceChargeSettings
                .Where(s => s.IsActive && !s.IsDeleted && s.MinRent <= house.RentFee && s.MaxRent >= house.RentFee)
                .OrderBy(s => s.MinRent)
                .Select(s => (decimal?)s.ServiceCharge)
                .FirstOrDefaultAsync() ?? 0m;

            var cursorYear = vacateRequest.VacateYear;
            var cursorMonth = vacateRequest.VacateMonth;

            while (cursorYear < now.Year || (cursorYear == now.Year && cursorMonth <= now.Month))
            {
                var exists = await _context.Payments.AnyAsync(p =>
                    p.TenantId == tenant.Id &&
                    p.Month == cursorMonth &&
                    p.Year == cursorYear &&
                    p.TenancyCycle == tenant.TenancyCycle &&
                    p.HouseId == tenant.HouseId &&
                    !p.IsInitialPayment &&
                    !p.IsDeleted);

                if (!exists)
                {
                    var rentDueDay = Math.Min(flat.RentDueDay, DateTime.DaysInMonth(cursorYear, cursorMonth));
                    var dueDate = new DateTime(cursorYear, cursorMonth, rentDueDay);
                    var totalDue = house.RentFee;

                    _context.Payments.Add(new Payment
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenant.Id,
                        HouseId = house.Id,
                        FlatId = flat.Id,
                        LandlordId = flat.LandlordId,
                        Amount = totalDue,
                        AmountPaid = 0,
                        Balance = totalDue,
                        RentAmount = house.RentFee,
                        ServiceChargeAmount = serviceCharge,
                        PaymentStatus = PaymentTransactionStatus.Pending,
                        PaymentType = PaymentType.Rent,
                        PhoneNumber = tenant.PhoneNumber,
                        DueDate = dueDate,
                        Month = cursorMonth,
                        Year = cursorYear,
                        TenancyCycle = tenant.TenancyCycle,
                        Description = $"Monthly rent - {new DateTime(cursorYear, cursorMonth, 1):MMMM yyyy}",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                cursorMonth++;
                if (cursorMonth > 12) { cursorMonth = 1; cursorYear++; }
            }
        }

        vacateRequest.Status = "Cancelled";
        vacateRequest.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        if (vacateRequest.AssignedAgentId.HasValue)
        {
            var agent = await _context.Agents.FindAsync(vacateRequest.AssignedAgentId.Value);
            if (agent != null)
            {
                try { await _emailService.SendVacateCancelledAgentEmailAsync(agent.Email, agent.FirstName, houseNumber); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send vacate cancelled email to agent {AgentId}", agent.Id); }

                try { await _notificationService.SendToUserAsync(agent.Id.ToString(), $"Vacate request for house {houseNumber} has been cancelled.", "property"); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify agent of vacate cancellation"); }
            }
        }

        try
        {
            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"Vacate request for house {houseNumber} has been cancelled.",
                "property");
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of vacate cancellation"); }

        return Ok(new { success = true, data = new { vacateRequest.Status } });
    }

    // GET /api/vacate/agent/{id}
    [HttpGet("agent/{id:guid}")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> GetAgentRequest(Guid id)
    {
        var callerId = GetCallerId();

        var vacateRequest = await _context.VacateRequests
            .Include(v => v.InspectionLines)
                .ThenInclude(l => l.Attachments)
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        if (vacateRequest.AssignedAgentId != callerId)
            return Forbid();

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == vacateRequest.TenantId);
        var house = await _context.Houses
            .Include(h => h.Flat)
            .FirstOrDefaultAsync(h => h.Id == vacateRequest.HouseId);

        return Ok(new
        {
            success = true,
            data = new
            {
                vacateRequest.Id,
                vacateRequest.Status,
                vacateRequest.VacateMonth,
                vacateRequest.VacateYear,
                vacateRequest.SitDeposit,
                vacateRequest.InspectionAssignedAt,
                vacateRequest.InspectionSubmittedAt,
                vacateRequest.CreatedAt,
                tenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}" : "",
                houseNumber = house?.HouseNumber ?? "",
                flatName = house?.Flat?.FlatName ?? "",
                inspectionLines = vacateRequest.InspectionLines
                    .OrderBy(l => l.LineOrder)
                    .Select(l => new
                    {
                        l.Id,
                        l.Description,
                        l.AssessedAmount,
                        l.LineOrder,
                        l.CreatedAt,
                        attachments = l.Attachments.Select(a => new
                        {
                            a.Id,
                            a.FilePath,
                            a.FileType,
                            a.FileSizeBytes,
                            a.UploadedAt
                        })
                    })
            }
        });
    }

    // POST /api/vacate/{id}/inspection-lines
    [HttpPost("{id:guid}/inspection-lines")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> AddInspectionLine(Guid id, [FromBody] AddVacateInspectionLineDto dto)
    {
        var callerId = GetCallerId();

        var vacateRequest = await _context.VacateRequests
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        if (vacateRequest.AssignedAgentId != callerId)
            return Forbid();

        if (vacateRequest.Status != "Open")
            return BadRequest(new { success = false, message = "Inspection lines can only be added to an open vacate request." });

        if (vacateRequest.InspectionSubmittedAt != null)
            return BadRequest(new { success = false, message = "This inspection has already been submitted." });

        var maxOrder = await _context.VacateInspectionLines
            .Where(l => l.VacateRequestId == id)
            .Select(l => (int?)l.LineOrder)
            .MaxAsync() ?? 0;

        var line = new VacateInspectionLine
        {
            Id = Guid.NewGuid(),
            VacateRequestId = id,
            Description = dto.Description,
            LineOrder = maxOrder + 1,
            CreatedAt = DateTime.UtcNow
        };

        _context.VacateInspectionLines.Add(line);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, data = new { line.Id, line.LineOrder } });
    }

    // POST /api/vacate/inspection-lines/{lineId}/attachments
    [HttpPost("inspection-lines/{lineId:guid}/attachments")]
    [Authorize(Roles = "Agent")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> UploadInspectionAttachments(Guid lineId, [FromForm] List<IFormFile> images)
    {
        var callerId = GetCallerId();

        var line = await _context.VacateInspectionLines
            .Include(l => l.VacateRequest)
            .FirstOrDefaultAsync(l => l.Id == lineId);

        if (line == null || line.VacateRequest == null || line.VacateRequest.IsDeleted)
            return NotFound(new { success = false, message = "Inspection line not found." });

        var vacateRequest = line.VacateRequest;

        if (vacateRequest.AssignedAgentId != callerId)
            return Forbid();

        if (vacateRequest.Status != "Open")
            return BadRequest(new { success = false, message = "Attachments can only be added to an open vacate request." });

        if (vacateRequest.InspectionSubmittedAt != null)
            return BadRequest(new { success = false, message = "This inspection has already been submitted." });

        images ??= new List<IFormFile>();

        const long maxFileSize = 4 * 1024 * 1024;
        foreach (var file in images)
            if (file.Length > maxFileSize)
                return BadRequest(new { success = false, message = $"{file.FileName} exceeds the 4MB limit." });

        var vacateRequestId = vacateRequest.Id;
        var saveDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "vacate-attachments", vacateRequestId.ToString());
        Directory.CreateDirectory(saveDir);

        var created = new List<object>();
        foreach (var file in images)
        {
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            using var stream = new FileStream(Path.Combine(saveDir, fileName), FileMode.Create);
            await file.CopyToAsync(stream);

            var attachment = new VacateInspectionLineAttachment
            {
                Id = Guid.NewGuid(),
                VacateInspectionLineId = lineId,
                VacateRequestId = vacateRequestId,
                FileType = "Image",
                FilePath = $"/vacate-attachments/{vacateRequestId}/{fileName}",
                FileSizeBytes = file.Length,
                UploadedAt = DateTime.UtcNow
            };
            _context.VacateInspectionLineAttachments.Add(attachment);
            created.Add(new { attachment.Id, attachment.FilePath });
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true, data = created });
    }

    // DELETE /api/vacate/inspection-lines/{lineId}
    [HttpDelete("inspection-lines/{lineId:guid}")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> DeleteInspectionLine(Guid lineId)
    {
        var callerId = GetCallerId();

        var line = await _context.VacateInspectionLines
            .Include(l => l.VacateRequest)
            .Include(l => l.Attachments)
            .FirstOrDefaultAsync(l => l.Id == lineId);

        if (line == null || line.VacateRequest == null || line.VacateRequest.IsDeleted)
            return NotFound(new { success = false, message = "Inspection line not found." });

        var vacateRequest = line.VacateRequest;

        if (vacateRequest.AssignedAgentId != callerId)
            return Forbid();

        if (vacateRequest.Status != "Open")
            return BadRequest(new { success = false, message = "Inspection lines can only be removed from an open vacate request." });

        if (vacateRequest.InspectionSubmittedAt != null)
            return BadRequest(new { success = false, message = "This inspection has already been submitted." });

        var vacateRequestId = vacateRequest.Id;
        foreach (var attachment in line.Attachments)
        {
            var diskPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot",
                "vacate-attachments", vacateRequestId.ToString(),
                Path.GetFileName(attachment.FilePath));
            try { if (System.IO.File.Exists(diskPath)) System.IO.File.Delete(diskPath); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to delete attachment file {FilePath}", diskPath); }
        }

        _context.VacateInspectionLineAttachments.RemoveRange(line.Attachments);
        _context.VacateInspectionLines.Remove(line);
        await _context.SaveChangesAsync();

        return Ok(new { success = true });
    }

    // PATCH /api/vacate/{id}/submit-inspection
    [HttpPatch("{id:guid}/submit-inspection")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> SubmitInspection(Guid id)
    {
        var callerId = GetCallerId();

        var vacateRequest = await _context.VacateRequests
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        if (vacateRequest.AssignedAgentId != callerId)
            return Forbid();

        if (vacateRequest.Status != "Open")
            return BadRequest(new { success = false, message = "This vacate request cannot be submitted." });

        if (vacateRequest.InspectionSubmittedAt != null)
            return BadRequest(new { success = false, message = "This inspection has already been submitted." });

        var hasLines = await _context.VacateInspectionLines.AnyAsync(l => l.VacateRequestId == id);
        if (!hasLines)
            return BadRequest(new { success = false, message = "Add at least one inspection line before submitting." });

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == vacateRequest.HouseId);
        var houseNumber = house?.HouseNumber ?? "";

        vacateRequest.InspectionSubmittedAt = DateTime.UtcNow;
        vacateRequest.Status = "AwaitingApproval";
        vacateRequest.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        try
        {
            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"Vacate inspection for house {houseNumber} has been submitted and is awaiting review.",
                "property");
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of vacate inspection submission"); }

        return Ok(new { success = true });
    }
}

public class CreateVacateRequestDto
{
    public Guid TenantId { get; set; }
    public int VacateMonth { get; set; }
    public int VacateYear { get; set; }
}

public class CancelVacateRequestDto
{
    public string? Reason { get; set; }
}

public class AddVacateInspectionLineDto
{
    public string Description { get; set; } = string.Empty;
}
