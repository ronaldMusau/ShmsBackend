using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Api.Services.Payment;
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
    private readonly IPaymentService _paymentService;
    private readonly IMpesaService _mpesaService;

    public VacateController(
        ShmsDbContext context,
        IEmailService emailService,
        INotificationService notificationService,
        ILogger<VacateController> logger,
        IPaymentService paymentService,
        IMpesaService mpesaService)
    {
        _context = context;
        _emailService = emailService;
        _notificationService = notificationService;
        _logger = logger;
        _paymentService = paymentService;
        _mpesaService = mpesaService;
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

            try { await _notificationService.SendToUserAsync(dto.TenantId.ToString(), $"You have KES {arrears:N2} in arrears. Please clear this before your vacate request can proceed.", "property", "Vacate"); }
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

        try { await _notificationService.SendToUserAsync(agent.Id.ToString(), $"You have a new vacate inspection assigned for house {houseNumber}.", "property", "Vacate", vacateRequest.Id.ToString()); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify agent of vacate inspection assignment"); }

        try
        {
            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"A new vacate request has been raised for house {houseNumber}.",
                "property", "Vacate", vacateRequest.Id.ToString());
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

        var paidSuccessor = await _context.Tenants.AnyAsync(t =>
            t.HouseId == vacateRequest.HouseId
            && !t.IsDeleted
            && t.TenantStatus != TenantStatus.SettlingVacate
            && t.HasCompletedInitialPayment);
        if (paidSuccessor)
            return BadRequest(new { success = false, message = "A new tenant has already been confirmed for this house. This vacate request can no longer be cancelled." });

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

                try { await _notificationService.SendToUserAsync(vacateRequest.TenantId.ToString(), "Your vacate request was cancelled and any forfeited amounts have been restored to your account.", "property", "Vacate", vacateRequest.Id.ToString()); }
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

                try { await _notificationService.SendToUserAsync(agent.Id.ToString(), $"Vacate request for house {houseNumber} has been cancelled.", "property", "Vacate", vacateRequest.Id.ToString()); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify agent of vacate cancellation"); }
            }
        }

        try
        {
            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"Vacate request for house {houseNumber} has been cancelled.",
                "property", "Vacate", vacateRequest.Id.ToString());
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of vacate cancellation"); }

        return Ok(new { success = true, data = new { vacateRequest.Status } });
    }

    // GET /api/vacate/agent/my-requests
    [HttpGet("agent/my-requests")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> GetMyAgentRequests()
    {
        var callerId = GetCallerId();

        var requests = await _context.VacateRequests
            .Where(v => v.AssignedAgentId == callerId && !v.IsDeleted)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();

        var tenantIds = requests.Select(v => v.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

        var houseIds = requests.Select(v => v.HouseId).Distinct().ToList();
        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var flatIds = requests.Select(v => v.FlatId).Distinct().ToList();
        var flats = await _context.Flats
            .Where(f => flatIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var data = requests.Select(v => new
        {
            v.Id,
            v.Status,
            v.VacateMonth,
            v.VacateYear,
            v.InspectionAssignedAt,
            v.InspectionSubmittedAt,
            tenantName = tenants.GetValueOrDefault(v.TenantId, ""),
            houseNumber = houses.GetValueOrDefault(v.HouseId, ""),
            flatName = flats.GetValueOrDefault(v.FlatId, "")
        });

        return Ok(new { success = true, data });
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

        var firstStep = await _context.ApprovalSequenceSteps
            .Where(s => s.Module == "Vacate")
            .OrderBy(s => s.StepOrder)
            .FirstOrDefaultAsync();
        if (firstStep == null)
            return BadRequest(new { success = false, message = "No approval sequence has been configured for Vacate. Please contact management." });

        vacateRequest.InspectionSubmittedAt = DateTime.UtcNow;
        vacateRequest.CurrentApprovalStepOrder = firstStep.StepOrder;
        vacateRequest.Status = "AwaitingApproval";
        vacateRequest.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        try
        {
            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"Vacate inspection for house {houseNumber} has been submitted and is awaiting review.",
                "property", "Vacate", vacateRequest.Id.ToString());
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of vacate inspection submission"); }

        return Ok(new { success = true });
    }

    // GET /api/vacate/all
    [HttpGet("all")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetAllVacateRequests(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? status = null,
        [FromQuery] Guid? flatId = null,
        [FromQuery] Guid? houseId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        var query = _context.VacateRequests.Where(v => !v.IsDeleted).AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(v => v.Status == status);
        if (flatId.HasValue)
            query = query.Where(v => v.FlatId == flatId.Value);
        if (houseId.HasValue)
            query = query.Where(v => v.HouseId == houseId.Value);
        if (fromDate.HasValue)
            query = query.Where(v => v.CreatedAt >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(v => v.CreatedAt <= toDate.Value.AddDays(1));

        var total = await query.CountAsync();

        var pagedRequests = await query
            .OrderByDescending(v => v.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var houseIds = pagedRequests.Select(v => v.HouseId).Distinct().ToList();
        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var flatIds = pagedRequests.Select(v => v.FlatId).Distinct().ToList();
        var flats = await _context.Flats
            .Where(f => flatIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var tenantIds = pagedRequests.Select(v => v.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

        var agentIds = pagedRequests
            .Where(v => v.AssignedAgentId.HasValue)
            .Select(v => v.AssignedAgentId!.Value)
            .Distinct().ToList();
        var agents = await _context.Agents
            .Where(a => agentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => $"{a.FirstName} {a.LastName}");

        var vacateIds = pagedRequests.Select(v => v.Id).ToList();
        var unreadCounts = await _context.VacateMessages
            .Where(m => vacateIds.Contains(m.VacateRequestId)
                     && m.SenderRole == "Tenant"
                     && !m.IsReadByManagement)
            .GroupBy(m => m.VacateRequestId)
            .Select(g => new { VacateRequestId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.VacateRequestId, g => g.Count);

        var data = pagedRequests.Select(v => new
        {
            v.Id,
            v.Status,
            v.VacateMonth,
            v.VacateYear,
            v.SitDeposit,
            HouseNumber = houses.GetValueOrDefault(v.HouseId, "-"),
            FlatName = flats.GetValueOrDefault(v.FlatId, "-"),
            TenantName = tenants.GetValueOrDefault(v.TenantId, "-"),
            AssignedAgentName = v.AssignedAgentId.HasValue ? agents.GetValueOrDefault(v.AssignedAgentId.Value, "-") : null,
            v.CreatedAt,
            v.InspectionSubmittedAt,
            UnreadMessageCount = unreadCounts.GetValueOrDefault(v.Id, 0)
        }).ToList();

        var totals = new
        {
            TotalOpen = await _context.VacateRequests.CountAsync(v => !v.IsDeleted && v.Status == "Open"),
            TotalAwaitingApproval = await _context.VacateRequests.CountAsync(v => !v.IsDeleted && v.Status == "AwaitingApproval"),
            TotalApproved = await _context.VacateRequests.CountAsync(v => !v.IsDeleted && v.Status == "Approved")
        };

        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize),
            totals
        });
    }

    // GET /api/vacate/my-approval-queue
    [HttpGet("my-approval-queue")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetMyVacateApprovalQueue()
    {
        var adminId = GetCallerId();
        var myStepOrders = await _context.ApprovalSequenceSteps
            .Where(s => s.Module == "Vacate" && s.ApproverId == adminId)
            .Select(s => s.StepOrder)
            .ToListAsync();

        var requests = await _context.VacateRequests
            .Where(v => !v.IsDeleted && v.CurrentApprovalStepOrder != null && myStepOrders.Contains(v.CurrentApprovalStepOrder.Value))
            .OrderBy(v => v.InspectionSubmittedAt)
            .ToListAsync();

        var houseIds = requests.Select(v => v.HouseId).Distinct().ToList();
        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var flatIds = requests.Select(v => v.FlatId).Distinct().ToList();
        var flats = await _context.Flats
            .Where(f => flatIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var tenantIds = requests.Select(v => v.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

        var data = requests.Select(v => new
        {
            v.Id,
            HouseNumber = houses.GetValueOrDefault(v.HouseId, "-"),
            FlatName = flats.GetValueOrDefault(v.FlatId, "-"),
            TenantName = tenants.GetValueOrDefault(v.TenantId, "-"),
            v.VacateMonth,
            v.VacateYear,
            v.CurrentApprovalStepOrder,
            v.InspectionSubmittedAt
        }).ToList();

        return Ok(new { success = true, data });
    }

    // GET /api/vacate/my-approval-history
    [HttpGet("my-approval-history")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetMyVacateApprovalHistory()
    {
        var adminId = GetCallerId();

        var actions = await _context.VacateApprovalActions
            .Where(a => a.ApproverId == adminId && (a.Decision == "Approved" || a.Decision == "Rejected"))
            .OrderByDescending(a => a.ActionedAt)
            .ToListAsync();

        var requestIds = actions.Select(a => a.VacateRequestId).Distinct().ToList();
        var requests = await _context.VacateRequests
            .Where(v => requestIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v);

        var houseIds = requests.Values.Select(v => v.HouseId).Distinct().ToList();
        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var flatIds = requests.Values.Select(v => v.FlatId).Distinct().ToList();
        var flats = await _context.Flats
            .Where(f => flatIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var tenantIds = requests.Values.Select(v => v.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

        var data = actions.Select(a =>
        {
            requests.TryGetValue(a.VacateRequestId, out var v);
            return new
            {
                a.Id,
                vacateRequestId = a.VacateRequestId,
                houseNumber = v != null ? houses.GetValueOrDefault(v.HouseId, "-") : "-",
                flatName = v != null ? flats.GetValueOrDefault(v.FlatId, "-") : "-",
                tenantName = v != null ? tenants.GetValueOrDefault(v.TenantId, "-") : "-",
                a.AttemptNumber,
                a.StepOrder,
                decision = a.Decision,
                notes = a.Notes,
                actionedAt = a.ActionedAt
            };
        }).ToList();

        return Ok(new { success = true, data });
    }

    // GET /api/vacate/{id}
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetVacateRequestById(Guid id)
    {
        var vacateRequest = await _context.VacateRequests
            .Include(v => v.InspectionLines)
                .ThenInclude(l => l.Attachments)
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == vacateRequest.TenantId);
        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == vacateRequest.HouseId);
        var flat = await _context.Flats.FirstOrDefaultAsync(f => f.Id == vacateRequest.FlatId);
        var agent = vacateRequest.AssignedAgentId.HasValue
            ? await _context.Agents.FirstOrDefaultAsync(a => a.Id == vacateRequest.AssignedAgentId.Value)
            : null;

        var approvalActions = await _context.VacateApprovalActions
            .Where(a => a.VacateRequestId == id)
            .OrderBy(a => a.AttemptNumber)
            .ThenBy(a => a.StepOrder)
            .ToListAsync();

        var approverIds = approvalActions.Select(a => a.ApproverId).Distinct().ToList();
        var approvers = await _context.PortalUsers
            .Where(u => approverIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}");

        var settlement = await _context.VacateSettlements
            .FirstOrDefaultAsync(s => s.VacateRequestId == id && !s.IsVoided);

        var forfeitedAdvance = await _context.VacateForfeitedAdvances
            .FirstOrDefaultAsync(f => f.VacateRequestId == id && !f.IsVoided);

        return Ok(new
        {
            success = true,
            data = new
            {
                vacateRequest.Id,
                vacateRequest.Status,
                vacateRequest.NeedsResubmission,
                vacateRequest.ApprovalAttemptNumber,
                vacateRequest.VacateMonth,
                vacateRequest.VacateYear,
                vacateRequest.SitDeposit,
                vacateRequest.CurrentApprovalStepOrder,
                vacateRequest.InspectionAssignedAt,
                vacateRequest.InspectionSubmittedAt,
                vacateRequest.SettledAt,
                vacateRequest.FinalRemarks,
                vacateRequest.FinalRemarksAt,
                vacateRequest.ClosedAt,
                vacateRequest.ClosedByAdminId,
                tenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}" : "-",
                houseNumber = house?.HouseNumber ?? "-",
                flatName = flat?.FlatName ?? "-",
                assignedAgentName = agent != null ? $"{agent.FirstName} {agent.LastName}" : null,
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
                            a.FilePath,
                            a.FileType,
                            a.FileSizeBytes,
                            a.UploadedAt
                        })
                    }),
                approvalHistory = approvalActions.Select(a => new
                {
                    a.AttemptNumber,
                    a.StepOrder,
                    a.Decision,
                    a.Notes,
                    approverName = approvers.TryGetValue(a.ApproverId, out var name) ? name : a.ApproverId.ToString(),
                    a.ActionedAt
                }),
                settlement = settlement == null ? null : (object)new
                {
                    settlement.Direction,
                    settlement.Amount,
                    settlement.Description,
                    settlement.PaidAt
                },
                forfeitedAdvance = forfeitedAdvance == null ? null : (object)new
                {
                    forfeitedAdvance.TotalAdvanceAmount,
                    forfeitedAdvance.AmountAppliedToDamages,
                    forfeitedAdvance.AmountForfeitedUnused
                }
            }
        });
    }

    // GET /api/vacate/tenant/{id}
    [HttpGet("tenant/{id:guid}")]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> GetTenantVacateRequest(Guid id)
    {
        var vacateRequest = await _context.VacateRequests
            .Include(v => v.InspectionLines)
                .ThenInclude(l => l.Attachments)
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        if (vacateRequest.TenantId != GetCallerId())
            return Forbid();

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == vacateRequest.HouseId);
        var flat = await _context.Flats.FirstOrDefaultAsync(f => f.Id == vacateRequest.FlatId);
        var agent = vacateRequest.AssignedAgentId.HasValue
            ? await _context.Agents.FirstOrDefaultAsync(a => a.Id == vacateRequest.AssignedAgentId.Value)
            : null;

        var settlement = await _context.VacateSettlements
            .FirstOrDefaultAsync(s => s.VacateRequestId == id && !s.IsVoided);

        var forfeitedAdvance = await _context.VacateForfeitedAdvances
            .FirstOrDefaultAsync(f => f.VacateRequestId == id && !f.IsVoided);

        var existingAppeal = await _context.VacateAppeals
            .Include(a => a.Lines)
            .FirstOrDefaultAsync(a => a.VacateRequestId == id);

        return Ok(new
        {
            success = true,
            data = new
            {
                id = vacateRequest.Id,
                status = vacateRequest.Status,
                vacateMonth = vacateRequest.VacateMonth,
                vacateYear = vacateRequest.VacateYear,
                sitDeposit = vacateRequest.SitDeposit,
                inspectionAssignedAt = vacateRequest.InspectionAssignedAt,
                inspectionSubmittedAt = vacateRequest.InspectionSubmittedAt,
                settledAt = vacateRequest.SettledAt,
                finalRemarks = vacateRequest.FinalRemarks,
                finalRemarksAt = vacateRequest.FinalRemarksAt,
                closedAt = vacateRequest.ClosedAt,
                houseNumber = house?.HouseNumber ?? "-",
                flatName = flat?.FlatName ?? "-",
                assignedAgentName = agent != null ? $"{agent.FirstName} {agent.LastName}" : null,
                inspectionLines = vacateRequest.InspectionLines
                    .OrderBy(l => l.LineOrder)
                    .Select(l => new
                    {
                        id = l.Id,
                        description = l.Description,
                        assessedAmount = l.AssessedAmount,
                        lineOrder = l.LineOrder,
                        attachments = l.Attachments.Select(a => new
                        {
                            filePath = a.FilePath,
                            fileType = a.FileType,
                            uploadedAt = a.UploadedAt
                        })
                    }),
                settlement = settlement == null ? null : (object)new
                {
                    settlement.Id,
                    settlement.Direction,
                    settlement.Amount,
                    settlement.Description,
                    settlement.PaidAt
                },
                forfeitedAdvance = forfeitedAdvance == null ? null : (object)new
                {
                    forfeitedAdvance.TotalAdvanceAmount,
                    forfeitedAdvance.AmountAppliedToDamages,
                    forfeitedAdvance.AmountForfeitedUnused
                },
                hasAppealed = existingAppeal != null,
                appealLines = existingAppeal == null ? null : existingAppeal.Lines.Select(l => new
                {
                    l.VacateInspectionLineId,
                    l.Reason
                })
            }
        });
    }

    // POST /api/vacate/{id}/appeal
    [HttpPost("{id:guid}/appeal")]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> SubmitVacateAppeal(Guid id, [FromBody] VacateAppealSubmitDto dto)
    {
        var vacateRequest = await _context.VacateRequests
            .Include(v => v.InspectionLines)
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        if (vacateRequest.TenantId != GetCallerId())
            return Forbid();

        if (vacateRequest.Status != "Approved")
            return BadRequest(new { success = false, message = "You can only appeal an approved settlement." });

        if (await _context.VacateAppeals.AnyAsync(a => a.VacateRequestId == id))
            return BadRequest(new { success = false, message = "You have already submitted an appeal for this request." });

        if (dto.Lines == null || !dto.Lines.Any())
            return BadRequest(new { success = false, message = "Select at least one line to appeal." });

        var validLineIds = vacateRequest.InspectionLines.Select(l => l.Id).ToHashSet();
        var invalidLine = dto.Lines.FirstOrDefault(l => !validLineIds.Contains(l.LineId));
        if (invalidLine != null)
            return BadRequest(new { success = false, message = "One or more selected lines do not belong to this request." });

        var appeal = new VacateAppeal
        {
            VacateRequestId = id,
            SubmittedAt = DateTime.UtcNow
        };
        foreach (var entry in dto.Lines)
        {
            appeal.Lines.Add(new VacateAppealLine
            {
                VacateAppealId = appeal.Id,
                VacateInspectionLineId = entry.LineId,
                Reason = entry.Reason
            });
        }
        _context.VacateAppeals.Add(appeal);

        vacateRequest.Status = "UnderAppeal";
        vacateRequest.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == vacateRequest.HouseId);
        var houseNumber = house?.HouseNumber ?? "";

        try
        {
            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"Tenant has appealed the vacate settlement for house {houseNumber}.",
                "property", "Vacate", vacateRequest.Id.ToString());
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of vacate appeal"); }

        try
        {
            var superAdmins = await _context.SuperAdmins.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var adminUsers = await _context.AdminUsers.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var managers = await _context.Managers.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var secretaries = await _context.Secretaries.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var managementUsers = superAdmins.Concat(adminUsers).Concat(managers).Concat(secretaries).ToList();
            foreach (var mgr in managementUsers)
            {
                try { await _emailService.SendVacateAppealManagementEmailAsync(mgr.Email, mgr.FirstName, houseNumber); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send appeal email to {Email}", mgr.Email); }
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to query management users for vacate appeal email"); }

        return Ok(new { success = true, message = "Your appeal has been submitted and management has been notified." });
    }

    // GET /api/vacate/landlord/my-requests
    [HttpGet("landlord/my-requests")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordVacateRequests()
    {
        var callerId = GetCallerId();

        var requests = await _context.VacateRequests
            .Include(v => v.InspectionLines)
            .Where(v => v.LandlordId == callerId && !v.IsDeleted)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();

        var houseIds = requests.Select(v => v.HouseId).Distinct().ToList();
        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var flatIds = requests.Select(v => v.FlatId).Distinct().ToList();
        var flats = await _context.Flats
            .Where(f => flatIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var tenantIds = requests.Select(v => v.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

        var agentIds = requests
            .Where(v => v.AssignedAgentId.HasValue)
            .Select(v => v.AssignedAgentId!.Value)
            .Distinct().ToList();
        var agents = await _context.Agents
            .Where(a => agentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => $"{a.FirstName} {a.LastName}");

        var data = requests.Select(v => new
        {
            id = v.Id,
            status = v.Status,
            vacateMonth = v.VacateMonth,
            vacateYear = v.VacateYear,
            inspectionSubmittedAt = v.InspectionSubmittedAt,
            settledAt = v.SettledAt,
            finalRemarks = v.FinalRemarks,
            finalRemarksAt = v.FinalRemarksAt,
            houseNumber = houses.GetValueOrDefault(v.HouseId, "-"),
            flatName = flats.GetValueOrDefault(v.FlatId, "-"),
            tenantName = tenants.GetValueOrDefault(v.TenantId, "-"),
            assignedAgentName = v.AssignedAgentId.HasValue ? agents.GetValueOrDefault(v.AssignedAgentId.Value, "-") : null,
            inspectionLineCount = v.InspectionLines.Count
        }).ToList();

        return Ok(new { success = true, data });
    }

    // GET /api/vacate/landlord/{id}
    [HttpGet("landlord/{id:guid}")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordVacateRequest(Guid id)
    {
        var callerId = GetCallerId();

        var vacateRequest = await _context.VacateRequests
            .Include(v => v.InspectionLines)
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        if (vacateRequest.LandlordId != callerId)
            return Forbid();

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == vacateRequest.HouseId);
        var flat = await _context.Flats.FirstOrDefaultAsync(f => f.Id == vacateRequest.FlatId);
        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == vacateRequest.TenantId);
        var agent = vacateRequest.AssignedAgentId.HasValue
            ? await _context.Agents.FirstOrDefaultAsync(a => a.Id == vacateRequest.AssignedAgentId.Value)
            : null;

        return Ok(new
        {
            success = true,
            data = new
            {
                id = vacateRequest.Id,
                status = vacateRequest.Status,
                vacateMonth = vacateRequest.VacateMonth,
                vacateYear = vacateRequest.VacateYear,
                inspectionSubmittedAt = vacateRequest.InspectionSubmittedAt,
                settledAt = vacateRequest.SettledAt,
                finalRemarks = vacateRequest.FinalRemarks,
                finalRemarksAt = vacateRequest.FinalRemarksAt,
                houseNumber = house?.HouseNumber ?? "-",
                flatName = flat?.FlatName ?? "-",
                tenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}" : "-",
                assignedAgentName = agent != null ? $"{agent.FirstName} {agent.LastName}" : null,
                inspectionLineCount = vacateRequest.InspectionLines.Count
            }
        });
    }

    // PATCH /api/vacate/{id}/approval-action
    [HttpPatch("{id:guid}/approval-action")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> VacateApprovalAction(Guid id, [FromBody] VacateApprovalActionDto dto)
    {
        var callerId = GetCallerId();

        var vacateRequest = await _context.VacateRequests
            .Include(v => v.InspectionLines)
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        var steps = await _context.ApprovalSequenceSteps
            .Where(s => s.Module == "Vacate")
            .OrderBy(s => s.StepOrder)
            .ToListAsync();
        var currentStep = steps.FirstOrDefault(s => s.StepOrder == vacateRequest.CurrentApprovalStepOrder);

        if (currentStep == null)
            return BadRequest(new { success = false, message = "This request is not currently awaiting approval." });

        if (currentStep.ApproverId != callerId)
            return Forbid();

        if (dto.Approved && dto.LineAmounts != null)
        {
            foreach (var entry in dto.LineAmounts)
            {
                var line = vacateRequest.InspectionLines.FirstOrDefault(l => l.Id == entry.LineId);
                if (line != null)
                    line.AssessedAmount = entry.Amount;
            }
        }

        _context.VacateApprovalActions.Add(new VacateApprovalAction
        {
            Id = Guid.NewGuid(),
            VacateRequestId = id,
            AttemptNumber = 1,
            StepOrder = currentStep.StepOrder,
            ApproverId = callerId,
            Decision = dto.Approved ? "Approved" : "Rejected",
            Notes = dto.Notes,
            ActionedAt = DateTime.UtcNow
        });

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == vacateRequest.HouseId);
        var houseNumber = house?.HouseNumber ?? "";

        if (!dto.Approved)
        {
            if (string.IsNullOrWhiteSpace(dto.Notes))
                return BadRequest(new { success = false, message = "Rejection notes are required." });

            vacateRequest.CurrentApprovalStepOrder = null;
            vacateRequest.Status = "Rejected";
            vacateRequest.NeedsResubmission = true;
            vacateRequest.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            try
            {
                await _notificationService.SendToRolesAsync(
                    new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                    $"Vacate request for house {houseNumber} was rejected at step {currentStep.StepOrder}.",
                    "property", "Vacate", vacateRequest.Id.ToString());
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of vacate approval rejection"); }

            try
            {
                var superAdmins = await _context.SuperAdmins.Select(u => new { u.Email, u.FirstName }).ToListAsync();
                var adminUsers = await _context.AdminUsers.Select(u => new { u.Email, u.FirstName }).ToListAsync();
                var managers = await _context.Managers.Select(u => new { u.Email, u.FirstName }).ToListAsync();
                var secretaries = await _context.Secretaries.Select(u => new { u.Email, u.FirstName }).ToListAsync();
                var managementUsers = superAdmins.Concat(adminUsers).Concat(managers).Concat(secretaries).ToList();
                foreach (var mgr in managementUsers)
                {
                    try { await _emailService.SendVacateRejectedManagementEmailAsync(mgr.Email, mgr.FirstName, houseNumber, dto.Notes); }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to send rejection email to {Email}", mgr.Email); }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to query management users for vacate rejection email"); }

            return Ok(new { success = true, message = "Rejected. Management may edit and resubmit, or issue final remarks to close this request." });
        }

        // Approved — advance to next step or complete the sequence
        var nextStep = steps.FirstOrDefault(s => s.StepOrder > currentStep.StepOrder);
        await _context.SaveChangesAsync();

        if (nextStep != null)
        {
            vacateRequest.CurrentApprovalStepOrder = nextStep.StepOrder;
            vacateRequest.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            try { await _notificationService.SendToUserAsync(nextStep.ApproverId.ToString(), $"Vacate request for house {houseNumber} requires your approval (step {nextStep.StepOrder}).", "property", "Vacate", vacateRequest.Id.ToString()); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify next approver of vacate step"); }

            var nextApprover = await _context.PortalUsers.FirstOrDefaultAsync(u => u.Id == nextStep.ApproverId);
            if (nextApprover != null)
            {
                try { await _emailService.SendApprovalStepEmailAsync(nextApprover.Email, nextApprover.FirstName, $"Vacate — {houseNumber}", nextStep.StepOrder); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send approval-step email to next vacate approver"); }
            }

            return Ok(new { success = true, message = "Approved. Advanced to the next approval step." });
        }
        else
        {
            vacateRequest.CurrentApprovalStepOrder = null;
            vacateRequest.Status = "AwaitingFinalRemarks";
            vacateRequest.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Internal approval complete. Awaiting management final remarks before settlement is calculated." });
        }
    }

    // PATCH /api/vacate/{id}/resubmit
    [HttpPatch("{id:guid}/resubmit")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> ResubmitVacateRequest(Guid id, [FromBody] VacateResubmitDto dto)
    {
        var vacateRequest = await _context.VacateRequests
            .Include(v => v.InspectionLines)
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        var isRejectedPendingResubmission = vacateRequest.Status == "Rejected" && vacateRequest.NeedsResubmission;
        var isUnderAppeal = vacateRequest.Status == "UnderAppeal";
        if (!isRejectedPendingResubmission && !isUnderAppeal)
            return BadRequest(new { success = false, message = "This request is not pending resubmission or appeal review." });

        if (dto.LineAmounts != null)
        {
            foreach (var entry in dto.LineAmounts)
            {
                var line = vacateRequest.InspectionLines.FirstOrDefault(l => l.Id == entry.LineId);
                if (line != null)
                    line.AssessedAmount = entry.Amount;
            }
        }

        vacateRequest.ApprovalAttemptNumber += 1;

        var firstStep = await _context.ApprovalSequenceSteps
            .Where(s => s.Module == "Vacate")
            .OrderBy(s => s.StepOrder)
            .FirstOrDefaultAsync();
        if (firstStep == null)
            return BadRequest(new { success = false, message = "No approval sequence has been configured for Vacate. Please contact management." });

        vacateRequest.CurrentApprovalStepOrder = firstStep.StepOrder;
        vacateRequest.NeedsResubmission = false;
        vacateRequest.Status = "AwaitingApproval";
        vacateRequest.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == vacateRequest.HouseId);
        var houseNumber = house?.HouseNumber ?? "";

        try { await _notificationService.SendToUserAsync(firstStep.ApproverId.ToString(), $"Vacate request for house {houseNumber} has been resubmitted and requires your approval (step {firstStep.StepOrder}).", "property", "Vacate", vacateRequest.Id.ToString()); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify first approver of vacate resubmission"); }

        var firstApprover = await _context.PortalUsers.FirstOrDefaultAsync(u => u.Id == firstStep.ApproverId);
        if (firstApprover != null)
        {
            try { await _emailService.SendApprovalStepEmailAsync(firstApprover.Email, firstApprover.FirstName, $"Vacate — {houseNumber}", firstStep.StepOrder); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send approval-step email to first vacate approver on resubmission"); }
        }

        return Ok(new { success = true, data = new { vacateRequest.ApprovalAttemptNumber } });
    }

    // PATCH /api/vacate/{id}/final-remarks
    [HttpPatch("{id:guid}/final-remarks")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> IssueFinalRemarks(Guid id, [FromBody] VacateFinalRemarksDto dto)
    {
        var vacateRequest = await _context.VacateRequests
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);

        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        if (vacateRequest.Status != "Rejected" && vacateRequest.Status != "AwaitingFinalRemarks")
            return BadRequest(new { success = false, message = "Final remarks cannot be issued for this request's current status." });

        if (dto.Outcome != "Approved" && dto.Outcome != "Rejected")
            return BadRequest(new { success = false, message = "Outcome must be 'Approved' or 'Rejected'." });

        if (vacateRequest.Status == "AwaitingFinalRemarks" && dto.Outcome != "Approved")
            return BadRequest(new { success = false, message = "Outcome must be 'Approved' when internal approval succeeded, or 'Rejected' when closing out a rejected request." });

        if (vacateRequest.Status == "Rejected" && dto.Outcome != "Rejected")
            return BadRequest(new { success = false, message = "Outcome must be 'Approved' when internal approval succeeded, or 'Rejected' when closing out a rejected request." });

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == vacateRequest.HouseId);
        var houseNumber = house?.HouseNumber ?? "";

        vacateRequest.FinalRemarks = dto.Remarks;
        vacateRequest.FinalRemarksAt = DateTime.UtcNow;
        vacateRequest.FinalRemarksByAdminId = GetCallerId();
        vacateRequest.Status = dto.Outcome;
        vacateRequest.NeedsResubmission = false;
        vacateRequest.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        if (dto.Outcome == "Approved")
        {
            var settlementResult = await _paymentService.CalculateVacateSettlementAsync(vacateRequest.Id);

            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == vacateRequest.TenantId);
            if (tenant != null)
            {
                try { await _notificationService.SendToUserAsync(vacateRequest.TenantId.ToString(), $"Your vacate request for house {houseNumber} has been approved. Settlement is ready for review.", "property", "Vacate", vacateRequest.Id.ToString()); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify tenant of vacate approval"); }

                try { await _emailService.SendVacateApprovedTenantEmailAsync(tenant.Email, tenant.FirstName, houseNumber); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send vacate approved email to tenant"); }
            }

            return Ok(new
            {
                success = true,
                message = "Approved. Settlement has been calculated.",
                data = new
                {
                    settlementResult.Direction,
                    settlementResult.FinalAmount,
                    settlementResult.TotalDamages,
                    settlementResult.AdvanceApplied,
                    settlementResult.AdvanceForfeited,
                    settlementResult.DepositApplied,
                    settlementResult.DepositRefunded
                }
            });
        }
        else
        {
            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == vacateRequest.TenantId);
            if (tenant != null)
            {
                try { await _notificationService.SendToUserAsync(vacateRequest.TenantId.ToString(), $"Your vacate request for house {houseNumber} has been closed. See final remarks in your portal.", "property", "Vacate", vacateRequest.Id.ToString()); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify tenant of vacate final rejection"); }

                try { await _emailService.SendVacateFinalRejectionTenantEmailAsync(tenant.Email, tenant.FirstName, houseNumber, dto.Remarks); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send vacate final rejection email to tenant"); }
            }

            return Ok(new { success = true, message = "Vacate request has been closed with final remarks." });
        }
    }

    // POST /api/vacate/settlement/{settlementId}/pay
    [HttpPost("settlement/{settlementId:guid}/pay")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Tenant")]
    public async Task<IActionResult> PayVacateSettlement(Guid settlementId, [FromBody] VacateSettlementPayDto dto)
    {
        var settlement = await _context.VacateSettlements
            .FirstOrDefaultAsync(s => s.Id == settlementId && !s.IsVoided);

        if (settlement == null)
            return NotFound(new { success = false, message = "Settlement not found." });

        if (settlement.Direction != "TenantOwes")
            return BadRequest(new { success = false, message = "This settlement does not require payment." });

        if (settlement.PaidAt != null)
            return BadRequest(new { success = false, message = "This settlement has already been paid." });

        var callerRole = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (callerRole == "Tenant" && settlement.TenantId != GetCallerId())
            return Forbid();

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == settlement.HouseId);
        var houseNumber = house?.HouseNumber ?? "";

        var reference = $"VAC-{settlementId.ToString().Substring(0, 8).ToUpper()}";

        STKPushResponse stkResponse;
        try
        {
            stkResponse = await _mpesaService.InitiateSTKPushAsync(new STKPushRequest
            {
                PhoneNumber = dto.PhoneNumber,
                Amount = settlement.Amount,
                AccountReference = reference,
                TransactionDesc = $"Vacate Settlement — {houseNumber}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "STK push failed for vacate settlement {SettlementId}", settlementId);
            return BadRequest(new { success = false, message = ex.Message });
        }

        _context.VacateCheckoutAttempts.Add(new VacateCheckoutAttempt
        {
            VacateSettlementId = settlementId,
            CheckoutRequestId = stkResponse.CheckoutRequestID,
            AttemptStatus = "Processing"
        });
        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            message = "Payment initiated. Please check your phone.",
            data = new
            {
                settlementId,
                checkoutRequestId = stkResponse.CheckoutRequestID,
                amount = settlement.Amount
            }
        });
    }

    // POST /api/vacate/settlement/mark-timeout/{checkoutRequestId}
    [HttpPost("settlement/mark-timeout/{checkoutRequestId}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Tenant")]
    public async Task<IActionResult> MarkVacateSettlementTimeout(string checkoutRequestId)
    {
        var attempt = await _context.VacateCheckoutAttempts
            .Include(a => a.VacateSettlement)
            .FirstOrDefaultAsync(a => a.CheckoutRequestId == checkoutRequestId);

        if (attempt == null)
            return NotFound(new { success = false, message = "Checkout attempt not found." });

        var callerRole = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (callerRole == "Tenant" && attempt.VacateSettlement?.TenantId != GetCallerId())
            return Forbid();

        if (attempt.ProcessedAt == null)
        {
            attempt.AttemptStatus = "Failed";
            attempt.ResultDesc = "Timed out — no callback received";
            attempt.ProcessedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        return Ok(new { success = true, attemptStatus = attempt.AttemptStatus });
    }

    // GET /api/vacate/settlement/checkout-status/{checkoutRequestId}
    [HttpGet("settlement/checkout-status/{checkoutRequestId}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Tenant")]
    public async Task<IActionResult> GetVacateSettlementCheckoutStatus(string checkoutRequestId)
    {
        var attempt = await _context.VacateCheckoutAttempts
            .Include(a => a.VacateSettlement)
            .FirstOrDefaultAsync(a => a.CheckoutRequestId == checkoutRequestId);

        if (attempt == null)
            return Ok(new { success = true, data = new { attemptStatus = "Processing" } });

        var callerRole = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (callerRole == "Tenant" && attempt.VacateSettlement?.TenantId != GetCallerId())
            return Forbid();

        return Ok(new
        {
            success = true,
            data = new
            {
                attemptStatus = attempt.AttemptStatus,
                resultCode = attempt.ResultCode,
                resultDesc = attempt.ResultDesc,
                settlementId = attempt.VacateSettlementId,
                paidAt = attempt.VacateSettlement?.PaidAt
            }
        });
    }

    // GET /api/vacate/refunds/all
    [HttpGet("refunds/all")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetAllRefunds(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] int? month = null,
        [FromQuery] int? year = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        var query = _context.VacateSettlements
            .Where(s => s.Direction == "ManagementOwes" && !s.IsVoided)
            .AsQueryable();

        if (tenantId.HasValue) query = query.Where(s => s.TenantId == tenantId.Value);
        if (month.HasValue) query = query.Where(s => s.CreatedAt.Month == month.Value);
        if (year.HasValue) query = query.Where(s => s.CreatedAt.Year == year.Value);
        if (fromDate.HasValue) query = query.Where(s => s.CreatedAt >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(s => s.CreatedAt.Date <= toDate.Value.Date);

        var totalRefundable = await query.Where(s => s.PaidAt == null).SumAsync(s => s.Amount);
        var totalPaid = await query.Where(s => s.PaidAt != null).SumAsync(s => s.Amount);

        var total = await query.CountAsync();
        var paged = await query.OrderByDescending(s => s.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var tenantIds = paged.Select(s => s.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

        var houseIds = paged.Select(s => s.HouseId).Distinct().ToList();
        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var data = paged.Select(s => new
        {
            s.Id,
            tenantName = tenants.GetValueOrDefault(s.TenantId, "-"),
            houseNumber = houses.GetValueOrDefault(s.HouseId, "-"),
            s.Amount,
            s.Description,
            s.PaidAt,
            s.CreatedAt
        });

        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize),
            totals = new { totalRefundable, totalPaid }
        });
    }

    // GET /api/vacate/refunds/years
    [HttpGet("refunds/years")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetRefundYears()
    {
        var years = await _context.VacateSettlements
            .Where(s => s.Direction == "ManagementOwes" && !s.IsVoided)
            .Select(s => s.CreatedAt.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();

        return Ok(new { success = true, data = years });
    }

    // PATCH /api/vacate/refunds/{id}/mark-paid
    [HttpPatch("refunds/{id:guid}/mark-paid")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> MarkRefundPaid(Guid id)
    {
        var settlement = await _context.VacateSettlements
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsVoided);

        if (settlement == null)
            return NotFound(new { success = false, message = "Settlement not found." });

        if (settlement.Direction != "ManagementOwes")
            return BadRequest(new { success = false, message = "This is not a refund record." });

        if (settlement.PaidAt != null)
            return BadRequest(new { success = false, message = "This refund has already been marked as paid." });

        settlement.PaidAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == settlement.HouseId);
        var houseNumber = house?.HouseNumber ?? "";

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == settlement.TenantId);
        if (tenant != null)
        {
            try { await _notificationService.SendToUserAsync(settlement.TenantId.ToString(), $"Management has processed your vacate refund for house {houseNumber}. Please check your portal for details.", "property", "Vacate", settlement.VacateRequestId.ToString()); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify tenant of vacate refund payment"); }

            try { await _emailService.SendVacateRefundPaidTenantEmailAsync(tenant.Email, tenant.FirstName, houseNumber); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send vacate refund paid email to tenant"); }
        }

        return Ok(new { success = true, message = "Refund marked as paid." });
    }

    // GET /api/vacate/{id}/messages
    [HttpGet("{id:guid}/messages")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetVacateMessages(Guid id)
    {
        var vacateRequest = await _context.VacateRequests.FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);
        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        var messages = await _context.VacateMessages
            .Where(m => m.VacateRequestId == id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        var unread = messages.Where(m => m.SenderRole == "Tenant" && !m.IsReadByManagement).ToList();
        foreach (var msg in unread)
            msg.IsReadByManagement = true;
        if (unread.Count > 0)
            await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            messages = messages.Select(m => new
            {
                m.Id,
                m.SenderRole,
                m.SenderUserId,
                m.Message,
                m.CreatedAt,
                m.IsReadByManagement,
                m.IsReadByTenant
            })
        });
    }

    // POST /api/vacate/{id}/messages
    [HttpPost("{id:guid}/messages")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> SendVacateMessage(Guid id, [FromBody] VacateMessageDto dto)
    {
        var vacateRequest = await _context.VacateRequests.FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);
        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        if (vacateRequest.Status == "Cancelled" || vacateRequest.ClosedAt != null)
            return BadRequest(new { success = false, message = "This vacate request is closed. Messaging is disabled." });

        if (string.IsNullOrWhiteSpace(dto.Message))
            return BadRequest(new { success = false, message = "Message cannot be empty." });

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == vacateRequest.HouseId);
        var houseNumber = house?.HouseNumber ?? "";

        var message = new VacateMessage
        {
            VacateRequestId = id,
            SenderRole = "Management",
            SenderUserId = GetCallerId(),
            Message = dto.Message,
            IsReadByManagement = true,
            IsReadByTenant = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.VacateMessages.Add(message);
        await _context.SaveChangesAsync();

        try { await _notificationService.SendToUserAsync(vacateRequest.TenantId.ToString(), $"New message on your vacate request for house {houseNumber}: {dto.Message}", "property", "Vacate", vacateRequest.Id.ToString()); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify tenant of new management vacate message"); }

        return Ok(new
        {
            success = true,
            data = new
            {
                message.Id,
                message.SenderRole,
                message.SenderUserId,
                message.Message,
                message.CreatedAt,
                message.IsReadByManagement,
                message.IsReadByTenant
            }
        });
    }

    // GET /api/vacate/tenant/{id}/messages
    [HttpGet("tenant/{id:guid}/messages")]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> GetTenantVacateMessages(Guid id)
    {
        var vacateRequest = await _context.VacateRequests
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted && v.TenantId == GetCallerId());
        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        var messages = await _context.VacateMessages
            .Where(m => m.VacateRequestId == id)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        var unread = messages.Where(m => m.SenderRole == "Management" && !m.IsReadByTenant).ToList();
        foreach (var msg in unread)
            msg.IsReadByTenant = true;
        if (unread.Count > 0)
            await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            messages = messages.Select(m => new
            {
                m.Id,
                m.SenderRole,
                m.SenderUserId,
                m.Message,
                m.CreatedAt,
                m.IsReadByManagement,
                m.IsReadByTenant
            })
        });
    }

    // POST /api/vacate/tenant/{id}/messages
    [HttpPost("tenant/{id:guid}/messages")]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> SendTenantVacateMessage(Guid id, [FromBody] VacateMessageDto dto)
    {
        var vacateRequest = await _context.VacateRequests
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted && v.TenantId == GetCallerId());
        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        if (vacateRequest.Status == "Cancelled" || vacateRequest.ClosedAt != null)
            return BadRequest(new { success = false, message = "This vacate request is closed. Messaging is disabled." });

        if (string.IsNullOrWhiteSpace(dto.Message))
            return BadRequest(new { success = false, message = "Message cannot be empty." });

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == vacateRequest.HouseId);
        var houseNumber = house?.HouseNumber ?? "";

        var message = new VacateMessage
        {
            VacateRequestId = id,
            SenderRole = "Tenant",
            SenderUserId = GetCallerId(),
            Message = dto.Message,
            IsReadByManagement = false,
            IsReadByTenant = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.VacateMessages.Add(message);
        await _context.SaveChangesAsync();

        try
        {
            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"New message from tenant on vacate request for house {houseNumber}: {dto.Message}",
                "property", "Vacate", vacateRequest.Id.ToString());
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of new tenant vacate message"); }

        return Ok(new
        {
            success = true,
            data = new
            {
                message.Id,
                message.SenderRole,
                message.SenderUserId,
                message.Message,
                message.CreatedAt,
                message.IsReadByManagement,
                message.IsReadByTenant
            }
        });
    }

    // GET /api/vacate/{id}/clear-checklist
    [HttpGet("{id:guid}/clear-checklist")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetClearChecklist(Guid id)
    {
        var vacateRequest = await _context.VacateRequests
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);
        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        var tenant = await _context.Tenants
            .FirstOrDefaultAsync(t => t.Id == vacateRequest.TenantId && !t.IsDeleted);
        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        if (tenant.TenantStatus != TenantStatus.SettlingVacate)
            return Ok(new
            {
                success = true,
                eligible = false,
                message = "Tenant is not yet in SettlingVacate status. Clearance is not yet applicable."
            });

        var arrears = await _context.Payments
            .Where(p => p.TenantId == vacateRequest.TenantId
                && p.TenancyCycle == tenant.TenancyCycle
                && !p.IsDeleted
                && (p.PaymentStatus == PaymentTransactionStatus.Overdue
                    || p.PaymentStatus == PaymentTransactionStatus.Pending
                    || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid))
            .SumAsync(p => p.Balance);

        var settlement = await _context.VacateSettlements
            .FirstOrDefaultAsync(s => s.VacateRequestId == id && !s.IsVoided);

        var isSettlementClear = settlement == null
            || settlement.Direction == "ManagementOwes"
            || settlement.PaidAt != null;

        var canClear = arrears == 0 && isSettlementClear;

        return Ok(new
        {
            success = true,
            eligible = true,
            data = new
            {
                arrearsAmount = arrears,
                arrearsCleared = arrears == 0,
                settlementDirection = settlement?.Direction,
                settlementPaid = settlement == null || settlement.PaidAt != null,
                settlementRequiresPayment = settlement != null && settlement.Direction == "TenantOwes",
                hasRefundPending = settlement != null && settlement.Direction == "ManagementOwes" && settlement.PaidAt == null,
                canClear
            }
        });
    }

    // PATCH /api/vacate/{id}/clear
    [HttpPatch("{id:guid}/clear")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> ClearVacateRequest(Guid id)
    {
        var vacateRequest = await _context.VacateRequests
            .FirstOrDefaultAsync(v => v.Id == id && !v.IsDeleted);
        if (vacateRequest == null)
            return NotFound(new { success = false, message = "Vacate request not found." });

        var tenant = await _context.Tenants
            .Include(t => t.House)
            .FirstOrDefaultAsync(t => t.Id == vacateRequest.TenantId && !t.IsDeleted);
        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        var arrears = await _context.Payments
            .Where(p => p.TenantId == vacateRequest.TenantId
                && p.TenancyCycle == tenant.TenancyCycle
                && !p.IsDeleted
                && (p.PaymentStatus == PaymentTransactionStatus.Overdue
                    || p.PaymentStatus == PaymentTransactionStatus.Pending
                    || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid))
            .SumAsync(p => p.Balance);

        if (arrears > 0)
            return BadRequest(new { success = false, message = $"Tenant has KES {arrears:N2} in outstanding arrears. Clear before closing out." });

        var settlement = await _context.VacateSettlements
            .FirstOrDefaultAsync(s => s.VacateRequestId == id && !s.IsVoided);

        if (settlement != null && settlement.Direction == "TenantOwes" && settlement.PaidAt == null)
            return BadRequest(new { success = false, message = "Tenant still owes a settlement payment. This must be paid before clearance." });

        var houseNumber = "";

        if (tenant.House != null)
        {
            houseNumber = tenant.House.HouseNumber;
            var hasOtherActiveTenant = await _context.Tenants.AnyAsync(t =>
                t.Id != tenant.Id
                && t.HouseId == tenant.House.Id
                && !t.IsDeleted
                && t.TenantStatus == TenantStatus.Active);

            if (!hasOtherActiveTenant)
            {
                tenant.House.OccupancyStatus = OccupancyStatus.Vacant;
                tenant.House.PaymentStatus = PaymentStatus.NotPaid;
                tenant.House.UpdatedAt = DateTime.UtcNow;
            }
        }

        var openHistory = await _context.TenantHouseHistories
            .Where(h => h.TenantId == tenant.Id && h.RemovedAt == null)
            .FirstOrDefaultAsync();
        if (openHistory != null)
            openHistory.RemovedAt = DateTime.UtcNow;

        tenant.IsDeleted = true;
        tenant.DeletedAt = DateTime.UtcNow;
        tenant.IsActive = false;

        vacateRequest.ClosedAt = DateTime.UtcNow;
        vacateRequest.ClosedByAdminId = GetCallerId();

        await _context.SaveChangesAsync();

        try { await _notificationService.SendToUserAsync(tenant.Id.ToString(), "Your tenancy has been formally closed out. Thank you for your tenancy.", "property", "Vacate", vacateRequest.Id.ToString()); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify tenant of vacate clearance"); }

        try
        {
            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"Vacate request for house {houseNumber} has been fully closed out.",
                "property", "Vacate", vacateRequest.Id.ToString());
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of vacate clearance"); }

        return Ok(new { success = true, message = "Vacate request closed out successfully." });
    }

    // GET /api/vacate/forfeited-advances/all
    [HttpGet("forfeited-advances/all")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetAllForfeitedAdvances(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] Guid? houseId = null,
        [FromQuery] Guid? flatId = null,
        [FromQuery] int? month = null,
        [FromQuery] int? year = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] decimal? minAmount = null,
        [FromQuery] decimal? maxAmount = null)
    {
        var allQuery = _context.VacateForfeitedAdvances.Where(f => !f.IsVoided);
        var totalForfeited = await allQuery.SumAsync(f => f.AmountForfeitedUnused);
        var totalAppliedToDamages = await allQuery.SumAsync(f => f.AmountAppliedToDamages);

        var query = allQuery.AsQueryable();
        if (tenantId.HasValue) query = query.Where(f => f.TenantId == tenantId.Value);
        if (houseId.HasValue) query = query.Where(f => f.HouseId == houseId.Value);
        if (flatId.HasValue) query = query.Where(f => f.FlatId == flatId.Value);
        if (month.HasValue) query = query.Where(f => f.CreatedAt.Month == month.Value);
        if (year.HasValue) query = query.Where(f => f.CreatedAt.Year == year.Value);
        if (fromDate.HasValue) query = query.Where(f => f.CreatedAt >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(f => f.CreatedAt < toDate.Value.Date.AddDays(1));
        if (minAmount.HasValue) query = query.Where(f => f.AmountForfeitedUnused >= minAmount.Value);
        if (maxAmount.HasValue) query = query.Where(f => f.AmountForfeitedUnused <= maxAmount.Value);

        var total = await query.CountAsync();
        var paged = await query.OrderByDescending(f => f.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var tenantIds = paged.Select(f => f.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

        var houseIds = paged.Select(f => f.HouseId).Distinct().ToList();
        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var data = paged.Select(f => new
        {
            f.Id,
            tenantName = tenants.GetValueOrDefault(f.TenantId, "-"),
            houseNumber = houses.GetValueOrDefault(f.HouseId, "-"),
            f.TotalAdvanceAmount,
            f.AmountAppliedToDamages,
            f.AmountForfeitedUnused,
            f.CreatedAt
        });

        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize),
            totals = new { totalForfeited, totalAppliedToDamages }
        });
    }

    // GET /api/vacate/forfeited-advances/years
    [HttpGet("forfeited-advances/years")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetForfeitedAdvanceYears()
    {
        var years = await _context.VacateForfeitedAdvances
            .Where(f => !f.IsVoided)
            .Select(f => f.CreatedAt.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();

        return Ok(new { success = true, data = years });
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

public class VacateApprovalActionDto
{
    public bool Approved { get; set; }
    public string? Notes { get; set; }
    public List<VacateLineAmountDto>? LineAmounts { get; set; }
}

public class VacateLineAmountDto
{
    public Guid LineId { get; set; }
    public decimal Amount { get; set; }
}

public class VacateResubmitDto
{
    public List<VacateLineAmountDto>? LineAmounts { get; set; }
}

public class VacateFinalRemarksDto
{
    public string Remarks { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
}

public class VacateAppealSubmitDto
{
    public List<VacateAppealLineDto> Lines { get; set; } = new();
}

public class VacateAppealLineDto
{
    public Guid LineId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class VacateSettlementPayDto
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class VacateMessageDto
{
    public string Message { get; set; } = string.Empty;
}
