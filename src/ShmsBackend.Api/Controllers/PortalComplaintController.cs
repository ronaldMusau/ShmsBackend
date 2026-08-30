using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using System.IO;
using System.Security.Claims;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/portalcomplaint")]
public class PortalComplaintController : ControllerBase
{
    private readonly ShmsDbContext _context;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<PortalComplaintController> _logger;

    public PortalComplaintController(
        ShmsDbContext context,
        IEmailService emailService,
        INotificationService notificationService,
        ILogger<PortalComplaintController> logger)
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

    // POST /api/portalcomplaint
    [HttpPost]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> Create([FromBody] CreateComplaintDto dto)
    {
        var tenantId = GetUserId();
        if (tenantId == Guid.Empty)
            return Unauthorized(new { success = false, message = "Invalid token." });

        var tenant = await _context.Tenants
            .Include(t => t.House)
                .ThenInclude(h => h != null ? h.Flat : null)
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant?.House == null || tenant.House.Flat == null)
            return BadRequest(new { success = false, message = "You do not currently have an assigned house." });

        var complaintType = await _context.ComplaintTypes.FirstOrDefaultAsync(t => t.Id == dto.ComplaintTypeId && t.IsActive);
        if (complaintType == null)
            return BadRequest(new { success = false, message = "Invalid complaint type." });

        var ticketNumber = await TicketNumberHelper.GenerateAsync(_context, tenant.House.HouseNumber);

