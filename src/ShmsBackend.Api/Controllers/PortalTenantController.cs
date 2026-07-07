using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Tenant;
using ShmsBackend.Api.Services.Portal;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/portaltenant")]
public class PortalTenantController : ControllerBase
{
    private readonly ShmsDbContext _context;
    private readonly ITenantService _tenantService;
    private readonly ILogger<PortalTenantController> _logger;

    public PortalTenantController(ShmsDbContext context, ITenantService tenantService, ILogger<PortalTenantController> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    // GET /api/portaltenant/my-house
    // Returns the house assigned to this tenant — reads tenantId from JWT, no query param needed.
    [HttpGet("my-house")]
    [Authorize(Roles = "Tenant")]
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
            },
            TenantProfile = new
            {
                tenant.FirstName,
                tenant.LastName,
                tenant.Email,
                tenant.PhoneNumber,
                tenant.NationalId,
                tenant.DateOfBirth,
                tenant.County,
                tenant.Constituency,
                tenant.Ward,
                tenant.IsActive,
                tenant.CreatedAt
            }
        });
    }

    [HttpPost("create")]
    [Authorize(Roles = "Agent,Landlord,SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantDto dto)
    {
        try
        {
            var tenant = await _tenantService.CreateAsync(dto);
            return Ok(new
            {
                success = true,
                message = "Tenant created successfully.",
                data = new
                {
                    tenant.Id,
                    tenant.FirstName,
                    tenant.LastName,
                    tenant.Email,
                    tenant.PhoneNumber,
                    tenant.HouseId,
                    tenant.IsActive,
                    tenant.CreatedAt
                }
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create tenant from portal");
            return StatusCode(500, new { success = false, message = "Failed to create tenant." });
        }
    }
}
