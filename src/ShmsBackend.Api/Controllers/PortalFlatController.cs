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

        var flats = await _context.Flats
            .Include(f => f.Houses)
            .ToListAsync();

        if (role == "Agent")
        {
            return Ok(flats.Select(flat => new
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
                    // NOTE: No RentFee, DepositFee, or PaymentStatus — agents cannot see financials
                }),
                flat.CreatedAt
            }));
        }

        return Ok(flats.Select(flat => new
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
        }));
    }

    // GET /api/portalflats/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var role = GetUserRole();

        var flat = await _context.Flats
            .Include(f => f.Houses)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (flat == null) return NotFound();

        if (role == "Agent")
        {
            return Ok(new
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
            });
        }

        return Ok(new
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
        });
    }
}
