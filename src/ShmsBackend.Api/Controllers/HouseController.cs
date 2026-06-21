using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.House;
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HouseController : ControllerBase
{
    private readonly IHouseService _houseService;
    private readonly ILogger<HouseController> _logger;

    public HouseController(IHouseService houseService, ILogger<HouseController> logger)
    {
        _houseService = houseService;
        _logger = logger;
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> Create([FromBody] CreateHouseDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var house = await _houseService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = house.Id },
                ApiResponse<object>.SuccessResponse(new
                {
                    house.Id,
                    house.Title,
                    house.Address,
                    house.City,
                    house.Price,
                    house.Bedrooms,
                    house.Bathrooms,
                    house.IsAvailable,
                    house.LandlordId,
                    house.AgentId
                }, "House created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating house");
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while creating the house"));
        }
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            var house = await _houseService.GetByIdAsync(id);
            if (house == null)
                return NotFound(ApiResponse<object>.FailureResponse("House not found"));

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                house.Id,
                house.Title,
                house.Description,
                house.Address,
                house.City,
                house.State,
                house.ZipCode,
                house.Price,
                house.Bedrooms,
                house.Bathrooms,
                house.IsAvailable,
                house.LandlordId,
                house.AgentId,
                house.CreatedAt,
                house.UpdatedAt
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting house: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving the house"));
        }
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var houses = await _houseService.GetAllAsync();
            return Ok(ApiResponse<object>.SuccessResponse(houses));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all houses");
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving houses"));
        }
    }

    [HttpGet("available")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAvailable()
    {
        try
        {
            var houses = await _houseService.GetAvailableAsync();
            return Ok(ApiResponse<object>.SuccessResponse(houses));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available houses");
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving available houses"));
        }
    }

    [HttpGet("city/{city}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetByCity(string city)
    {
        try
        {
            var houses = await _houseService.GetByCityAsync(city);
            return Ok(ApiResponse<object>.SuccessResponse(houses));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting houses by city: {City}", city);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving houses"));
        }
    }

    [HttpGet("landlord/{landlordId}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> GetByLandlord(Guid landlordId)
    {
        try
        {
            var houses = await _houseService.GetByLandlordAsync(landlordId);
            return Ok(ApiResponse<object>.SuccessResponse(houses));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting houses by landlord: {LandlordId}", landlordId);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while retrieving houses"));
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateHouseDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var house = await _houseService.UpdateAsync(id, dto);
            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                house.Id,
                house.Title,
                house.Address,
                house.City,
                house.Price,
                house.Bedrooms,
                house.Bathrooms,
                house.IsAvailable,
                house.AgentId,
                house.UpdatedAt
            }, "House updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailureResponse(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating house: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while updating the house"));
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var result = await _houseService.DeleteAsync(id);
            if (!result)
                return NotFound(ApiResponse<object>.FailureResponse("House not found"));

            return Ok(ApiResponse<object?>.SuccessResponse(null, "House deleted successfully"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting house: {Id}", id);
            return StatusCode(500, ApiResponse<object>.FailureResponse("An error occurred while deleting the house"));
        }
    }
}
