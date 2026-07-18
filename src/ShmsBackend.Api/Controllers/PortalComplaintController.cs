using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using System.Security.Claims;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/portalcomplaint")]
[Authorize(Roles = "Tenant")]
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
}

public class CreateComplaintDto
{
    public Guid ComplaintTypeId { get; set; }
    public string Description { get; set; } = string.Empty;
}
