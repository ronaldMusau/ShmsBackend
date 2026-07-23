using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/portalflats")]
[Authorize]
public class PortalFlatController : ControllerBase
{
    private readonly ShmsDbContext _context;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<PortalFlatController> _logger;

    public PortalFlatController(
        ShmsDbContext context,
        IEmailService emailService,
        INotificationService notificationService,
        ILogger<PortalFlatController> logger)
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

    private string GetUserRole()
    {
        return User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
    }

    // GET /api/portalflats
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var role = GetUserRole();

        if (role == "Agent")
        {
            var agentId = GetUserId();
            var agentFlats = await _context.AgentFlats
                .Include(af => af.Flat)
                    .ThenInclude(f => f.Houses)
                        .ThenInclude(h => h.HouseTypeRef)
                .Where(af => af.AgentId == agentId)
                .AsSplitQuery()
                .Select(af => new
                {
                    af.Flat.Id,
                    af.Flat.FlatName,
                    af.Flat.County,
                    af.Flat.Constituency,
                    af.Flat.Ward,
                    af.Flat.LandlordId,
                    HouseCount = af.Flat.Houses.Count,
                    VacantCount = af.Flat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Vacant),
                    OccupiedCount = af.Flat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Occupied),
                    Houses = af.Flat.Houses.Select(h => new
                    {
                        h.Id,
                        h.HouseNumber,
                        HouseTypeName = h.HouseTypeRef != null ? h.HouseTypeRef.Name : null,
                        h.RentFee,
                        h.DepositFee,
                        OccupancyStatus = h.OccupancyStatus.ToString(),
                        PaymentStatus = h.PaymentStatus.ToString(),
                        Images = h.Images.OrderBy(i => i.SortOrder).Select(i => new { i.Id, i.ImagePath }).ToList()
                    }).ToList()
                })
                .ToListAsync();

            return Ok(new { success = true, data = agentFlats });
        }

        if (User.IsInRole("Landlord"))
        {
            var landlordIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(landlordIdStr, out var landlordId))
                return Unauthorized();

            var landlordFlats = await _context.Flats
                .Include(f => f.Houses)
                    .ThenInclude(h => h.HouseTypeRef)
                .Where(f => f.LandlordId == landlordId)
                .AsSplitQuery()
                .Select(f => new
                {
                    f.Id,
                    f.FlatName,
                    f.County,
                    f.Constituency,
                    f.Ward,
                    f.LandlordId,
                    HouseCount = f.Houses.Count,
                    VacantCount = f.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Vacant),
                    OccupiedCount = f.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Occupied),
                    Houses = f.Houses.Select(h => new
                    {
                        h.Id,
                        h.HouseNumber,
                        HouseTypeName = h.HouseTypeRef != null ? h.HouseTypeRef.Name : null,
                        h.RentFee,
                        h.DepositFee,
                        OccupancyStatus = h.OccupancyStatus.ToString(),
                        PaymentStatus = h.PaymentStatus.ToString(),
                        Images = h.Images.OrderBy(i => i.SortOrder).Select(i => new { i.Id, i.ImagePath }).ToList()
                    }).ToList()
                })
                .ToListAsync();

            return Ok(new { success = true, data = landlordFlats });
        }

        var allFlats = await _context.Flats
            .Include(f => f.Houses)
                .ThenInclude(h => h.Images)
            .Include(f => f.Houses)
                .ThenInclude(h => h.HouseTypeRef)
            .AsSplitQuery()
            .ToListAsync();

        return Ok(new { success = true, data = allFlats.Select(flat => new
        {
            flat.Id,
            flat.FlatName,
            flat.County,
            flat.Constituency,
            flat.Ward,
            flat.LandlordId,
            TotalHouses = flat.Houses.Count,
            VacantHouses = flat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Vacant),
            OccupiedHouses = flat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Occupied),
            Houses = flat.Houses.Select(h => new
            {
                h.Id,
                h.HouseNumber,
                HouseTypeName = h.HouseTypeRef != null ? h.HouseTypeRef.Name : null,
                h.RentFee,
                h.DepositFee,
                OccupancyStatus = h.OccupancyStatus.ToString(),
                PaymentStatus = h.PaymentStatus.ToString(),
                h.CreatedAt,
                Images = h.Images.OrderBy(i => i.SortOrder).Select(i => new { i.Id, i.ImagePath }).ToList()
            }),
            flat.CreatedAt
        })});
    }

    // GET /api/portalflats/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var role = GetUserRole();

        if (role == "Agent")
        {
            var agentId = GetUserId();
            var agentFlat = await _context.AgentFlats
                .Include(af => af.Flat)
                    .ThenInclude(f => f.Houses)
                        .ThenInclude(h => h.HouseTypeRef)
                .Include(af => af.Flat)
                    .ThenInclude(f => f.Houses)
                        .ThenInclude(h => h.Images)
                .FirstOrDefaultAsync(af => af.AgentId == agentId && af.FlatId == id);

            if (agentFlat == null)
                return NotFound(new { success = false, message = "Flat not found or not assigned to you." });

            var flat = agentFlat.Flat;
            return Ok(new { success = true, data = new
            {
                flat.Id,
                flat.FlatName,
                flat.County,
                flat.Constituency,
                flat.Ward,
                flat.LandlordId,
                HouseCount = flat.Houses.Count,
                VacantCount = flat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Vacant),
                OccupiedCount = flat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Occupied),
                Houses = flat.Houses.Select(h => new
                {
                    h.Id,
                    h.HouseNumber,
                    HouseTypeName = h.HouseTypeRef != null ? h.HouseTypeRef.Name : null,
                    h.RentFee,
                    h.DepositFee,
                    OccupancyStatus = h.OccupancyStatus.ToString(),
                    PaymentStatus = h.PaymentStatus.ToString(),
                    Images = h.Images.OrderBy(i => i.SortOrder).Select(i => new { i.Id, i.ImagePath }).ToList()
                }).ToList(),
                flat.CreatedAt
            }});
        }

        if (User.IsInRole("Landlord"))
        {
            var landlordIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(landlordIdStr, out var landlordId))
                return Unauthorized();

            var landlordFlat = await _context.Flats
                .Include(f => f.Houses)
                    .ThenInclude(h => h.Tenants)
                .Include(f => f.Houses)
                    .ThenInclude(h => h.Images)
                .Include(f => f.Houses)
                    .ThenInclude(h => h.HouseTypeRef)
                .FirstOrDefaultAsync(f => f.Id == id && f.LandlordId == landlordId);

            if (landlordFlat == null)
                return NotFound(new { success = false, message = "Flat not found or not owned by you." });

            return Ok(new { success = true, data = new
            {
                landlordFlat.Id,
                landlordFlat.FlatName,
                landlordFlat.County,
                landlordFlat.Constituency,
                landlordFlat.Ward,
                landlordFlat.LandlordId,
                HouseCount = landlordFlat.Houses.Count,
                VacantCount = landlordFlat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Vacant),
                OccupiedCount = landlordFlat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Occupied),
                TotalHouses = landlordFlat.Houses.Count,
                VacantHouses = landlordFlat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Vacant),
                OccupiedHouses = landlordFlat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Occupied),
                Houses = landlordFlat.Houses.Select(h => new
                {
                    h.Id,
                    h.HouseNumber,
                    HouseTypeName = h.HouseTypeRef != null ? h.HouseTypeRef.Name : null,
                    h.RentFee,
                    h.DepositFee,
                    OccupancyStatus = h.OccupancyStatus.ToString(),
                    PaymentStatus = h.PaymentStatus.ToString(),
                    h.CreatedAt,
                    CurrentTenant = h.Tenants.Select(t => new
                    {
                        t.Id,
                        t.FirstName,
                        t.LastName,
                        t.PhoneNumber,
                        t.Email,
                        t.CreatedAt
                    }).FirstOrDefault(),
                    Images = h.Images.OrderBy(i => i.SortOrder).Select(i => new { i.Id, i.ImagePath }).ToList()
                }),
                landlordFlat.CreatedAt
            }});
        }

        var result = await _context.Flats
            .Include(f => f.Houses)
                .ThenInclude(h => h.HouseTypeRef)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (result == null) return NotFound(new { success = false, message = "Flat not found." });

        return Ok(new { success = true, data = new
        {
            result.Id,
            result.FlatName,
            result.County,
            result.Constituency,
            result.Ward,
            result.LandlordId,
            TotalHouses = result.Houses.Count,
            VacantHouses = result.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Vacant),
            OccupiedHouses = result.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Occupied),
            Houses = result.Houses.Select(h => new
            {
                h.Id,
                h.HouseNumber,
                HouseTypeName = h.HouseTypeRef != null ? h.HouseTypeRef.Name : null,
                h.RentFee,
                h.DepositFee,
                OccupancyStatus = h.OccupancyStatus.ToString(),
                PaymentStatus = h.PaymentStatus.ToString(),
                h.CreatedAt,
                Images = h.Images.OrderBy(i => i.SortOrder).Select(i => new { i.Id, i.ImagePath }).ToList()
            }),
            result.CreatedAt
        }});
    }

    // PATCH /api/portalflats/edit-request/{id}/final-approval
    [HttpPatch("edit-request/{id:guid}/final-approval")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> LandlordFinalEditApproval(Guid id, [FromBody] LandlordApprovalDto dto)
    {
        var landlordId = GetUserId();
        var request = await _context.FlatEditRequests.Include(r => r.Flat).FirstOrDefaultAsync(r => r.Id == id);
        if (request == null) return NotFound(new { success = false, message = "Edit request not found." });
        if (request.Flat!.LandlordId != landlordId) return Forbid();
        if (request.CurrentApprovalStepOrder != null) return BadRequest(new { success = false, message = "Internal approval is still in progress." });
        if (request.Status != "Pending") return BadRequest(new { success = false, message = "This request has already been actioned." });

        request.LandlordDecision = dto.Approved ? "Approved" : "Rejected";
        request.LandlordDecisionNotes = dto.Notes;
        request.LandlordActionedAt = DateTime.UtcNow;
        request.Status = dto.Approved ? "Approved" : "Rejected";
        request.UpdatedAt = DateTime.UtcNow;

        if (dto.Approved)
        {
            var flat = request.Flat;
            if (request.ProposedFlatName != null) flat.FlatName = request.ProposedFlatName;
            if (request.ProposedCounty != null) flat.County = request.ProposedCounty;
            if (request.ProposedConstituency != null) flat.Constituency = request.ProposedConstituency;
            if (request.ProposedWard != null) flat.Ward = request.ProposedWard;
            if (request.ProposedRentDueDay.HasValue) flat.RentDueDay = request.ProposedRentDueDay.Value;
            if (request.ProposedBillableGracePeriodMonths.HasValue) flat.BillableGracePeriodMonths = request.ProposedBillableGracePeriodMonths.Value;
            if (request.ProposedGoogleMapsLink != null) flat.GoogleMapsLink = request.ProposedGoogleMapsLink;
            flat.UpdatedAt = DateTime.UtcNow;

            if (request.ClearAgent)
            {
                var links = await _context.AgentFlats.Where(af => af.FlatId == flat.Id).ToListAsync();
                _context.AgentFlats.RemoveRange(links);
            }
            else if (request.ProposedAgentId.HasValue)
            {
                var links = await _context.AgentFlats.Where(af => af.FlatId == flat.Id).ToListAsync();
                _context.AgentFlats.RemoveRange(links);
                _context.AgentFlats.Add(new AgentFlat
                {
                    AgentId = request.ProposedAgentId.Value,
                    FlatId = flat.Id,
                    AssignedAt = DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync();

        var requester = await _context.PortalUsers.FirstOrDefaultAsync(u => u.Id == request.RequestedByUserId);
        if (requester != null)
        {
            try { await _notificationService.SendToUserAsync(requester.Id.ToString(), $"The landlord {(dto.Approved ? "approved" : "rejected")} your edit for \"{request.Flat.FlatName}\".", "property"); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to notify requester of landlord's flat edit decision"); }
            try { await _emailService.SendLandlordDecisionEmailAsync(requester.Email, requester.FirstName, $"FLAT-{request.Flat.FlatName}", request.LandlordDecision!, request.LandlordDecisionNotes, null); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to send flat edit landlord-decision email"); }
        }

        return Ok(new { success = true, message = dto.Approved ? "Approved. Flat updated." : "Rejected." });
    }

    // GET /api/portalflats/edit-request/my-queue
    [HttpGet("edit-request/my-queue")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordEditRequestQueue()
    {
        var landlordId = GetUserId();
        var requests = await _context.FlatEditRequests
            .Include(r => r.Flat)
            .Where(r => r.Flat!.LandlordId == landlordId && r.CurrentApprovalStepOrder == null && r.Status == "Pending")
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        var data = requests.Select(r => new
        {
            r.Id,
            r.FlatId,
            FlatName = r.Flat!.FlatName,
            r.ProposedFlatName,
            r.ProposedCounty,
            r.ProposedConstituency,
            r.ProposedWard,
            r.ProposedRentDueDay,
            r.ProposedBillableGracePeriodMonths,
            r.ProposedGoogleMapsLink,
            r.CreatedAt
        });

        return Ok(new { success = true, requests = data });
    }
}
