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
using ShmsBackend.Api.Models.Responses;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
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
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;

    public HouseController(HouseService houseService, ShmsDbContext context, IEmailService emailService, INotificationService notificationService)
    {
        _houseService = houseService;
        _context = context;
        _emailService = emailService;
        _notificationService = notificationService;
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
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant,Landlord,Agent")]
    public async Task<IActionResult> GetHistory(Guid id)
    {
        if (User.IsInRole("Landlord") || User.IsInRole("Agent"))
        {
            var house = await _context.Houses
                .Include(h => h.Flat)
                .FirstOrDefaultAsync(h => h.Id == id);
            if (house == null)
                return NotFound(new { success = false, message = "House not found." });

            var callerIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(callerIdStr, out var callerId))
                return Unauthorized();

            if (User.IsInRole("Landlord"))
            {
                if (house.Flat == null || house.Flat.LandlordId != callerId)
                    return StatusCode(403, new { success = false, message = "Not authorized for this house." });
            }
            else
            {
                var authorized = await _context.AgentFlats
                    .AnyAsync(af => af.AgentId == callerId && af.FlatId == house.FlatId);
                if (!authorized)
                    return StatusCode(403, new { success = false, message = "Not authorized for this house." });
            }
        }

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

    [HttpPost("bulk-price-change")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> BulkPriceChange([FromBody] BulkPriceChangeDto dto)
    {
        var results = new List<object>();
        foreach (var houseId in dto.HouseIds)
        {
            var house = await _context.Houses.Include(h => h.Flat).FirstOrDefaultAsync(h => h.Id == houseId);
            if (house == null) continue;

            var everOccupied = await _context.TenantHouseHistories.AnyAsync(h => h.HouseId == houseId);
            if (!everOccupied)
            {
                house.RentFee = dto.NewRentFee;
                house.DepositFee = dto.NewDepositFee;
                results.Add(new { houseId, applied = "immediate" });
            }
            else
            {
                if (!dto.EffectiveMonth.HasValue || !dto.EffectiveYear.HasValue)
                    return BadRequest(new { success = false, message = $"House {house.HouseNumber} has tenant history — an effective month is required." });

                _context.PendingRentChanges.Add(new PendingRentChange
                {
                    Id = Guid.NewGuid(),
                    HouseId = houseId,
                    NewRentFee = dto.NewRentFee,
                    NewDepositFee = dto.NewDepositFee,
                    EffectiveMonth = dto.EffectiveMonth.Value,
                    EffectiveYear = dto.EffectiveYear.Value,
                    CreatedByUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)
                });
                results.Add(new { houseId, applied = "scheduled" });

                var currentTenant = await _context.Tenants.FirstOrDefaultAsync(t => t.HouseId == houseId && t.IsActive);
                if (currentTenant != null)
                {
                    await _emailService.SendRentChangeNoticeAsync(currentTenant.Email, currentTenant.FirstName,
                        house.HouseNumber, dto.NewRentFee, dto.EffectiveMonth.Value, dto.EffectiveYear.Value);
                    await _notificationService.SendToUserAsync(currentTenant.Id.ToString(),
                        $"Your rent for House {house.HouseNumber} will change to KES {dto.NewRentFee} starting {dto.EffectiveMonth}/{dto.EffectiveYear}.", "rent_change");
                }
                if (house.Flat?.LandlordId != null)
                {
                    await _notificationService.SendToUserAsync(house.Flat.LandlordId.ToString(),
                        $"Rent for House {house.HouseNumber} in {house.Flat.FlatName} will change to KES {dto.NewRentFee} starting {dto.EffectiveMonth}/{dto.EffectiveYear}.", "rent_change");
                }
            }
        }
        await _context.SaveChangesAsync();
        return Ok(ApiResponse<object>.SuccessResponse(results));
    }
}
