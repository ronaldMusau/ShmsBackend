using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Flat;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FlatController : ControllerBase
{
    private readonly IFlatService _flatService;
    private readonly ILogger<FlatController> _logger;

    public FlatController(IFlatService flatService, ILogger<FlatController> logger)
    {
        _flatService = flatService;
        _logger = logger;
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> Create([FromBody] CreateFlatDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var flat = await _flatService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = flat.Id },
                ApiResponse<object>.SuccessResponse(new
                {
                    flat.Id,
                    flat.Title,
                    flat.Address,
                    flat.City,
                    flat.Price,
                    flat.FloorNumber,
                    flat.Bedrooms,
                    flat.Bathrooms,
                    flat.IsAvailable,
                    flat.HouseId,
                    flat.LandlordId,
                    flat.AgentId
                }, "Flat created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating flat");
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while creating the flat"));
        }
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var flat = await _flatService.GetByIdAsync(id);
            if (flat == null)
                return NotFound(ApiResponse<object>.FailureResponse("Flat not found"));

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                flat.Id,
                flat.Title,
                flat.Description,
                flat.Address,
                flat.City,
                flat.State,
                flat.ZipCode,
                flat.Price,
                flat.FloorNumber,
                flat.Bedrooms,
                flat.Bathrooms,
                flat.IsAvailable,
                flat.HouseId,
                flat.LandlordId,
                flat.AgentId,
                flat.CreatedAt,
                flat.UpdatedAt
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting flat: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving the flat"));
        }
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var flats = await _flatService.GetAllAsync();
            return Ok(ApiResponse<object>.SuccessResponse(flats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all flats");
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving flats"));
        }
    }

    [HttpGet("available")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAvailable()
    {
        try
        {
            var flats = await _flatService.GetAvailableAsync();
            return Ok(ApiResponse<object>.SuccessResponse(flats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available flats");
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving available flats"));
        }
    }

    [HttpGet("city/{city}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetByCity(string city)
    {
        try
        {
            var flats = await _flatService.GetByCityAsync(city);
            return Ok(ApiResponse<object>.SuccessResponse(flats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting flats by city: {City}", city);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving flats"));
        }
    }

    [HttpGet("landlord/{landlordId}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> GetByLandlord(Guid landlordId)
    {
        try
        {
            var flats = await _flatService.GetByLandlordAsync(landlordId);
            return Ok(ApiResponse<object>.SuccessResponse(flats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting flats by landlord: {LandlordId}", landlordId);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving flats"));
        }
    }

    [HttpGet("house/{houseId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetByHouse(Guid houseId)
    {
        try
        {
            var flats = await _flatService.GetByHouseAsync(houseId);
            return Ok(ApiResponse<object>.SuccessResponse(flats));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting flats by house: {HouseId}", houseId);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving flats"));
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFlatDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var flat = await _flatService.UpdateAsync(id, dto);
            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                flat.Id,
                flat.Title,
                flat.Address,
                flat.City,
                flat.Price,
                flat.FloorNumber,
                flat.Bedrooms,
                flat.Bathrooms,
                flat.IsAvailable,
                flat.HouseId,
                flat.AgentId,
                flat.UpdatedAt
            }, "Flat updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating flat: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while updating the flat"));
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var result = await _flatService.DeleteAsync(id);
            if (!result)
                return NotFound(ApiResponse<object>.FailureResponse("Flat not found"));

            return Ok(ApiResponse<object?>.SuccessResponse(null, "Flat deleted successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting flat: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while deleting the flat"));
        }
    }
}
