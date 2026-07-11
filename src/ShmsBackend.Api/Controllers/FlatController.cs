using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Flat;
using ShmsBackend.Api.Services.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/flats")]
public class FlatController : ControllerBase
{
    private readonly FlatService _flatService;
    private readonly ILogger<FlatController> _logger;

    public FlatController(FlatService flatService, ILogger<FlatController> logger)
    {
        _flatService = flatService;
        _logger = logger;
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Create([FromBody] CreateFlatDto dto)
    {
        try
        {
            var result = await _flatService.CreateAsync(dto);
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create flat: {Message}", ex.Message);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetAll()
    {
        var result = await _flatService.GetAllAsync();
        return Ok(new { success = true, data = result });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _flatService.GetByIdAsync(id);
        if (result == null) return NotFound(new { success = false, message = "Flat not found." });
        return Ok(new { success = true, data = result });
    }

    [HttpGet("landlord/{landlordId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetByLandlord(Guid landlordId)
    {
        var result = await _flatService.GetByLandlordAsync(landlordId);
        return Ok(new { success = true, data = result });
    }

    [HttpGet("location")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetByLocation(
        [FromQuery] string county,
        [FromQuery] string constituency,
        [FromQuery] string ward)
    {
        var result = await _flatService.GetByLocationAsync(county, constituency, ward);
        return Ok(new { success = true, data = result });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFlatDto dto)
    {
        try
        {
            var result = await _flatService.UpdateAsync(id, dto);
            if (result == null) return NotFound(new { success = false, message = "Flat not found." });
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var result = await _flatService.DeleteAsync(id);
        if (!result) return NotFound(new { success = false, message = "Flat not found." });
        return Ok(new { success = true, message = "Flat deleted successfully." });
    }

    [HttpPost("{flatId:guid}/houses")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> AddHouseLines(Guid flatId, [FromBody] List<HouseGroupDto> houseLines)
    {
        try
        {
            var result = await _flatService.AddHouseLinesAsync(flatId, houseLines);
            if (result == null) return NotFound(new { success = false, message = "Flat not found." });
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add house lines to flat {FlatId}: {Message}", flatId, ex.Message);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }
}