        var complaint = new Complaint
        {
            TicketNumber = ticketNumber,
            TenantId = tenantId,
            HouseId = tenant.House.Id,
            FlatId = tenant.House.Flat.Id,
            LandlordId = tenant.House.Flat.LandlordId,
            ComplaintTypeId = dto.ComplaintTypeId,
            Description = dto.Description,
            Status = "Open",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Complaints.AddAsync(complaint);

        _context.ComplaintStatusHistory.Add(new ComplaintStatusHistoryEntry
        {
            ComplaintId = complaint.Id,
            FromStatus = null,
            ToStatus = "Open",
            ChangedByTenantId = tenantId,
            ChangedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        // Tenant confirmation
        try
        {
            await _emailService.SendComplaintConfirmationEmailAsync(tenant.Email, tenant.FirstName, complaint.TicketNumber, complaintType.Name);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send tenant complaint confirmation email"); }

        try
        {
            await _notificationService.SendToUserAsync(tenantId.ToString(), $"Your complaint {complaint.TicketNumber} has been received.", "property");
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send tenant complaint notification"); }

        // Management alert
        try
        {
            var superAdmins = await _context.SuperAdmins.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var adminUsers = await _context.AdminUsers.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var managers = await _context.Managers.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var secretaries = await _context.Secretaries.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var managementUsers = superAdmins.Concat(adminUsers).Concat(managers).Concat(secretaries).ToList();

            foreach (var mgr in managementUsers)
            {
                try
                {
                    await _emailService.SendComplaintManagementAlertEmailAsync(
                        mgr.Email, mgr.FirstName, complaint.TicketNumber, complaintType.Name,
                        $"{tenant.FirstName} {tenant.LastName}", tenant.House.HouseNumber, tenant.House.Flat.FlatName);
                }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send management complaint email to {Email}", mgr.Email); }
            }

            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"New complaint {complaint.TicketNumber} raised by {tenant.FirstName} {tenant.LastName}.",
                "property");
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to process management complaint alerts"); }

        // Landlord notification
        try
        {
            await _notificationService.SendToUserAsync(complaint.LandlordId.ToString(), $"A complaint has been raised at {tenant.House.HouseNumber} - {tenant.House.Flat.FlatName}.", "property");
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send landlord complaint notification"); }

        return Ok(new { success = true, data = new { complaint.Id, complaint.TicketNumber, complaint.Status } });
    }

    // GET /api/portalcomplaint/my-complaints
    [HttpGet("my-complaints")]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> GetMyComplaints()
    {
        var tenantId = GetUserId();
        if (tenantId == Guid.Empty)
            return Unauthorized(new { success = false, message = "Invalid token." });

        var complaints = await _context.Complaints
            .Include(c => c.ComplaintType)
            .Include(c => c.Attachments)
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var complaintIds = complaints.Select(c => c.Id).ToList();
        var unreadCounts = await _context.ComplaintMessages
            .Where(m => complaintIds.Contains(m.ComplaintId) && m.SenderRole == "Management" && !m.IsReadByTenant)
            .GroupBy(m => m.ComplaintId)
            .Select(g => new { ComplaintId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ComplaintId, g => g.Count);

        var result = complaints.Select(c => new
        {
            id = c.Id,
            ticketNumber = c.TicketNumber,
            complaintTypeId = c.ComplaintTypeId,
            complaintTypeName = c.ComplaintType?.Name,
            description = c.Description,
            status = c.Status,
            isBillable = c.IsBillable,
            billableTarget = c.BillableTarget,
            createdAt = c.CreatedAt,
            escalatedAt = c.EscalatedAt,
            closedAt = c.ClosedAt,
            agentCompletedAt = c.AgentCompletedAt,
            agentCompletionNotes = c.AgentCompletionNotes,
            tenantVerificationStatus = c.TenantVerificationStatus,
            attachments = c.Attachments.Select(a => new
            {
                a.FilePath,
                a.FileType,
                a.FileSizeBytes,
                a.UploadedAt
            }),
            unreadMessageCount = unreadCounts.GetValueOrDefault(c.Id, 0)
        }).ToList();

        return Ok(new { success = true, complaints = result });
    }

    // GET /api/portalcomplaint/landlord/my-complaints
    [HttpGet("landlord/my-complaints")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordComplaints()
    {
        var landlordIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(landlordIdStr, out var landlordId))
            return Unauthorized();

        var complaints = await (
            from c in _context.Complaints
            join h in _context.Houses on c.HouseId equals h.Id
            join f in _context.Flats on h.FlatId equals f.Id
            join t in _context.Tenants on c.TenantId equals t.Id
            where c.LandlordId == landlordId
            orderby c.CreatedAt descending
            select new
            {
                id = c.Id,
                ticketNumber = c.TicketNumber,
                complaintTypeId = c.ComplaintTypeId,
                complaintTypeName = c.ComplaintType.Name,
                description = c.Description,
                status = c.Status,
                isBillable = c.IsBillable,
                billableTarget = c.BillableTarget,
                createdAt = c.CreatedAt,
                houseNumber = h.HouseNumber,
                flatId = f.Id,
                flatName = f.FlatName,
                tenantFirstName = t.FirstName,
                tenantLastName = t.LastName,
                escalatedAt = c.EscalatedAt,
                closedAt = c.ClosedAt,
                agentCompletionNotes = c.AgentCompletionNotes,
                tenantVerificationStatus = c.TenantVerificationStatus,
                tenantRejectionReason = c.TenantRejectionReason,
                agentRedoCount = c.AgentRedoCount,
                attachments = _context.ComplaintAttachments
                    .Where(a => a.ComplaintId == c.Id)
                    .Select(a => new { a.FilePath, a.FileType, a.FileSizeBytes, a.UploadedAt })
                    .ToList()
            }
        ).ToListAsync();

        return Ok(new { success = true, complaints });
    }

    // GET /api/portalcomplaint/landlord/{id}
    [HttpGet("landlord/{id}")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordComplaintDetail(Guid id)
    {
        var landlordId = GetUserId();
        var complaint = await _context.Complaints
            .Include(c => c.ComplaintType)
            .Include(c => c.Attachments)
            .FirstOrDefaultAsync(c => c.Id == id && c.LandlordId == landlordId);

        if (complaint == null)
            return NotFound(new { success = false, message = "Complaint not found." });

        var result = await ComplaintDetailHelper.BuildAsync(_context, complaint, "Landlord");
        return Ok(new { success = true, data = result });
    }

    // GET /api/portalcomplaint/{id}
    [HttpGet("{id}")]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> GetTenantComplaintDetail(Guid id)
    {
        var tenantId = GetUserId();
        var complaint = await _context.Complaints
            .Include(c => c.ComplaintType)
            .Include(c => c.Attachments)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);

        if (complaint == null)
            return NotFound(new { success = false, message = "Complaint not found." });

        var result = await ComplaintDetailHelper.BuildAsync(_context, complaint, "Tenant");
        return Ok(new { success = true, data = result });
    }

    // GET /api/portalcomplaint/{id}/messages
    [HttpGet("{id}/messages")]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> GetTenantMessages(Guid id)
    {
        var tenantId = GetUserId();
        var complaint = await _context.Complaints.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);
        if (complaint == null) return NotFound(new { success = false, message = "Complaint not found." });

        var messages = await _context.ComplaintMessages
            .Where(m => m.ComplaintId == id)
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

    // POST /api/portalcomplaint/{id}/messages
    [HttpPost("{id}/messages")]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> SendTenantMessage(Guid id, [FromBody] ComplaintMessageDto dto)
    {
        var tenantId = GetUserId();
        var complaint = await _context.Complaints.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);
        if (complaint == null) return NotFound(new { success = false, message = "Complaint not found." });

        if (complaint.Status == "Closed")
            return BadRequest(new { success = false, message = "This complaint is closed. Messaging is disabled." });

        if (string.IsNullOrWhiteSpace(dto.Message))
            return BadRequest(new { success = false, message = "Message cannot be empty." });

        var message = new ComplaintMessage
        {
            ComplaintId = id,
            SenderRole = "Tenant",
            SenderUserId = tenantId,
            Message = dto.Message,
            IsReadByManagement = false,
            IsReadByTenant = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.ComplaintMessages.Add(message);
        await _context.SaveChangesAsync();

        try
        {
            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"New message from tenant on complaint {complaint.TicketNumber}: {dto.Message}",
                "property");
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of new tenant message"); }

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

    // GET /api/portalcomplaint/agent/{id}
    [HttpGet("agent/{id}")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> GetAgentComplaintDetail(Guid id)
    {
        var agentId = GetUserId();
        var complaint = await _context.Complaints
            .Include(c => c.ComplaintType)
            .Include(c => c.Attachments)
            .FirstOrDefaultAsync(c => c.Id == id && c.EscalatedToAgentId == agentId);

        if (complaint == null)
            return NotFound(new { success = false, message = "Complaint not found or not escalated to you." });

        var result = await ComplaintDetailHelper.BuildAsync(_context, complaint, "Agent");
        return Ok(new { success = true, data = result });
    }

    // POST /api/portalcomplaint/{complaintId}/attachments
    [HttpPost("{complaintId}/attachments")]
    [Authorize(Roles = "Tenant")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> UploadAttachments(Guid complaintId, [FromForm] List<IFormFile> images, [FromForm] List<IFormFile> documents)
    {
        var tenantId = GetUserId();
        var complaint = await _context.Complaints.FirstOrDefaultAsync(c => c.Id == complaintId && c.TenantId == tenantId);
        if (complaint == null)
            return NotFound(new { success = false, message = "Complaint not found." });

        images ??= new List<IFormFile>();
        documents ??= new List<IFormFile>();

        if (images.Count > 3)
            return BadRequest(new { success = false, message = "Maximum 3 images allowed." });
        if (documents.Count > 3)
            return BadRequest(new { success = false, message = "Maximum 3 documents allowed." });

        const long maxFileSize = 4 * 1024 * 1024; // 4MB
        var allFiles = images.Concat(documents).ToList();
        foreach (var file in allFiles)
        {
            if (file.Length > maxFileSize)
                return BadRequest(new { success = false, message = $"{file.FileName} exceeds the 4MB limit." });
        }

        var saveDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "complaint-attachments");
        Directory.CreateDirectory(saveDir);

        var savedAttachments = new List<ComplaintAttachment>();
        foreach (var file in allFiles)
        {
            var ext = Path.GetExtension(file.FileName);
            var fileName = $"{Guid.NewGuid()}{ext}";
            var fullPath = Path.Combine(saveDir, fileName);
            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
            savedAttachments.Add(new ComplaintAttachment
            {
                Id = Guid.NewGuid(),
                ComplaintId = complaintId,
                FilePath = $"/complaint-attachments/{fileName}",
                FileType = images.Contains(file) ? "Image" : "Document",
                FileSizeBytes = file.Length,
                UploadedAt = DateTime.UtcNow
            });
        }

        _context.ComplaintAttachments.AddRange(savedAttachments);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, attachmentCount = savedAttachments.Count });
    }

    // GET /api/portalcomplaint/agent/my-escalated
    [HttpGet("agent/my-escalated")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> GetMyEscalatedComplaints()
    {
        var agentId = GetUserId();
        var complaints = await _context.Complaints
            .Include(c => c.ComplaintType)
            .Where(c => c.EscalatedToAgentId == agentId)
            .OrderByDescending(c => c.EscalatedAt)
            .ToListAsync();

        var houseIds = complaints.Select(c => c.HouseId).Distinct().ToList();
        var houses = await _context.Houses.Where(h => houseIds.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.HouseNumber);
        var flatIds = complaints.Select(c => c.FlatId).Distinct().ToList();
        var flats = await _context.Flats.Where(f => flatIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var data = complaints.Select(c => new
        {
            c.Id,
            c.TicketNumber,
            ComplaintTypeName = c.ComplaintType.Name,
            c.Description,
            c.Status,
            c.IsBillable,
            HouseNumber = houses.GetValueOrDefault(c.HouseId, "-"),
            FlatName = flats.GetValueOrDefault(c.FlatId, "-"),
            c.EscalatedAt,
            c.EscalationNotes,
            c.AgentCompletedAt,
            c.TenantVerificationStatus,
            c.TenantRejectionReason,
            c.AgentRedoCount
        });

        return Ok(new { success = true, complaints = data });
    }

    // POST /api/portalcomplaint/{id}/agent-evidence
    [HttpPost("{id}/agent-evidence")]
    [Authorize(Roles = "Agent")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> UploadAgentEvidence(Guid id, [FromForm] List<IFormFile> images, [FromForm] List<IFormFile> documents)
    {
        var agentId = GetUserId();
        var complaint = await _context.Complaints.FirstOrDefaultAsync(c => c.Id == id && c.EscalatedToAgentId == agentId);
        if (complaint == null) return NotFound(new { success = false, message = "Complaint not found or not escalated to you." });

        images ??= new List<IFormFile>();
        documents ??= new List<IFormFile>();

        const long maxFileSize = 4 * 1024 * 1024;
        foreach (var file in images.Concat(documents))
            if (file.Length > maxFileSize)
                return BadRequest(new { success = false, message = $"{file.FileName} exceeds the 4MB limit." });

        var saveDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "complaint-attachments");
        Directory.CreateDirectory(saveDir);

        var attemptNumber = complaint.AgentRedoCount + 1;
        foreach (var file in images)
        {
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            using var stream = new FileStream(Path.Combine(saveDir, fileName), FileMode.Create);
            await file.CopyToAsync(stream);
            _context.ComplaintAttachments.Add(new ComplaintAttachment
            {
                Id = Guid.NewGuid(),
                ComplaintId = id,
                FilePath = $"/complaint-attachments/{fileName}",
                FileType = "Image",
                FileSizeBytes = file.Length,
                UploadedAt = DateTime.UtcNow,
                Stage = "AgentCompletion",
                AttemptNumber = attemptNumber
            });
        }
        foreach (var file in documents)
        {
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            using var stream = new FileStream(Path.Combine(saveDir, fileName), FileMode.Create);
            await file.CopyToAsync(stream);
            _context.ComplaintAttachments.Add(new ComplaintAttachment
            {
                Id = Guid.NewGuid(),
                ComplaintId = id,
                FilePath = $"/complaint-attachments/{fileName}",
                FileType = "Document",
                FileSizeBytes = file.Length,
                UploadedAt = DateTime.UtcNow,
                Stage = "AgentCompletion",
                AttemptNumber = attemptNumber
            });
        }
        await _context.SaveChangesAsync();
        return Ok(new { success = true });
    }

    // PATCH /api/portalcomplaint/{id}/agent-complete
    [HttpPatch("{id}/agent-complete")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> AgentComplete(Guid id, [FromBody] AgentCompleteDto dto)
    {
        var agentId = GetUserId();
        var complaint = await _context.Complaints.FirstOrDefaultAsync(c => c.Id == id && c.EscalatedToAgentId == agentId);
        if (complaint == null) return NotFound(new { success = false, message = "Complaint not found or not escalated to you." });
        if (string.IsNullOrWhiteSpace(dto.Notes)) return BadRequest(new { success = false, message = "Completion notes are required." });

        var submittedAt = DateTime.UtcNow;
        complaint.AgentCompletionNotes = dto.Notes;
        complaint.AgentCompletedAt = submittedAt;
        complaint.TenantVerificationStatus = null;
        complaint.TenantRejectionReason = null;

        _context.ComplaintWorkAttempts.Add(new ComplaintWorkAttempt
        {
            Id = Guid.NewGuid(),
            ComplaintId = complaint.Id,
            AttemptNumber = complaint.AgentRedoCount + 1,
            Notes = dto.Notes,
            SubmittedAt = submittedAt
        });

        _context.ComplaintStatusHistory.Add(new ComplaintStatusHistoryEntry
        {
            Id = Guid.NewGuid(),
            ComplaintId = complaint.Id,
            FromStatus = complaint.Status,
            ToStatus = complaint.Status,
            ChangedByAgentId = agentId,
            Notes = dto.Notes,
            ChangedAt = submittedAt
        });
        await _context.SaveChangesAsync();

        try { await _notificationService.SendToUserAsync(complaint.TenantId.ToString(), $"Please review the completed work for complaint {complaint.TicketNumber}.", "property"); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify tenant of agent completion"); }

        return Ok(new { success = true, message = "Marked as completed. Awaiting tenant verification." });
    }

    // PATCH /api/portalcomplaint/{id}/tenant-verify
    [HttpPatch("{id}/tenant-verify")]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> TenantVerify(Guid id, [FromBody] TenantVerifyDto dto)
    {
        var tenantId = GetUserId();
        var complaint = await _context.Complaints.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);
        if (complaint == null) return NotFound(new { success = false, message = "Complaint not found." });
        if (complaint.AgentCompletedAt == null) return BadRequest(new { success = false, message = "No completed work to verify yet." });

        var currentAttemptNumber = complaint.AgentRedoCount + 1;
        var workAttempt = await _context.ComplaintWorkAttempts
            .FirstOrDefaultAsync(w => w.ComplaintId == complaint.Id && w.AttemptNumber == currentAttemptNumber);

        if (dto.Verified)
        {
            var verdictAt = DateTime.UtcNow;
            complaint.TenantVerificationStatus = "Verified";
            complaint.TenantCompletedAt = verdictAt;

            if (workAttempt != null)
            {
                workAttempt.TenantVerdict = "Verified";
                workAttempt.TenantVerdictAt = verdictAt;
            }

            _context.ComplaintStatusHistory.Add(new ComplaintStatusHistoryEntry
            {
                Id = Guid.NewGuid(),
                ComplaintId = complaint.Id,
                FromStatus = complaint.Status,
                ToStatus = complaint.Status,
                ChangedByTenantId = tenantId,
                Notes = "Tenant verified agent's completed work.",
                ChangedAt = verdictAt
            });
            await _context.SaveChangesAsync();

            try
            {
                await _notificationService.SendToRolesAsync(
                    new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                    $"Complaint {complaint.TicketNumber} verified by tenant — ready for final close.",
                    "property");
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of tenant verification"); }

            return Ok(new { success = true, message = "Verified. Management will finalize closure." });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(dto.RejectionReason))
                return BadRequest(new { success = false, message = "A rejection reason is required." });

            var verdictAt = DateTime.UtcNow;

            if (workAttempt != null)
            {
                workAttempt.TenantVerdict = "Rejected";
                workAttempt.TenantVerdictReason = dto.RejectionReason;
                workAttempt.TenantVerdictAt = verdictAt;
            }

            complaint.TenantVerificationStatus = "Rejected";
            complaint.TenantRejectionReason = dto.RejectionReason;
            complaint.AgentRedoCount += 1;

            _context.ComplaintStatusHistory.Add(new ComplaintStatusHistoryEntry
            {
                Id = Guid.NewGuid(),
                ComplaintId = complaint.Id,
                FromStatus = complaint.Status,
                ToStatus = complaint.Status,
                ChangedByTenantId = tenantId,
                Notes = $"Rejected: {dto.RejectionReason}",
                ChangedAt = verdictAt
            });
            await _context.SaveChangesAsync();

            if (complaint.EscalatedToAgentId.HasValue)
            {
                try { await _notificationService.SendToUserAsync(complaint.EscalatedToAgentId.Value.ToString(), $"Complaint {complaint.TicketNumber} was rejected by the tenant: {dto.RejectionReason}. Please redo.", "property"); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to notify agent of rejection"); }
            }

            return Ok(new { success = true, message = "Rejected. Agent has been notified to redo the work." });
        }
    }

    // PATCH /api/portalcomplaint/landlord/{id}/final-approval
    [HttpPatch("landlord/{id}/final-approval")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> LandlordFinalApproval(Guid id, [FromBody] LandlordApprovalDto dto)
    {
        var landlordId = GetUserId();
        var complaint = await _context.Complaints.FirstOrDefaultAsync(c => c.Id == id && c.LandlordId == landlordId);
        if (complaint == null) return NotFound(new { success = false, message = "Complaint not found." });
        if (complaint.BillableTarget != "Management")
            return BadRequest(new { success = false, message = "This complaint does not require your approval." });
        if (complaint.CurrentApprovalStepOrder != null)
            return BadRequest(new { success = false, message = "Internal approval is still in progress." });
        var existingDecision = await _context.ComplaintLandlordDecisions
            .FirstOrDefaultAsync(d => d.ComplaintId == complaint.Id && d.ApprovalAttemptNumber == complaint.ApprovalAttemptNumber);
        if (existingDecision != null)
            return BadRequest(new { success = false, message = "You have already actioned this approval attempt." });

        var decidedAt = DateTime.UtcNow;
        complaint.LandlordActionedAt = decidedAt;
        complaint.LandlordDecision = dto.Approved ? "Approved" : "Rejected";
        complaint.LandlordDecisionNotes = dto.Notes;
        complaint.FinalDecision = dto.Approved ? "Approved" : "Rejected";
        complaint.FinalDecisionAt = decidedAt;

        _context.ComplaintLandlordDecisions.Add(new ComplaintLandlordDecision
        {
            Id = Guid.NewGuid(),
            ComplaintId = complaint.Id,
            ApprovalAttemptNumber = complaint.ApprovalAttemptNumber,
            Decision = dto.Approved ? "Approved" : "Rejected",
            Notes = dto.Notes,
            DecidedAt = decidedAt,
            DecidedByLandlordId = landlordId
        });

        if (dto.Approved)
        {
            if (complaint.BillableAmount == null || complaint.BillableAmount <= 0)
                return BadRequest(new { success = false, message = "This complaint has no valid billable amount to deduct." });

            _context.Deductions.Add(new Deduction
            {
                Id = Guid.NewGuid(),
                LandlordId = complaint.LandlordId,
                TenantId = complaint.TenantId,
                HouseId = complaint.HouseId,
                FlatId = complaint.FlatId,
                ComplaintId = complaint.Id,
                Amount = complaint.BillableAmount.Value,
                Description = $"Complaint {complaint.TicketNumber} — {complaint.BillableExplanation}",
                DeductionMonth = decidedAt.Month,
                DeductionYear = decidedAt.Year,
                CreatedAt = DateTime.UtcNow
            });

            var landlordForEmail = await _context.Landlords.FirstOrDefaultAsync(l => l.Id == landlordId);
            try
            {
                if (landlordForEmail != null)
                    await _emailService.SendDeductionCreatedEmailAsync(landlordForEmail.Email, landlordForEmail.FirstName, complaint.TicketNumber, complaint.BillableAmount.Value, complaint.BillableExplanation);
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send deduction email to landlord"); }
            try { await _notificationService.SendToUserAsync(complaint.LandlordId.ToString(), $"A deduction of KES {complaint.BillableAmount.Value:N2} has been created on complaint {complaint.TicketNumber}.", "property"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify landlord of deduction"); }
        }
        else
        {
            complaint.NeedsResubmission = true;
            complaint.Status = "Rejected";
        }

        await _context.SaveChangesAsync();

        try
        {
            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"Landlord {(dto.Approved ? "approved" : "rejected")} complaint {complaint.TicketNumber}.",
                "property");
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of landlord's final decision"); }

        try
        {
            var superAdmins = await _context.SuperAdmins.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var adminUsers = await _context.AdminUsers.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var managers = await _context.Managers.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var secretaries = await _context.Secretaries.Select(u => new { u.Email, u.FirstName }).ToListAsync();
            var managementUsers = superAdmins.Concat(adminUsers).Concat(managers).Concat(secretaries).ToList();

            foreach (var mgr in managementUsers)
            {
                try { await _emailService.SendLandlordDecisionEmailAsync(mgr.Email, mgr.FirstName, complaint.TicketNumber, complaint.LandlordDecision!, complaint.LandlordDecisionNotes, complaint.BillableAmount); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send landlord-decision email to {Email}", mgr.Email); }
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to process landlord-decision management emails"); }

        return Ok(new { success = true, message = dto.Approved ? "Approved. Deduction recorded." : "Rejected." });
    }

    // GET /api/portalcomplaint/landlord/my-deductions
    [HttpGet("landlord/my-deductions")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetMyDeductions([FromQuery] int? month = null, [FromQuery] int? year = null, [FromQuery] DateTime? fromDate = null, [FromQuery] DateTime? toDate = null)
    {
        var landlordId = GetUserId();
        var query = _context.Deductions.Where(d => d.LandlordId == landlordId);
        if (month.HasValue) query = query.Where(d => d.DeductionMonth == month.Value);
        if (year.HasValue) query = query.Where(d => d.DeductionYear == year.Value);
        if (fromDate.HasValue) query = query.Where(d => d.CreatedAt >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(d => d.CreatedAt.Date <= toDate.Value.Date);

        var deductions = await query.OrderByDescending(d => d.CreatedAt).ToListAsync();

        var tenantIds = deductions.Select(d => d.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants.Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");
        var houseIds = deductions.Select(d => d.HouseId).Distinct().ToList();
        var houses = await _context.Houses.Where(h => houseIds.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.HouseNumber);
        var complaintIds = deductions.Select(d => d.ComplaintId).Distinct().ToList();
        var complaints = await _context.Complaints.Where(c => complaintIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.TicketNumber);

        var data = deductions.Select(d => new
        {
            d.Id,
            d.ComplaintId,
            TenantName = tenants.GetValueOrDefault(d.TenantId, "-"),
            HouseNumber = houses.GetValueOrDefault(d.HouseId, "-"),
            TicketNumber = complaints.GetValueOrDefault(d.ComplaintId, "-"),
            d.Amount,
            d.Description,
            d.DeductionMonth,
            d.DeductionYear,
            d.CreatedAt
        });

        return Ok(new { success = true, deductions = data, totalAmount = deductions.Sum(d => d.Amount) });
    }

    // GET /api/portalcomplaint/my-approval-queue
    [HttpGet("my-approval-queue")]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> GetMyApprovalQueue()
    {
        var tenantId = GetUserId();
        if (tenantId == Guid.Empty)
            return Unauthorized(new { success = false, message = "Invalid token." });

        var complaints = await _context.Complaints
            .Include(c => c.ComplaintType)
            .Include(c => c.Attachments)
            .Where(c => c.TenantId == tenantId
                     && c.AgentCompletedAt != null
                     && c.TenantVerificationStatus == null
                     && !c.IsDeleted)
            .OrderByDescending(c => c.AgentCompletedAt)
            .ToListAsync();

        var result = complaints.Select(c => new
        {
            id = c.Id,
            ticketNumber = c.TicketNumber,
            complaintTypeId = c.ComplaintTypeId,
            complaintTypeName = c.ComplaintType?.Name,
            description = c.Description,
            status = c.Status,
            createdAt = c.CreatedAt,
            agentCompletedAt = c.AgentCompletedAt,
            agentCompletionNotes = c.AgentCompletionNotes,
            attachments = c.Attachments
                .Where(a => a.Stage == "AgentCompletion")
                .Select(a => new { a.FilePath, a.FileType, a.FileSizeBytes, a.UploadedAt })
        }).ToList();

        return Ok(new { success = true, complaints = result });
    }

    // GET /api/portalcomplaint/landlord/my-approval-queue
    [HttpGet("landlord/my-approval-queue")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordApprovalQueue()
    {
        var landlordId = GetUserId();
        var complaints = await _context.Complaints
            .Include(c => c.ComplaintType)
            .Where(c => c.LandlordId == landlordId && c.BillableTarget == "Management" && c.CurrentApprovalStepOrder == null && string.IsNullOrEmpty(c.LandlordDecision))
            .OrderBy(c => c.ReviewedAt)
            .ToListAsync();

        var data = new List<object>();
        foreach (var c in complaints)
            data.Add(await ComplaintDetailHelper.BuildAsync(_context, c, "Landlord"));

        return Ok(new { success = true, complaints = data });
    }

    // GET /api/portalcomplaint/landlord/my-approval-history
    [HttpGet("landlord/my-approval-history")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordApprovalHistory()
    {
        var landlordIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(landlordIdStr, out var landlordId))
            return Unauthorized();

        var decisions = await _context.ComplaintLandlordDecisions
            .Where(d => d.DecidedByLandlordId == landlordId)
            .OrderByDescending(d => d.DecidedAt)
            .ToListAsync();

        var complaintIds = decisions.Select(d => d.ComplaintId).Distinct().ToList();
        var complaints = await _context.Complaints
            .Include(c => c.ComplaintType)
            .Where(c => complaintIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c);

        var tenantIds = complaints.Values.Select(c => c.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

        var data = decisions.Select(d =>
        {
            complaints.TryGetValue(d.ComplaintId, out var c);
            return new
            {
                d.Id,
                complaintId = d.ComplaintId,
                ticketNumber = c?.TicketNumber,
                complaintTypeName = c?.ComplaintType?.Name,
                tenantName = c != null && tenants.TryGetValue(c.TenantId, out var tn) ? tn : null,
                d.ApprovalAttemptNumber,
                decision = d.Decision,
                notes = d.Notes,
                decidedAt = d.DecidedAt
            };
        }).ToList();

        return Ok(new { success = true, history = data });
    }

    // GET /api/portalcomplaint/my-verification-history
    [HttpGet("my-verification-history")]
    [Authorize(Roles = "Tenant")]
    public async Task<IActionResult> GetMyVerificationHistory()
    {
        var tenantId = GetUserId();
        if (tenantId == Guid.Empty)
            return Unauthorized(new { success = false, message = "Invalid token." });

        var data = await (
            from w in _context.ComplaintWorkAttempts
            join c in _context.Complaints on w.ComplaintId equals c.Id
            where c.TenantId == tenantId
                  && (w.TenantVerdict == "Verified" || w.TenantVerdict == "Rejected")
            orderby w.TenantVerdictAt descending
            select new
            {
                w.Id,
                complaintId = c.Id,
                ticketNumber = c.TicketNumber,
                complaintTypeName = c.ComplaintType.Name,
                attemptNumber = w.AttemptNumber,
                verdict = w.TenantVerdict,
                verdictReason = w.TenantVerdictReason,
                verdictAt = w.TenantVerdictAt
            }
        ).ToListAsync();

        return Ok(new { success = true, history = data });
    }
}

public class CreateComplaintDto
{
    public Guid ComplaintTypeId { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class AgentCompleteDto
{
    public string? Notes { get; set; }
}

public class TenantVerifyDto
{
    public bool Verified { get; set; }
    public string? RejectionReason { get; set; }
}

public class LandlordApprovalDto { public bool Approved { get; set; } public string? Notes { get; set; } }
