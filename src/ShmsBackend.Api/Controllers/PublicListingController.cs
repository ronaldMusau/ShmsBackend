using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

public class LikeDto { public bool IsLike { get; set; } }
public class RateDto { public int Stars { get; set; } }
public class CommentBodyDto { public string Comment { get; set; } = string.Empty; }

[ApiController]
[Route("api/public/listings")]
public class PublicListingController : ControllerBase
{
    private readonly ShmsDbContext _context;

    public PublicListingController(ShmsDbContext context)
    {
        _context = context;
    }

    // GET /api/public/listings
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetListings(
        [FromQuery] string? county,
        [FromQuery] string? constituency,
        [FromQuery] string? ward,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = _context.Houses
            .Include(h => h.Flat)
            .Include(h => h.Images)
            .Include(h => h.HouseTypeRef)
            .Where(h => h.OccupancyStatus == OccupancyStatus.Vacant
                     && !h.IsListingHidden
                     && h.Images.Any());

        if (!string.IsNullOrEmpty(county))
            query = query.Where(h => h.Flat != null && h.Flat.County == county);
        if (!string.IsNullOrEmpty(constituency))
            query = query.Where(h => h.Flat != null && h.Flat.Constituency == constituency);
        if (!string.IsNullOrEmpty(ward))
            query = query.Where(h => h.Flat != null && h.Flat.Ward == ward);

        var total = await query.CountAsync();
        var houses = await query
            .OrderByDescending(h => h.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var houseIds = houses.Select(h => h.Id).ToList();
        var flatIds = houses.Select(h => h.FlatId).Distinct().ToList();

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

        var agentFlats = await _context.AgentFlats
            .Include(af => af.Agent)
            .Where(af => flatIds.Contains(af.FlatId))
            .ToListAsync();

        var likeDict = likeCounts.Where(l => l.IsLike).ToDictionary(l => l.HouseId, l => l.Count);
        var dislikeDict = likeCounts.Where(l => !l.IsLike).ToDictionary(l => l.HouseId, l => l.Count);
        var ratingDict = ratings.ToDictionary(r => r.HouseId, r => (double?)r.Avg);
        var agentDict = agentFlats
            .GroupBy(af => af.FlatId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(af => af.AssignedAt).First().Agent);

        var data = houses.Select(h =>
        {
            likeDict.TryGetValue(h.Id, out var likeCount);
            dislikeDict.TryGetValue(h.Id, out var dislikeCount);
            ratingDict.TryGetValue(h.Id, out var avgRating);
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
                images = h.Images.OrderBy(i => i.SortOrder).Select(i => i.ImagePath).ToList(),
                avgRating,
                likeCount,
                dislikeCount,
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

    // GET /api/public/listings/upcoming
    [HttpGet("upcoming")]
    [AllowAnonymous]
    public async Task<IActionResult> GetUpcomingListings(
        [FromQuery] string? county,
        [FromQuery] string? constituency,
        [FromQuery] string? ward,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = _context.Houses
            .Include(h => h.Flat)
            .Include(h => h.Images)
            .Include(h => h.HouseTypeRef)
            .Where(h => h.OccupancyStatus == OccupancyStatus.Occupied
                     && !h.IsListingHidden
                     && h.Images.Any()
                     && _context.VacateRequests
                         .Any(v => v.HouseId == h.Id && v.Status == "Approved" && !v.IsDeleted));

        if (!string.IsNullOrEmpty(county))
            query = query.Where(h => h.Flat != null && h.Flat.County == county);
        if (!string.IsNullOrEmpty(constituency))
            query = query.Where(h => h.Flat != null && h.Flat.Constituency == constituency);
        if (!string.IsNullOrEmpty(ward))
            query = query.Where(h => h.Flat != null && h.Flat.Ward == ward);

        var total = await query.CountAsync();
        var houses = await query
            .OrderByDescending(h => h.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var houseIds = houses.Select(h => h.Id).ToList();
        var flatIds = houses.Select(h => h.FlatId).Distinct().ToList();

        var vacateData = await _context.VacateRequests
            .Where(v => houseIds.Contains(v.HouseId) && v.Status == "Approved" && !v.IsDeleted)
            .Select(v => new { v.HouseId, v.VacateMonth, v.VacateYear, v.CreatedAt })
            .ToListAsync();

        var vacateDict = vacateData
            .GroupBy(v => v.HouseId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.CreatedAt).First());

        var agentFlats = await _context.AgentFlats
            .Include(af => af.Agent)
            .Where(af => flatIds.Contains(af.FlatId))
            .ToListAsync();

        var agentDict = agentFlats
            .GroupBy(af => af.FlatId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(af => af.AssignedAt).First().Agent);

        var data = houses.Select(h =>
        {
            agentDict.TryGetValue(h.FlatId, out var agent);
            vacateDict.TryGetValue(h.Id, out var vacate);
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
                images = h.Images.OrderBy(i => i.SortOrder).Select(i => i.ImagePath).ToList(),
                availableFromMonth = vacate?.VacateMonth,
                availableFromYear = vacate?.VacateYear,
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

    // GET /api/public/listings/{id}
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetListing(Guid id)
    {
        var house = await _context.Houses
            .Include(h => h.Flat)
            .Include(h => h.Images)
            .Include(h => h.HouseTypeRef)
            .FirstOrDefaultAsync(h => h.Id == id);

        if (house == null
            || house.OccupancyStatus != OccupancyStatus.Vacant
            || house.IsListingHidden
            || !house.Images.Any())
            return NotFound(new { success = false, message = "Listing not found." });

        var likeCount = await _context.HouseListingLikes.CountAsync(l => l.HouseId == id && l.IsLike);
        var dislikeCount = await _context.HouseListingLikes.CountAsync(l => l.HouseId == id && !l.IsLike);
        var avgRating = await _context.HouseListingRatings
            .Where(r => r.HouseId == id)
            .AverageAsync(r => (double?)r.Stars);

        var comments = await _context.HouseListingComments
            .Where(c => c.HouseId == id && !c.IsHidden)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Id, c.CommenterName, c.Comment, c.CreatedAt })
            .ToListAsync();

        var agentFlat = await _context.AgentFlats
            .Include(af => af.Agent)
            .Where(af => af.FlatId == house.FlatId)
            .OrderByDescending(af => af.AssignedAt)
            .FirstOrDefaultAsync();

        bool? myLike = null;
        int? myRating = null;
        if (User.Identity?.IsAuthenticated == true && User.IsInRole("Explorer"))
        {
            var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(idStr, out var eid))
            {
                var likeRecord = await _context.HouseListingLikes
                    .FirstOrDefaultAsync(l => l.HouseId == id && l.ExplorerId == eid);
                if (likeRecord != null) myLike = likeRecord.IsLike;

                var ratingRecord = await _context.HouseListingRatings
                    .FirstOrDefaultAsync(r => r.HouseId == id && r.ExplorerId == eid);
                if (ratingRecord != null) myRating = ratingRecord.Stars;
            }
        }

        var agent = agentFlat?.Agent;
        return Ok(new
        {
            success = true,
            data = new
            {
                id = house.Id,
                houseNumber = house.HouseNumber,
                houseType = house.HouseTypeRef?.Name,
                flatName = house.Flat?.FlatName,
                rentFee = house.RentFee,
                depositFee = house.DepositFee,
                county = house.Flat?.County,
                constituency = house.Flat?.Constituency,
                ward = house.Flat?.Ward,
                googleMapsLink = house.Flat?.GoogleMapsLink,
                images = house.Images.OrderBy(i => i.SortOrder).Select(i => i.ImagePath).ToList(),
                avgRating,
                likeCount,
                dislikeCount,
                comments,
                agent = agent == null ? null : new
                {
                    name = $"{agent.FirstName} {agent.LastName}".Trim(),
                    phone = agent.PhoneNumber,
                    agencyName = agent.AgencyName
                },
                myLike,
                myRating
            }
        });
    }

    // GET /api/public/listings/{id}/comments
    [HttpGet("{id:guid}/comments")]
    [AllowAnonymous]
    public async Task<IActionResult> GetComments(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var exists = await _context.Houses.AnyAsync(h => h.Id == id);
        if (!exists) return NotFound(new { success = false, message = "Listing not found." });

        var query = _context.HouseListingComments
            .Where(c => c.HouseId == id && !c.IsHidden);

        var total = await query.CountAsync();
        var comments = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new { c.Id, c.CommenterName, c.Comment, c.CreatedAt })
            .ToListAsync();

        return Ok(new
        {
            success = true,
            data = comments,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    // POST /api/public/listings/{id}/like
    [HttpPost("{id:guid}/like")]
    [Authorize(Roles = "Explorer")]
    public async Task<IActionResult> Like(Guid id, [FromBody] LikeDto dto)
    {
        var exists = await _context.Houses
            .AnyAsync(h => h.Id == id && h.OccupancyStatus == OccupancyStatus.Vacant && !h.IsListingHidden);
        if (!exists) return NotFound(new { success = false, message = "Listing not found." });

        var explorerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var existing = await _context.HouseListingLikes
            .FirstOrDefaultAsync(l => l.HouseId == id && l.ExplorerId == explorerId);

        if (existing != null)
        {
            existing.IsLike = dto.IsLike;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.HouseListingLikes.Add(new HouseListingLike
            {
                HouseId = id,
                ExplorerId = explorerId,
                IsLike = dto.IsLike
            });
        }
        await _context.SaveChangesAsync();

        var likeCount = await _context.HouseListingLikes.CountAsync(l => l.HouseId == id && l.IsLike);
        var dislikeCount = await _context.HouseListingLikes.CountAsync(l => l.HouseId == id && !l.IsLike);
        return Ok(new { success = true, data = new { likeCount, dislikeCount } });
    }

    // POST /api/public/listings/{id}/rate
    [HttpPost("{id:guid}/rate")]
    [Authorize(Roles = "Explorer")]
    public async Task<IActionResult> Rate(Guid id, [FromBody] RateDto dto)
    {
        if (dto.Stars < 1 || dto.Stars > 5)
            return BadRequest(new { success = false, message = "Stars must be between 1 and 5." });

        var exists = await _context.Houses
            .AnyAsync(h => h.Id == id && h.OccupancyStatus == OccupancyStatus.Vacant && !h.IsListingHidden);
        if (!exists) return NotFound(new { success = false, message = "Listing not found." });

        var explorerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var existing = await _context.HouseListingRatings
            .FirstOrDefaultAsync(r => r.HouseId == id && r.ExplorerId == explorerId);

        if (existing != null)
        {
            existing.Stars = dto.Stars;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.HouseListingRatings.Add(new HouseListingRating
            {
                HouseId = id,
                ExplorerId = explorerId,
                Stars = dto.Stars
            });
        }
        await _context.SaveChangesAsync();

        var avgRating = await _context.HouseListingRatings
            .Where(r => r.HouseId == id)
            .AverageAsync(r => (double?)r.Stars);
        return Ok(new { success = true, data = new { avgRating } });
    }

    // POST /api/public/listings/{id}/comments
    [HttpPost("{id:guid}/comments")]
    [Authorize(Roles = "Explorer")]
    public async Task<IActionResult> AddComment(Guid id, [FromBody] CommentBodyDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new { success = false, message = "Comment cannot be empty." });

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == id);
        if (house == null || house.OccupancyStatus != OccupancyStatus.Vacant || house.IsListingHidden)
            return NotFound(new { success = false, message = "Listing not found." });

        if (house.CommentsMuted)
            return BadRequest(new { success = false, message = "Comments are currently disabled for this listing." });

        var explorerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var explorer = await _context.Explorers.FindAsync(explorerId);
        if (explorer == null) return Unauthorized();

        var comment = new HouseListingComment
        {
            HouseId = id,
            ExplorerId = explorerId,
            CommenterName = $"{explorer.FirstName} {explorer.LastName}".Trim(),
            Comment = dto.Comment.Trim()
        };
        _context.HouseListingComments.Add(comment);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            data = new { comment.Id, comment.CommenterName, comment.Comment, comment.CreatedAt }
        });
    }

    // PATCH /api/public/listings/comments/{commentId}/hide
    [HttpPatch("comments/{commentId:guid}/hide")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager")]
    public async Task<IActionResult> HideComment(Guid commentId)
    {
        var comment = await _context.HouseListingComments.FindAsync(commentId);
        if (comment == null) return NotFound(new { success = false, message = "Comment not found." });

        comment.IsHidden = true;
        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Comment hidden." });
    }
}
