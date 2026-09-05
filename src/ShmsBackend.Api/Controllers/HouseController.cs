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
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var all = (await _houseService.GetAllAsync()).ToList();
        var total = all.Count;
        var data = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
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
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant,Landlord,Agent")]
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

    [HttpPatch("{id:guid}/listing-visibility")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> SetListingVisibility(Guid id, [FromBody] SetListingVisibilityDto dto)
    {
        var house = await _context.Houses.FindAsync(id);
        if (house == null) return NotFound(new { success = false, message = "House not found." });
        house.IsListingHidden = dto.Hidden;
        house.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { success = true, data = new { house.Id, house.IsListingHidden } });
    }

    [HttpPatch("{id:guid}/comments-mute")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> SetCommentsMute(Guid id, [FromBody] SetCommentsMuteDto dto)
    {
        var house = await _context.Houses.FindAsync(id);
        if (house == null) return NotFound(new { success = false, message = "House not found." });
        house.CommentsMuted = dto.Muted;
        house.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { success = true, data = new { house.Id, house.CommentsMuted } });
    }

    [HttpGet("listing-stats/all")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetAllListingStats(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? houseType = null,
        [FromQuery] decimal? minRent = null,
        [FromQuery] decimal? maxRent = null,
        [FromQuery] string? county = null,
        [FromQuery] string? constituency = null,
        [FromQuery] string? ward = null,
        [FromQuery] string? sort = null,
        [FromQuery] bool? isHidden = null,
        [FromQuery] bool? commentsMuted = null)
    {
        IQueryable<House> baseQuery = _context.Houses
            .Include(h => h.Flat)
            .Include(h => h.Images)
            .Include(h => h.HouseTypeRef);

        if (!string.IsNullOrEmpty(search))
            baseQuery = baseQuery.Where(h =>
                h.HouseNumber.Contains(search) ||
                (h.Flat != null && h.Flat.FlatName.Contains(search)));
        if (!string.IsNullOrEmpty(county))
            baseQuery = baseQuery.Where(h => h.Flat != null && h.Flat.County == county);
        if (!string.IsNullOrEmpty(constituency))
            baseQuery = baseQuery.Where(h => h.Flat != null && h.Flat.Constituency == constituency);
        if (!string.IsNullOrEmpty(ward))
            baseQuery = baseQuery.Where(h => h.Flat != null && h.Flat.Ward == ward);
        if (!string.IsNullOrEmpty(houseType))
            baseQuery = baseQuery.Where(h => h.HouseTypeRef != null && h.HouseTypeRef.Name == houseType);
        if (minRent.HasValue)
            baseQuery = baseQuery.Where(h => h.RentFee >= minRent.Value);
        if (maxRent.HasValue)
            baseQuery = baseQuery.Where(h => h.RentFee <= maxRent.Value);
        if (isHidden.HasValue)
            baseQuery = baseQuery.Where(h => h.IsListingHidden == isHidden.Value);
        if (commentsMuted.HasValue)
            baseQuery = baseQuery.Where(h => h.CommentsMuted == commentsMuted.Value);

        var total = await baseQuery.CountAsync();

        List<House> pagedHouses;
        Dictionary<Guid, int> likeDict;
        Dictionary<Guid, int> dislikeDict;
        Dictionary<Guid, double?> ratingDict;

        if (sort == "popular" || sort == "trending")
        {
            var allLight = await baseQuery
                .Select(h => new { h.Id, h.CreatedAt })
                .ToListAsync();

            var allIds = allLight.Select(h => h.Id).ToList();

            var allLikeCounts = await _context.HouseListingLikes
                .Where(l => allIds.Contains(l.HouseId))
                .GroupBy(l => new { l.HouseId, l.IsLike })
                .Select(g => new { g.Key.HouseId, g.Key.IsLike, Count = g.Count() })
                .ToListAsync();

            var allRatings = await _context.HouseListingRatings
                .Where(r => allIds.Contains(r.HouseId))
                .GroupBy(r => r.HouseId)
                .Select(g => new { HouseId = g.Key, Avg = g.Average(r => (double)r.Stars) })
                .ToListAsync();

            var sortLikeDict = allLikeCounts.Where(l => l.IsLike).ToDictionary(l => l.HouseId, l => l.Count);
            var sortDislikeDict = allLikeCounts.Where(l => !l.IsLike).ToDictionary(l => l.HouseId, l => l.Count);
            var sortRatingDict = allRatings.ToDictionary(r => r.HouseId, r => r.Avg);

            var pageIds = (sort == "popular"
                ? allLight
                    .OrderByDescending(h => sortRatingDict.GetValueOrDefault(h.Id))
                    .ThenByDescending(h => h.CreatedAt)
                : allLight
                    .OrderByDescending(h =>
                        sortLikeDict.GetValueOrDefault(h.Id) - sortDislikeDict.GetValueOrDefault(h.Id))
                    .ThenByDescending(h => h.CreatedAt))
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(h => h.Id)
                .ToList();

            var pageHouseList = await _context.Houses
                .Include(h => h.Flat)
                .Include(h => h.Images)
                .Include(h => h.HouseTypeRef)
                .Where(h => pageIds.Contains(h.Id))
                .ToListAsync();

            pagedHouses = pageIds.Select(id => pageHouseList.First(h => h.Id == id)).ToList();

            likeDict = sortLikeDict.Where(kv => pageIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            dislikeDict = sortDislikeDict.Where(kv => pageIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            ratingDict = sortRatingDict.Where(kv => pageIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => (double?)kv.Value);
        }
        else
        {
            pagedHouses = await baseQuery
                .OrderByDescending(h => h.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var houseIds = pagedHouses.Select(h => h.Id).ToList();

            var likeCounts = await _context.HouseListingLikes
                .Where(l => houseIds.Contains(l.HouseId))
                .GroupBy(l => new { l.HouseId, l.IsLike })
                .Select(g => new { g.Key.HouseId, g.Key.IsLike, Count = g.Count() })
                .ToListAsync();

            var ratings = await _context.HouseListingRatings
                .Where(r => houseIds.Contains(r.HouseId))
                .GroupBy(r => r.HouseId)
                .Select(g => new { HouseId = g.Key, Avg = g.Average(r => (double)r.Stars) })
                .ToListAsync();

            likeDict = likeCounts.Where(l => l.IsLike).ToDictionary(l => l.HouseId, l => l.Count);
            dislikeDict = likeCounts.Where(l => !l.IsLike).ToDictionary(l => l.HouseId, l => l.Count);
            ratingDict = ratings.ToDictionary(r => r.HouseId, r => (double?)r.Avg);
        }

        var pageHouseIds = pagedHouses.Select(h => h.Id).ToList();
        var flatIds = pagedHouses.Select(h => h.FlatId).Distinct().ToList();

        var commentCounts = await _context.HouseListingComments
            .Where(c => pageHouseIds.Contains(c.HouseId))
            .GroupBy(c => c.HouseId)
            .Select(g => new { HouseId = g.Key, Count = g.Count() })
            .ToListAsync();
        var commentCountDict = commentCounts.ToDictionary(c => c.HouseId, c => c.Count);

        var agentFlats = await _context.AgentFlats
            .Include(af => af.Agent)
            .Where(af => flatIds.Contains(af.FlatId))
            .ToListAsync();

        var agentDict = agentFlats
            .GroupBy(af => af.FlatId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(af => af.AssignedAt).First().Agent);

        var data = pagedHouses.Select(h =>
        {
            likeDict.TryGetValue(h.Id, out var likeCount);
            dislikeDict.TryGetValue(h.Id, out var dislikeCount);
            ratingDict.TryGetValue(h.Id, out var avgRating);
            commentCountDict.TryGetValue(h.Id, out var commentCount);
            agentDict.TryGetValue(h.FlatId, out var agent);
            return (object)new
            {
                id = h.Id,
                houseNumber = h.HouseNumber,
                houseType = h.HouseTypeRef?.Name,
                flatName = h.Flat?.FlatName,
                rentFee = h.RentFee,
                depositFee = h.DepositFee,
                county = h.Flat?.County,
                constituency = h.Flat?.Constituency,
                ward = h.Flat?.Ward,
                occupancyStatus = h.OccupancyStatus.ToString(),
                images = h.Images.OrderBy(i => i.SortOrder).Select(i => i.ImagePath).ToList(),
                isListingHidden = h.IsListingHidden,
                commentsMuted = h.CommentsMuted,
                isPubliclyVisible = h.OccupancyStatus == OccupancyStatus.Vacant
                                 && !h.IsListingHidden
                                 && h.Images.Any(),
                avgRating,
                likeCount,
                dislikeCount,
                commentCount,
                agent = agent == null ? null : new
                {
                    name = $"{agent.FirstName} {agent.LastName}".Trim(),
                    phone = agent.PhoneNumber,
                    agencyName = agent.AgencyName
                }
            };
        }).ToList();

        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    [HttpGet("{id:guid}/listing-stats")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetListingStats(Guid id)
    {
        var house = await _context.Houses.Include(h => h.Flat).FirstOrDefaultAsync(h => h.Id == id);
        if (house == null) return NotFound(new { success = false, message = "House not found." });

        var likeCount = await _context.HouseListingLikes.CountAsync(l => l.HouseId == id && l.IsLike);
        var dislikeCount = await _context.HouseListingLikes.CountAsync(l => l.HouseId == id && !l.IsLike);
        var avgRating = await _context.HouseListingRatings
            .Where(r => r.HouseId == id)
            .AverageAsync(r => (double?)r.Stars);

        var comments = await _context.HouseListingComments
            .Where(c => c.HouseId == id)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Id, c.CommenterName, c.Comment, c.IsHidden, c.CreatedAt })
            .ToListAsync();

        return Ok(new
        {
            success = true,
            data = new { houseNumber = house.HouseNumber, flatName = house.Flat?.FlatName, likeCount, dislikeCount, avgRating, comments }
        });
    }

    [HttpGet("{id:guid}/listing-comments")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetListingComments(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var house = await _context.Houses.FindAsync(id);
        if (house == null) return NotFound(new { success = false, message = "House not found." });

        var query = _context.HouseListingComments
            .Where(c => c.HouseId == id)
            .OrderByDescending(c => c.CreatedAt);

        var total = await query.CountAsync();
        var data = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new { c.Id, c.CommenterName, c.Comment, c.IsHidden, c.CreatedAt })
            .ToListAsync();

        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    [HttpGet("{id:guid}/listing-ratings")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetListingRatings(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var house = await _context.Houses.FindAsync(id);
        if (house == null) return NotFound(new { success = false, message = "House not found." });

        var query = _context.HouseListingRatings
            .Where(r => r.HouseId == id)
            .OrderByDescending(r => r.CreatedAt);

        var total = await query.CountAsync();
        var pageRows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var explorerIds = pageRows
            .Where(r => r.ExplorerId != null)
            .Select(r => r.ExplorerId!.Value)
            .Distinct()
            .ToList();

        var explorerNames = await _context.Explorers
            .Where(e => explorerIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .ToDictionaryAsync(e => e.Id);

        var data = pageRows.Select(r =>
        {
            var displayName = r.ExplorerId != null && explorerNames.TryGetValue(r.ExplorerId.Value, out var exp)
                ? $"{exp.FirstName} {exp.LastName}".Trim()
                : "Anonymous Visitor";
            return (object)new { r.Id, displayName, r.Stars, r.CreatedAt };
        }).ToList();

        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    [HttpGet("{id:guid}/listing-reactions")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> GetListingReactions(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var house = await _context.Houses.FindAsync(id);
        if (house == null) return NotFound(new { success = false, message = "House not found." });

        var query = _context.HouseListingLikes
            .Where(l => l.HouseId == id)
            .OrderByDescending(l => l.CreatedAt);

        var total = await query.CountAsync();
        var pageRows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var explorerIds = pageRows
            .Where(l => l.ExplorerId != null)
            .Select(l => l.ExplorerId!.Value)
            .Distinct()
            .ToList();

        var explorerNames = await _context.Explorers
            .Where(e => explorerIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .ToDictionaryAsync(e => e.Id);

        var data = pageRows.Select(l =>
        {
            var displayName = l.ExplorerId != null && explorerNames.TryGetValue(l.ExplorerId.Value, out var exp)
                ? $"{exp.FirstName} {exp.LastName}".Trim()
                : "Anonymous Visitor";
            return (object)new { l.Id, displayName, l.IsLike, l.CreatedAt };
        }).ToList();

        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
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

                // Notifications for this branch were removed: occupied-group price changes now go through
                // the flat-edit approval flow (PortalFlatController.LandlordFinalEditApproval, "ScheduleRentChange"),
                // which sends the equivalent notice on approval. This endpoint's occupied-house branch is no
                // longer called by the frontend, so removing the notification here doesn't affect the
                // still-used immediate branch above (never-occupied houses).
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
            }
        }
        await _context.SaveChangesAsync();
        return Ok(ApiResponse<object>.SuccessResponse(results));
    }
}
