using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Landlord;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LandlordController : ControllerBase
{
    private readonly ILandlordService _landlordService;
    private readonly ILogger<LandlordController> _logger;

    public LandlordController(ILandlordService landlordService, ILogger<LandlordController> logger)
    {
        _landlordService = landlordService;
        _logger = logger;
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Create([FromBody] CreateLandlordDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var landlord = await _landlordService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = landlord.Id },
                ApiResponse<object>.SuccessResponse(new
                {
                    landlord.Id,
                    landlord.Email,
                    landlord.FirstName,
                    landlord.LastName,
                    landlord.PhoneNumber,
                    landlord.NationalId,
                    landlord.AgencyName,
                    landlord.IsActive,
                    landlord.PortalUserType
                }, "Landlord created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating landlord");
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while creating the landlord"));
        }
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Landlord,Tenant,Agent")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var landlord = await _landlordService.GetByIdAsync(id);
            if (landlord == null)
                return NotFound(ApiResponse<object>.FailureResponse("Landlord not found"));

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                landlord.Id,
                landlord.Email,
                landlord.FirstName,
                landlord.LastName,
                landlord.PhoneNumber,
                landlord.NationalId,
                landlord.AgencyName,
                landlord.IsActive,
                landlord.IsEmailVerified,
                landlord.PortalUserType,
                landlord.CreatedAt,
                landlord.UpdatedAt
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting landlord: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving the landlord"));
        }
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Landlord,Tenant,Agent")]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var landlords = await _landlordService.GetAllAsync();
            return Ok(ApiResponse<object>.SuccessResponse(landlords));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all landlords");
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while retrieving landlords"));
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLandlordDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var landlord = await _landlordService.UpdateAsync(id, dto);
            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                landlord.Id,
                landlord.Email,
                landlord.FirstName,
                landlord.LastName,
                landlord.PhoneNumber,
                landlord.NationalId,
                landlord.AgencyName,
                landlord.IsActive,
                landlord.UpdatedAt
            }, "Landlord updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating landlord: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while updating the landlord"));
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var result = await _landlordService.DeleteAsync(id);
            if (!result)
                return NotFound(ApiResponse<object>.FailureResponse("Landlord not found"));

            return Ok(ApiResponse<object?>.SuccessResponse(null, "Landlord deleted successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting landlord: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while deleting the landlord"));
        }
    }

    [HttpPatch("{id}/toggle-status")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> ToggleStatus(Guid id)
    {
        try
        {
            var result = await _landlordService.ToggleStatusAsync(id);
            if (!result)
                return NotFound(ApiResponse<object>.FailureResponse("Landlord not found"));

            return Ok(ApiResponse<object?>.SuccessResponse(null, "Landlord status updated successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling landlord status: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse(
                "An error occurred while updating landlord status"));
        }
    }
}
