using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Tenant;
using ShmsBackend.Api.Models.Responses;
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
            .Include(t => t.House)
                .ThenInclude(h => h!.Images)
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null) return NotFound("Tenant not found.");
        if (tenant.House == null) return NotFound("No house has been assigned to this tenant yet.");

        var h = tenant.House;
        return Ok(ApiResponse<object>.SuccessResponse(new
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
            Images = h.Images.OrderBy(i => i.SortOrder).Select(i => new { i.Id, i.ImagePath }).ToList(),
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
        }));
    }

    [HttpGet("my-tenants")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> GetMyTenants()
    {
        var agentIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(agentIdStr, out var agentId))
            return Unauthorized();

        var agentFlatIds = await _context.AgentFlats
            .Where(af => af.AgentId == agentId)
            .Select(af => af.FlatId)
            .ToListAsync();

        var tenants = await _context.Tenants
            .Include(t => t.House)
            .ThenInclude(h => h!.Flat)
            .Where(t => t.HouseId != null &&
                   t.House != null &&
                   agentFlatIds.Contains(t.House.FlatId))
            .Select(t => new
            {
                t.Id,
                t.FirstName,
                t.LastName,
                t.Email,
                t.PhoneNumber,
                t.IsActive,
                TenantStatus = t.TenantStatus.ToString(),
                t.HasCompletedInitialPayment,
                t.CreatedAt,
                HouseNumber = t.House!.HouseNumber,
                HouseId = t.House!.Id,
                FlatName = t.House!.Flat != null ? t.House!.Flat.FlatName : "(Flat Deleted)",
                FlatId = t.House!.Flat != null ? t.House!.Flat.Id : (Guid?)null
            })
            .ToListAsync();

        return Ok(new { success = true, data = tenants });
    }

    [HttpGet("landlord/my-tenants")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordTenants()
    {
        var landlordIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(landlordIdStr, out var landlordId))
            return Unauthorized();

        var tenants = await _context.Tenants
            .Include(t => t.House)
                .ThenInclude(h => h!.Flat)
            .Where(t => t.HouseId != null &&
                   t.House != null &&
                   t.House.Flat != null &&
                   t.House.Flat.LandlordId == landlordId)
            .Select(t => new
            {
                t.Id,
                t.FirstName,
                t.LastName,
                t.Email,
                t.PhoneNumber,
                t.IsActive,
                TenantStatus = t.TenantStatus.ToString(),
                t.HasCompletedInitialPayment,
                t.CreatedAt,
                HouseNumber = t.House!.HouseNumber,
                HouseId = t.House!.Id,
                FlatName = t.House!.Flat != null ? t.House!.Flat.FlatName : "(Flat Deleted)",
                FlatId = t.House!.Flat != null ? t.House!.Flat.Id : (Guid?)null
            })
            .ToListAsync();

        return Ok(new { success = true, data = tenants });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> GetTenantById(Guid id)
    {
        var agentIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(agentIdStr, out var agentId))
            return Unauthorized();

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == id);
        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        if (tenant.HouseId.HasValue)
        {
            var house = await _context.Houses.FindAsync(tenant.HouseId.Value);
            if (house != null)
            {
                var authorized = await _context.AgentFlats
                    .AnyAsync(af => af.AgentId == agentId && af.FlatId == house.FlatId);
                if (!authorized)
                    return StatusCode(403, new { success = false, message = "Not authorized for this tenant." });
            }
        }

        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            tenant.Id,
            tenant.Email,
            tenant.FirstName,
            tenant.LastName,
            tenant.PhoneNumber,
            tenant.NationalId,
            tenant.DateOfBirth,
            tenant.EmergencyContactName,
            tenant.EmergencyContactPhone,
            tenant.HouseId,
            tenant.IsActive,
            tenant.HasCompletedInitialPayment
        }));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "Agent")]
    public async Task<IActionResult> UpdateTenant(Guid id, [FromBody] UpdateTenantDto dto)
    {
        var agentIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(agentIdStr, out var agentId))
            return Unauthorized();

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == id);
        if (tenant == null)
            return NotFound(new { success = false, message = "Tenant not found." });

        if (tenant.HouseId.HasValue)
        {
            var house = await _context.Houses.FindAsync(tenant.HouseId.Value);
            if (house != null)
            {
                var authorized = await _context.AgentFlats
                    .AnyAsync(af => af.AgentId == agentId && af.FlatId == house.FlatId);
                if (!authorized)
                    return StatusCode(403, new { success = false, message = "Not authorized for this tenant." });
            }
        }

        if (dto.HouseId.HasValue && dto.HouseId != tenant.HouseId)
        {
            var newHouse = await _context.Houses.FindAsync(dto.HouseId.Value);
            if (newHouse == null)
                return BadRequest(new { success = false, message = "House not found." });
            var authorizedNew = await _context.AgentFlats
                .AnyAsync(af => af.AgentId == agentId && af.FlatId == newHouse.FlatId);
            if (!authorizedNew)
                return StatusCode(403, new { success = false, message = "Not authorized to assign this house." });
        }

        try
        {
            var result = await _tenantService.UpdateAsync(id, dto);
            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                result.Id,
                result.Email,
                result.FirstName,
                result.LastName,
                result.PhoneNumber,
                result.DateOfBirth,
                result.EmergencyContactName,
                result.EmergencyContactPhone,
                result.IsActive,
                result.UpdatedAt
            }, "Tenant updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tenant: {Id}", id);
            return StatusCode(500, new { success = false, message = "Failed to update tenant." });
        }
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
