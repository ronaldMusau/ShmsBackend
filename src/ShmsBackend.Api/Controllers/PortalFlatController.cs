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
            var flats = await _context.AgentFlats
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
                    TotalHouses = af.Flat.Houses.Count,
                    VacantHouses = af.Flat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Vacant),
                    OccupiedHouses = af.Flat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Occupied),
                    Houses = af.Flat.Houses.Select(h => new
                    {
                        h.Id,
                        h.HouseNumber,
                        HouseType = h.HouseType.ToString(),
                        OccupancyStatus = h.OccupancyStatus.ToString(),
                        h.CreatedAt
                    }),
                    af.Flat.CreatedAt
                })
                .ToListAsync();

            return Ok(new { success = true, data = flats });
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
                h.CreatedAt
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
                TotalHouses = flat.Houses.Count,
                VacantHouses = flat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Vacant),
                OccupiedHouses = flat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Occupied),
                Houses = flat.Houses.Select(h => new
                {
                    h.Id,
                    h.HouseNumber,
                    HouseType = h.HouseType.ToString(),
                    OccupancyStatus = h.OccupancyStatus.ToString(),
                    h.CreatedAt
                }),
                flat.CreatedAt
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
                h.CreatedAt
            }),
            result.CreatedAt
        }});
    }
}
