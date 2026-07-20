using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ComplaintController : ControllerBase
{
    private readonly ShmsDbContext _context;

    public ComplaintController(ShmsDbContext context)
    {
        _context = context;
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
            c.CreatedAt
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

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == complaint.HouseId);
        var flat = await _context.Flats.FirstOrDefaultAsync(f => f.Id == complaint.FlatId);
        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == complaint.TenantId);
        var landlord = await _context.Landlords.FirstOrDefaultAsync(l => l.Id == complaint.LandlordId);
        var agent = complaint.EscalatedToAgentId.HasValue
            ? await _context.Agents.FirstOrDefaultAsync(a => a.Id == complaint.EscalatedToAgentId.Value)
            : null;

        return Ok(new
        {
            success = true,
            data = new
            {
                complaint.Id,
                complaint.TicketNumber,
                complaint.Description,
                complaint.Status,
                complaint.IsBillable,
                complaint.BillableTarget,
                complaint.BillableTargetOverrideReason,
                complaint.BillableAmount,
                complaint.BillableExplanation,
                complaint.CreatedAt,
                ComplaintTypeName = complaint.ComplaintType.Name,
                HouseNumber = house != null ? house.HouseNumber : "-",
                FlatName = flat != null ? flat.FlatName : "-",
                BillableGracePeriodMonths = flat != null ? flat.BillableGracePeriodMonths : 3,
                TenantName = tenant != null ? $"{tenant.FirstName} {tenant.LastName}" : "-",
                LandlordName = landlord != null ? $"{landlord.FirstName} {landlord.LastName}" : "-",
                AgentName = agent != null ? $"{agent.FirstName} {agent.LastName}" : null,
                Attachments = complaint.Attachments.Select(a => new
                {
                    a.FilePath,
                    a.FileType,
                    a.FileSizeBytes,
                    a.UploadedAt
                })
            }
        });
    }
}
