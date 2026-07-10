using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.DTOs.House;
using ShmsBackend.Api.Services.Portal;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/houses")]
public class HouseController : ControllerBase
{
    private readonly HouseService _houseService;
    private readonly ShmsDbContext _context;

    public HouseController(HouseService houseService, ShmsDbContext context)
    {
        _houseService = houseService;
        _context = context;
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Create([FromBody] CreateHouseDto dto)
    {
        try
        {
            var result = await _houseService.CreateAsync(dto);
            return Ok(new { success = true, data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetAll()
    {
        var result = await _houseService.GetAllAsync();
        return Ok(new { success = true, data = result });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant,Agent")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _houseService.GetByIdAsync(id);
        if (result == null) return NotFound(new { success = false, message = "House not found." });

        if (User.IsInRole("Agent"))
        {
            var agentIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(agentIdStr, out var agentId))
                return Unauthorized();

            var authorized = await _context.Houses
                .AnyAsync(h => h.Id == id &&
                               _context.AgentFlats.Any(af => af.AgentId == agentId && af.FlatId == h.FlatId));
            if (!authorized)
                return StatusCode(403, new { success = false, message = "Not authorized for this house." });
        }

        return Ok(new { success = true, data = result });
    }

    [HttpGet("flat/{flatId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetByFlat(Guid flatId)
    {
        var result = await _houseService.GetByFlatAsync(flatId);
        return Ok(new { success = true, data = result });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateHouseDto dto)
    {
        try
        {
            var result = await _houseService.UpdateAsync(id, dto);
            if (result == null) return NotFound(new { success = false, message = "House not found." });
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
        var result = await _houseService.DeleteAsync(id);
        if (!result) return NotFound(new { success = false, message = "House not found." });
        return Ok(new { success = true, message = "House deleted successfully." });
    }

    [HttpGet("{id:guid}/history")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetHistory(Guid id)
    {
        var history = await _houseService.GetHistoryAsync(id);
        return Ok(new { success = true, data = history });
    }
}
