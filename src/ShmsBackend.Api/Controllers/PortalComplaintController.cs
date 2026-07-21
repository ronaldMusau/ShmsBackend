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
            .Select(c => new
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
                attachments = c.Attachments.Select(a => new
                {
                    a.FilePath,
                    a.FileType,
                    a.FileSizeBytes,
                    a.UploadedAt
                })
            })
            .ToListAsync();

        return Ok(new { success = true, complaints });
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

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == complaint.HouseId);
        var flat = await _context.Flats.FirstOrDefaultAsync(f => f.Id == complaint.FlatId);
        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == complaint.TenantId);
        var agent = complaint.EscalatedToAgentId.HasValue
            ? await _context.Agents.FirstOrDefaultAsync(a => a.Id == complaint.EscalatedToAgentId.Value)
            : null;

        var closeHistoryEntry = await _context.ComplaintStatusHistory
            .Where(h => h.ComplaintId == complaint.Id && h.ToStatus == "Closed")
            .OrderByDescending(h => h.ChangedAt)
            .FirstOrDefaultAsync();

        return Ok(new
        {
            success = true,
            data = new
            {
                complaint.Id,
                complaint.TicketNumber,
                complaint.Description,
                complaint.Status,
                complaint.CreatedAt,
                ComplaintTypeName = complaint.ComplaintType.Name,
                TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}" : "-",
                HouseNumber = house != null ? house.HouseNumber : "-",
                FlatName = flat != null ? flat.FlatName : "-",
                complaint.IsBillable,
                complaint.BillableTarget,
                complaint.BillableTargetOverrideReason,
                complaint.ReviewedAt,
                complaint.EscalatedAt,
                complaint.EscalationNotes,
                AgentName = agent != null ? $"{agent.FirstName} {agent.LastName}" : null,
                complaint.AgentCompletionNotes,
                complaint.AgentCompletedAt,
                complaint.TenantVerificationStatus,
                complaint.TenantRejectionReason,
                complaint.TenantCompletedAt,
                complaint.AgentRedoCount,
                complaint.ClosedAt,
                ClosingComment = closeHistoryEntry?.Notes,
                Attachments = complaint.Attachments.Select(a => new
                {
                    a.FilePath,
                    a.FileType,
                    a.FileSizeBytes,
                    a.UploadedAt,
                    a.Stage
                })
            }
        });
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
                Stage = "AgentCompletion"
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
                Stage = "AgentCompletion"
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

        complaint.AgentCompletionNotes = dto.Notes;
        complaint.AgentCompletedAt = DateTime.UtcNow;
        complaint.TenantVerificationStatus = null;
        complaint.TenantRejectionReason = null;

        _context.ComplaintStatusHistory.Add(new ComplaintStatusHistoryEntry
        {
            Id = Guid.NewGuid(),
            ComplaintId = complaint.Id,
            FromStatus = complaint.Status,
            ToStatus = complaint.Status,
            ChangedByAgentId = agentId,
            Notes = dto.Notes,
            ChangedAt = DateTime.UtcNow
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

        if (dto.Verified)
        {
            complaint.TenantVerificationStatus = "Verified";
            complaint.TenantCompletedAt = DateTime.UtcNow;

            _context.ComplaintStatusHistory.Add(new ComplaintStatusHistoryEntry
            {
                Id = Guid.NewGuid(),
                ComplaintId = complaint.Id,
                FromStatus = complaint.Status,
                ToStatus = complaint.Status,
                ChangedByTenantId = tenantId,
                Notes = "Tenant verified agent's completed work.",
                ChangedAt = DateTime.UtcNow
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
                ChangedAt = DateTime.UtcNow
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
