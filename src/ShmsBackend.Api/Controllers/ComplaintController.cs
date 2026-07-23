using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using System.Security.Claims;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ComplaintController : ControllerBase
{
    private readonly ShmsDbContext _context;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ComplaintController> _logger;

    public ComplaintController(
        ShmsDbContext context,
        IEmailService emailService,
        INotificationService notificationService,
        ILogger<ComplaintController> logger)
    {
        _context = context;
        _emailService = emailService;
        _notificationService = notificationService;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    // GET /api/complaint/all
    [HttpGet("all")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? status = null,
        [FromQuery] Guid? complaintTypeId = null,
        [FromQuery] Guid? flatId = null,
        [FromQuery] Guid? houseId = null,
        [FromQuery] bool? isBillable = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? ticketNumberSearch = null)
    {
        var query = _context.Complaints.AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(c => c.Status == status);
        if (complaintTypeId.HasValue)
            query = query.Where(c => c.ComplaintTypeId == complaintTypeId.Value);
        if (flatId.HasValue)
            query = query.Where(c => c.FlatId == flatId.Value);
        if (houseId.HasValue)
            query = query.Where(c => c.HouseId == houseId.Value);
        if (isBillable.HasValue)
            query = query.Where(c => c.IsBillable == isBillable.Value);
        if (fromDate.HasValue)
            query = query.Where(c => c.CreatedAt >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(c => c.CreatedAt <= toDate.Value.AddDays(1));
        if (!string.IsNullOrEmpty(ticketNumberSearch))
            query = query.Where(c => c.TicketNumber.Contains(ticketNumberSearch));

        var total = await query.CountAsync();

        var pagedComplaints = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Cross-table name lookups (in-memory projection after ToListAsync, matching PaymentController pattern)
        var complaintTypeIds = pagedComplaints.Select(c => c.ComplaintTypeId).Distinct().ToList();
        var complaintTypes = await _context.ComplaintTypes
            .Where(t => complaintTypeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        var houseIds = pagedComplaints.Select(c => c.HouseId).Distinct().ToList();
        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var flatIds = pagedComplaints.Select(c => c.FlatId).Distinct().ToList();
        var flats = await _context.Flats
            .Where(f => flatIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var tenantIds = pagedComplaints.Select(c => c.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.FirstName + " " + t.LastName);

        var data = pagedComplaints.Select(c => new
        {
            c.Id,
            c.TicketNumber,
            ComplaintTypeName = complaintTypes.GetValueOrDefault(c.ComplaintTypeId, "Unknown"),
            c.Status,
            c.IsBillable,
            c.BillableTarget,
            c.BillableAmount,
            HouseNumber = houses.GetValueOrDefault(c.HouseId, "-"),
            FlatName = flats.GetValueOrDefault(c.FlatId, "-"),
            TenantName = tenants.GetValueOrDefault(c.TenantId, "-"),
            c.CreatedAt,
            c.EscalatedAt,
            c.AgentCompletionNotes,
            c.TenantVerificationStatus,
            c.TenantRejectionReason,
            c.AgentRedoCount
        }).ToList();

        var totals = new
        {
            TotalOpen = await _context.Complaints.CountAsync(c => c.Status == "Open"),
            TotalBillable = await _context.Complaints.CountAsync(c => c.IsBillable == true),
            TotalClosed = await _context.Complaints.CountAsync(c => c.Status == "Closed")
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

    // GET /api/complaint/{id}
    [HttpGet("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var complaint = await _context.Complaints
            .Include(c => c.ComplaintType)
            .Include(c => c.Attachments)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (complaint == null)
            return NotFound(new { success = false, message = "Complaint not found." });

        var result = await ComplaintDetailHelper.BuildAsync(_context, complaint, "Management");
        return Ok(new { success = true, data = result });
    }

    // PATCH /api/complaint/{id}/billable-decision
    [HttpPatch("{id}/billable-decision")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> BillableDecision(Guid id, [FromBody] BillableDecisionDto dto)
    {
        var complaint = await _context.Complaints.FirstOrDefaultAsync(c => c.Id == id);
        if (complaint == null)
            return NotFound(new { success = false, message = "Complaint not found." });

        var adminId = GetUserId();

        if (!dto.IsBillable)
        {
            if (string.IsNullOrWhiteSpace(dto.ResolutionNotes))
                return BadRequest(new { success = false, message = "Resolution notes are required when closing a complaint." });

            var originalStatus = complaint.Status;
            complaint.IsBillable = false;
            complaint.Status = "Closed";
            complaint.ReviewedAt = DateTime.UtcNow;
            complaint.ReviewedByAdminId = adminId;
            complaint.ClosedAt = DateTime.UtcNow;
            complaint.ClosedByAdminId = adminId;

            _context.ComplaintStatusHistory.Add(new ComplaintStatusHistoryEntry
            {
                Id = Guid.NewGuid(),
                ComplaintId = complaint.Id,
                FromStatus = originalStatus,
                ToStatus = "Closed",
                ChangedByAdminId = adminId,
                Notes = dto.ResolutionNotes,
                ChangedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            try
            {
                await _notificationService.SendToUserAsync(complaint.TenantId.ToString(), $"Your complaint {complaint.TicketNumber} has been reviewed and closed: {dto.ResolutionNotes}", "property");
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify tenant of complaint closure"); }

            try
            {
                var tenant2 = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == complaint.TenantId);
                if (tenant2 != null)
                    await _emailService.SendComplaintClosedEmailAsync(tenant2.Email, tenant2.FirstName, complaint.TicketNumber, dto.ResolutionNotes);
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send complaint closure email"); }

            return Ok(new { success = true, message = "Complaint closed (not billable)." });
        }

        if (string.IsNullOrWhiteSpace(dto.Justification))
            return BadRequest(new { success = false, message = "Justification is required when marking a complaint billable." });

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == complaint.TenantId);
        var flat = await _context.Flats.FirstOrDefaultAsync(f => f.Id == complaint.FlatId);
        if (tenant == null || flat == null)
            return BadRequest(new { success = false, message = "Could not resolve tenant or flat for this complaint." });

        var initialPayment = await _context.Payments
            .Where(p => p.TenantId == tenant.Id && p.IsInitialPayment == true && p.TenancyCycle == tenant.TenancyCycle && !p.IsDeleted)
            .FirstOrDefaultAsync();

        bool withinGracePeriod = false;
        if (initialPayment?.PaidAt != null)
        {
            var monthsSinceStart = ((DateTime.UtcNow.Year - initialPayment.PaidAt.Value.Year) * 12) + DateTime.UtcNow.Month - initialPayment.PaidAt.Value.Month;
            withinGracePeriod = monthsSinceStart < flat.BillableGracePeriodMonths;
        }

        string billableTarget;
        if (withinGracePeriod)
        {
            if (dto.BillableTargetOverride != null)
                return BadRequest(new { success = false, message = "Cannot override billable target — tenant is within the protected grace period." });
            billableTarget = "Management";
        }
        else
        {
            billableTarget = dto.BillableTargetOverride == "Management" ? "Management" : "Tenant";

            if (billableTarget == "Management" && string.IsNullOrWhiteSpace(dto.OverrideReason))
                return BadRequest(new { success = false, message = "An override reason is required when switching billable target to Management." });
        }

        if (billableTarget == "Management" && (dto.BillableAmount == null || dto.BillableAmount <= 0))
            return BadRequest(new { success = false, message = "A billable amount is required when the target is Management." });

        var firstStep = await _context.ApprovalSequenceSteps
            .Where(s => s.Module == "Complaints")
            .OrderBy(s => s.StepOrder)
            .FirstOrDefaultAsync();

        if (firstStep == null)
            return BadRequest(new { success = false, message = "No approval sequence is configured for Complaints yet. Set one up under Setups > Approvals before proceeding." });

        complaint.IsBillable = true;
        complaint.BillableExplanation = dto.Justification;
        complaint.BillableAmount = dto.BillableAmount;
        complaint.BillableTarget = billableTarget;
        complaint.BillableTargetOverrideReason = dto.OverrideReason;
        complaint.Status = "UnderReview";
        complaint.ReviewedAt = DateTime.UtcNow;
        complaint.ReviewedByAdminId = adminId;
        complaint.CurrentApprovalStepOrder = firstStep.StepOrder;
        complaint.ApprovalAttemptNumber = complaint.ApprovalAttemptNumber + 1;

        _context.ComplaintStatusHistory.Add(new ComplaintStatusHistoryEntry
        {
            Id = Guid.NewGuid(),
            ComplaintId = complaint.Id,
            FromStatus = "Open",
            ToStatus = "UnderReview",
            ChangedByAdminId = adminId,
            Notes = dto.Justification,
            ChangedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        try
        {
            await _notificationService.SendToUserAsync(firstStep.ApproverId.ToString(), $"Complaint {complaint.TicketNumber} requires your approval (step 1).", "property");
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify first approver"); }

        return Ok(new { success = true, message = "Complaint marked billable and sent for approval.", billableTarget });
    }

    // POST /api/complaint/{id}/escalate
    [HttpPost("{id}/escalate")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> Escalate(Guid id, [FromBody] EscalateDto dto)
    {
        var complaint = await _context.Complaints.FirstOrDefaultAsync(c => c.Id == id);
        if (complaint == null) return NotFound(new { success = false, message = "Complaint not found." });

        var assignment = await _context.AgentFlats
            .Where(af => af.FlatId == complaint.FlatId)
            .OrderByDescending(af => af.AssignedAt)
            .FirstOrDefaultAsync();
        if (assignment == null)
            return BadRequest(new { success = false, message = "No agent is assigned to this flat." });

        var adminId = GetUserId();
        complaint.EscalatedToAgentId = assignment.AgentId;
        complaint.EscalatedAt = DateTime.UtcNow;
        complaint.EscalationNotes = dto.Notes;

        _context.ComplaintStatusHistory.Add(new ComplaintStatusHistoryEntry
        {
            Id = Guid.NewGuid(),
            ComplaintId = complaint.Id,
            FromStatus = complaint.Status,
            ToStatus = complaint.Status,
            ChangedByAdminId = adminId,
            Notes = $"Escalated to agent for physical resolution. {dto.Notes}",
            ChangedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var agent = await _context.Agents.FirstOrDefaultAsync(a => a.Id == assignment.AgentId);
        if (agent != null)
        {
            try { await _emailService.SendComplaintEscalatedAgentEmailAsync(agent.Email, agent.FirstName, complaint.TicketNumber); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send agent escalation email"); }
            try { await _notificationService.SendToUserAsync(agent.Id.ToString(), $"Complaint {complaint.TicketNumber} has been escalated to you.", "property"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify agent of escalation"); }
        }
        return Ok(new { success = true, message = "Escalated to agent." });
    }

    // PATCH /api/complaint/{id}/final-close
    [HttpPatch("{id}/final-close")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> FinalClose(Guid id, [FromBody] FinalCloseDto dto)
    {
        var complaint = await _context.Complaints.FirstOrDefaultAsync(c => c.Id == id);
        if (complaint == null) return NotFound(new { success = false, message = "Complaint not found." });

        if (complaint.TenantVerificationStatus != "Verified")
            return BadRequest(new { success = false, message = "Cannot close — tenant has not verified the agent's completed work yet." });
        if (string.IsNullOrWhiteSpace(dto.ClosingComment))
            return BadRequest(new { success = false, message = "A closing comment is required." });

        var adminId = GetUserId();
        var originalStatus = complaint.Status;
        complaint.Status = "Closed";
        complaint.ClosedByAdminId = adminId;
        complaint.ClosedAt = DateTime.UtcNow;

        _context.ComplaintStatusHistory.Add(new ComplaintStatusHistoryEntry
        {
            Id = Guid.NewGuid(),
            ComplaintId = complaint.Id,
            FromStatus = originalStatus,
            ToStatus = "Closed",
            ChangedByAdminId = adminId,
            Notes = dto.ClosingComment,
            ChangedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == complaint.TenantId);
        if (tenant != null)
        {
            try { await _emailService.SendComplaintClosedEmailAsync(tenant.Email, tenant.FirstName, complaint.TicketNumber, dto.ClosingComment); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send final close email"); }
            try { await _notificationService.SendToUserAsync(tenant.Id.ToString(), $"Your complaint {complaint.TicketNumber} has been closed: {dto.ClosingComment}", "property"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify tenant of final close"); }
        }
        return Ok(new { success = true, message = "Complaint closed." });
    }

    // PATCH /api/complaint/{id}/approval-action
    [HttpPatch("{id}/approval-action")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> ApprovalAction(Guid id, [FromBody] ApprovalActionDto dto)
    {
        var complaint = await _context.Complaints.FirstOrDefaultAsync(c => c.Id == id);
        if (complaint == null) return NotFound(new { success = false, message = "Complaint not found." });
        var adminId = GetUserId();
        var steps = await _context.ApprovalSequenceSteps.Where(s => s.Module == "Complaints").OrderBy(s => s.StepOrder).ToListAsync();
        var currentStep = steps.FirstOrDefault(s => s.StepOrder == complaint.CurrentApprovalStepOrder);

        if (currentStep == null)
            return BadRequest(new { success = false, message = "This complaint is not currently awaiting an internal approval step." });
        if (currentStep.ApproverId != adminId)
            return Forbid();
        _context.ComplaintApprovalActions.Add(new ComplaintApprovalAction
        {
            Id = Guid.NewGuid(),
            ComplaintId = complaint.Id,
            AttemptNumber = complaint.ApprovalAttemptNumber,
            StepOrder = currentStep.StepOrder,
            ApproverId = adminId,
            Decision = dto.Approved ? "Approved" : "Rejected",
            Notes = dto.Notes,
            ActionedAt = DateTime.UtcNow
        });
        if (!dto.Approved)
        {
            if (string.IsNullOrWhiteSpace(dto.Notes))
                return BadRequest(new { success = false, message = "Rejection notes are required." });

            // Restart the entire sequence from step 1 (per locked design)
            complaint.CurrentApprovalStepOrder = steps.First().StepOrder;
            complaint.ApprovalAttemptNumber += 1;
            await _context.SaveChangesAsync();
            var originalDecider = await _context.PortalUsers.FirstOrDefaultAsync(u => u.Id == complaint.ReviewedByAdminId);
            // Notify whoever made the original billable call (the reviewer), per locked design — rejection goes back to them, not to the sequence
            if (complaint.ReviewedByAdminId.HasValue)
            {
                try { await _notificationService.SendToUserAsync(complaint.ReviewedByAdminId.Value.ToString(), $"Complaint {complaint.TicketNumber} was rejected at the approval step and needs your revision.", "property"); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify original reviewer of rejection"); }
            }
            if (originalDecider != null)
            {
                try { await _emailService.SendApprovalRejectedEmailAsync(originalDecider.Email, originalDecider.FirstName, complaint.TicketNumber, dto.Notes!); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send rejection email"); }
            }
            return Ok(new { success = true, message = "Rejected. Sent back to the original reviewer for revision." });
        }

        // Approved — advance to next step, or complete the sequence
        var nextStep = steps.FirstOrDefault(s => s.StepOrder > currentStep.StepOrder);
        await _context.SaveChangesAsync();

        if (nextStep != null)
        {
            complaint.CurrentApprovalStepOrder = nextStep.StepOrder;
            await _context.SaveChangesAsync();

            try { await _notificationService.SendToUserAsync(nextStep.ApproverId.ToString(), $"Complaint {complaint.TicketNumber} requires your approval (step {nextStep.StepOrder}).", "property"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify next approver"); }

            var nextApprover = await _context.PortalUsers.FirstOrDefaultAsync(u => u.Id == nextStep.ApproverId);
            if (nextApprover != null)
            {
                try { await _emailService.SendApprovalStepEmailAsync(nextApprover.Email, nextApprover.FirstName, complaint.TicketNumber, nextStep.StepOrder); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send approval-step email"); }
            }

            return Ok(new { success = true, message = "Approved. Advanced to the next approval step." });
        }
        else
        {
            // Internal sequence fully cleared
            complaint.CurrentApprovalStepOrder = null;
            if (complaint.BillableTarget == "Tenant")
            {
                // Transparency-only for landlord — no action needed, ticket effectively done with the internal process
                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = "Approved. Internal sequence complete — billed to tenant, no landlord action required." });
            }
            else
            {
                await _context.SaveChangesAsync();
                try { await _notificationService.SendToUserAsync(complaint.LandlordId.ToString(), $"Complaint {complaint.TicketNumber} requires your final approval.", "property"); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify landlord of pending approval"); }
                var landlord = await _context.Landlords.FirstOrDefaultAsync(l => l.Id == complaint.LandlordId);
                if (landlord != null)
                {
                    try { await _emailService.SendLandlordApprovalNeededEmailAsync(landlord.Email, landlord.FirstName, complaint.TicketNumber); }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to send landlord approval-needed email"); }
                }
                return Ok(new { success = true, message = "Approved. Internal sequence complete — sent to landlord for final approval." });
            }
        }
    }

    // GET /api/complaint/my-approval-queue
    [HttpGet("my-approval-queue")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetMyApprovalQueue()
    {
        var adminId = GetUserId();
        var myStepOrders = await _context.ApprovalSequenceSteps
            .Where(s => s.Module == "Complaints" && s.ApproverId == adminId)
            .Select(s => s.StepOrder)
            .ToListAsync();

        var complaints = await _context.Complaints
            .Include(c => c.ComplaintType)
            .Where(c => c.CurrentApprovalStepOrder != null && myStepOrders.Contains(c.CurrentApprovalStepOrder.Value))
            .OrderBy(c => c.ReviewedAt)
            .ToListAsync();
        var data = new List<object>();
        foreach (var c in complaints)
            data.Add(await ComplaintDetailHelper.BuildAsync(_context, c, "Management"));

        return Ok(new { success = true, complaints = data });
    }
}

public class BillableDecisionDto
{
    public bool IsBillable { get; set; }
    public string? Justification { get; set; }
    public decimal? BillableAmount { get; set; }
    public string? BillableTargetOverride { get; set; }
    public string? OverrideReason { get; set; }
    public string? ResolutionNotes { get; set; }
}

public class EscalateDto
{
    public string? Notes { get; set; }
}

public class FinalCloseDto
{
    public string? ClosingComment { get; set; }
}

public class ApprovalActionDto { public bool Approved { get; set; } public string? Notes { get; set; } }
