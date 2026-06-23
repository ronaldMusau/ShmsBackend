using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/portaltenant")]
[Authorize(Roles = "Tenant")]
public class PortalTenantController : ControllerBase
{
    private readonly ShmsDbContext _context;

    public PortalTenantController(ShmsDbContext context)
    {
        _context = context;
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    // GET /api/portaltenant/my-house
    // Returns the house assigned to this tenant — reads tenantId from JWT, no query param needed.
    [HttpGet("my-house")]
    public async Task<IActionResult> GetMyHouse()
    {
        var tenantId = GetUserId();

        var tenant = await _context.Tenants
            .Include(t => t.House)
                .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null) return NotFound("Tenant not found.");
        if (tenant.House == null) return NotFound("No house has been assigned to this tenant yet.");

        var h = tenant.House;
        return Ok(new
        {
            h.Id,
            h.HouseNumber,
            HouseType = h.HouseType.ToString(),
            OccupancyStatus = h.OccupancyStatus.ToString(),
            h.RentFee,
            h.DepositFee,
            PaymentStatus = h.PaymentStatus.ToString(),
            h.FlatId,
            Flat = h.Flat == null ? null : new
            {
                h.Flat.Id,
                h.Flat.FlatName,
                h.Flat.County,
                h.Flat.Constituency,
                h.Flat.Ward
            }
        });
    }
}
