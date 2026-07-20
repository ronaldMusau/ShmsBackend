using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using System.IO;
using System.Security.Claims;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/portalcomplaint")]
public class PortalComplaintController : ControllerBase
{
    private readonly ShmsDbContext _context;

    public PortalComplaintController(ShmsDbContext context)
    {
        _context = context;
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

        var typeExists = await _context.ComplaintTypes.AnyAsync(t => t.Id == dto.ComplaintTypeId && t.IsActive);
        if (!typeExists)
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
                createdAt = c.CreatedAt
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
                tenantLastName = t.LastName
            }
        ).ToListAsync();

        return Ok(new { success = true, complaints });
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
}

public class CreateComplaintDto
{
    public Guid ComplaintTypeId { get; set; }
    public string Description { get; set; } = string.Empty;
}
