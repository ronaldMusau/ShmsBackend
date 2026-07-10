using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.DTOs.House;
using ShmsBackend.Api.Services.Portal;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

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

    [HttpPost("upload-images")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Agent")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadImages([FromForm] List<Guid> houseIds, [FromForm] List<IFormFile> files)
    {
        if (houseIds == null || houseIds.Count == 0)
            return BadRequest(new { success = false, message = "At least one house ID is required." });
        if (files == null || files.Count == 0)
            return BadRequest(new { success = false, message = "At least one image file is required." });

        var existingCount = await _context.HouseImages.CountAsync(hi => hi.HouseId == houseIds[0]);
        if (existingCount + files.Count > 5)
            return BadRequest(new { success = false, message = $"Maximum 5 images per house. This house already has {existingCount}." });

        var savedPaths = new List<string>();
        var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "house-images");
        Directory.CreateDirectory(uploadsRoot);

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file.FileName);
            if (!new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(ext.ToLower()))
                continue;

            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadsRoot, fileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
            savedPaths.Add($"/house-images/{fileName}");
        }

        int sortOrder = existingCount;
        foreach (var houseId in houseIds)
        {
            foreach (var path in savedPaths)
            {
                _context.HouseImages.Add(new HouseImage
                {
                    Id = Guid.NewGuid(),
                    HouseId = houseId,
                    ImagePath = path,
                    SortOrder = sortOrder,
                    CreatedAt = DateTime.UtcNow
                });
            }
            sortOrder++;
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Images uploaded successfully.", data = savedPaths });
    }

    [HttpDelete("images/{imageId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Agent")]
    public async Task<IActionResult> DeleteImage(Guid imageId)
    {
        var image = await _context.HouseImages.FindAsync(imageId);
        if (image == null)
            return NotFound(new { success = false, message = "Image not found." });

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", image.ImagePath.TrimStart('/'));
        if (System.IO.File.Exists(filePath))
            System.IO.File.Delete(filePath);

        _context.HouseImages.Remove(image);
        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Image deleted." });
    }
}
