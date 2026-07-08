using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Tenant;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TenantController : ControllerBase
{
    private readonly ITenantService _tenantService;
    private readonly ILogger<TenantController> _logger;

    public TenantController(ITenantService tenantService, ILogger<TenantController> logger)
    {
        _tenantService = tenantService;
        _logger = logger;
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Create([FromBody] CreateTenantDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var tenant = await _tenantService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = tenant.Id },
                ApiResponse<object>.SuccessResponse(new
                {
                    tenant.Id,
                    tenant.Email,
                    tenant.FirstName,
                    tenant.LastName,
                    tenant.PhoneNumber,
                    tenant.DateOfBirth,
                    tenant.EmergencyContactName,
                    tenant.EmergencyContactPhone,
                    tenant.IsActive,
                    tenant.PortalUserType
                }, "Tenant created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tenant");
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while creating the tenant"));
        }
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Landlord,Tenant,Agent")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var tenant = await _tenantService.GetByIdAsync(id);
            if (tenant == null)
                return NotFound(ApiResponse<object>.FailureResponse("Tenant not found"));

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                tenant.Id,
                tenant.Email,
                tenant.FirstName,
                tenant.LastName,
                tenant.PhoneNumber,
                tenant.DateOfBirth,
                tenant.EmergencyContactName,
                tenant.EmergencyContactPhone,
                tenant.IsActive,
                tenant.IsEmailVerified,
                tenant.PortalUserType,
                tenant.CreatedAt,
                tenant.UpdatedAt,
                tenant.NationalId,
                tenant.County,
                tenant.Constituency,
                tenant.Ward,
                TenantStatus = tenant.TenantStatus.ToString()
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tenant: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving the tenant"));
        }
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Landlord,Tenant,Agent")]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var tenants = await _tenantService.GetAllAsync();
            return Ok(ApiResponse<object>.SuccessResponse(tenants.Select(t => new
            {
                t.Id,
                t.Email,
                t.FirstName,
                t.LastName,
                t.PhoneNumber,
                t.IsActive,
                t.IsEmailVerified,
                TenantStatus = t.TenantStatus.ToString(),
                Status = t.TenantStatus.ToString(),
                t.HasCompletedInitialPayment,
                t.HouseId,
                HouseNumber = t.House != null ? t.House.HouseNumber : null,
                HouseName = t.House != null
                    ? (t.House.Flat != null
                        ? $"{t.House.HouseNumber} - {t.House.Flat.FlatName}"
                        : $"{t.House.HouseNumber} - (Flat Deleted)")
                    : null,
                t.CreatedAt,
                t.NationalId,
                t.County,
                t.Constituency,
                t.Ward,
                t.DateOfBirth,
                t.EmergencyContactName,
                t.EmergencyContactPhone,
                t.UpdatedAt,
                PortalUserType = t.PortalUserType.ToString()
            })));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all tenants");
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving tenants"));
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenantDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var tenant = await _tenantService.UpdateAsync(id, dto);
            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                tenant.Id,
                tenant.Email,
                tenant.FirstName,
                tenant.LastName,
                tenant.PhoneNumber,
                tenant.DateOfBirth,
                tenant.EmergencyContactName,
                tenant.EmergencyContactPhone,
                tenant.IsActive,
                tenant.UpdatedAt
            }, "Tenant updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tenant: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while updating the tenant"));
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var result = await _tenantService.DeleteAsync(id);
            if (!result)
                return NotFound(ApiResponse<object>.FailureResponse("Tenant not found"));

            return Ok(ApiResponse<object?>.SuccessResponse(null, "Tenant deleted successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting tenant: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while deleting the tenant"));
        }
    }

    [HttpPatch("{id}/toggle-status")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> ToggleStatus(Guid id)
    {
        try
        {
            var result = await _tenantService.ToggleStatusAsync(id);
            if (!result)
                return NotFound(ApiResponse<object>.FailureResponse("Tenant not found"));

            return Ok(ApiResponse<object?>.SuccessResponse(null, "Tenant status updated successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling tenant status: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while updating tenant status"));
        }
    }
}
