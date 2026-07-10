using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/portalflats")]
[Authorize]
public class PortalFlatController : ControllerBase
{
    private readonly ShmsDbContext _context;

    public PortalFlatController(ShmsDbContext context)
    {
        _context = context;
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
                .Where(af => af.AgentId == agentId)
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
                        HouseType = h.HouseType.ToString(),
                        h.RentFee,
                        h.DepositFee,
                        OccupancyStatus = h.OccupancyStatus.ToString(),
                        PaymentStatus = h.PaymentStatus.ToString(),
                        ImagePaths = h.Images.OrderBy(i => i.SortOrder).Select(i => i.ImagePath).ToList()
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
                .Where(f => f.LandlordId == landlordId)
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
                        HouseType = h.HouseType.ToString(),
                        h.RentFee,
                        h.DepositFee,
                        OccupancyStatus = h.OccupancyStatus.ToString(),
                        PaymentStatus = h.PaymentStatus.ToString(),
                        ImagePaths = h.Images.OrderBy(i => i.SortOrder).Select(i => i.ImagePath).ToList()
                    }).ToList()
                })
                .ToListAsync();

            return Ok(new { success = true, data = landlordFlats });
        }

        var allFlats = await _context.Flats
            .Include(f => f.Houses)
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
                HouseType = h.HouseType.ToString(),
                h.RentFee,
                h.DepositFee,
                OccupancyStatus = h.OccupancyStatus.ToString(),
                PaymentStatus = h.PaymentStatus.ToString(),
                h.CreatedAt,
                ImagePaths = h.Images.OrderBy(i => i.SortOrder).Select(i => i.ImagePath).ToList()
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
                    HouseType = h.HouseType.ToString(),
                    h.RentFee,
                    h.DepositFee,
                    OccupancyStatus = h.OccupancyStatus.ToString(),
                    PaymentStatus = h.PaymentStatus.ToString(),
                    ImagePaths = h.Images.OrderBy(i => i.SortOrder).Select(i => i.ImagePath).ToList()
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
                    HouseType = h.HouseType.ToString(),
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
                    ImagePaths = h.Images.OrderBy(i => i.SortOrder).Select(i => i.ImagePath).ToList()
                }),
                landlordFlat.CreatedAt
            }});
        }

        var result = await _context.Flats
            .Include(f => f.Houses)
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
                HouseType = h.HouseType.ToString(),
                h.RentFee,
                h.DepositFee,
                OccupancyStatus = h.OccupancyStatus.ToString(),
                PaymentStatus = h.PaymentStatus.ToString(),
                h.CreatedAt,
                ImagePaths = h.Images.OrderBy(i => i.SortOrder).Select(i => i.ImagePath).ToList()
            }),
            result.CreatedAt
        }});
    }
}
