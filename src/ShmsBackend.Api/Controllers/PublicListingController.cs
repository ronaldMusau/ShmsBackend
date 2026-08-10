using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

public class LikeDto { public bool IsLike { get; set; } public string? DeviceId { get; set; } }
public class RateDto { public int Stars { get; set; } public string? DeviceId { get; set; } }
public class CommentBodyDto { public string Comment { get; set; } = string.Empty; public string? DeviceId { get; set; } }
public class MergeDeviceDto { public string DeviceId { get; set; } = string.Empty; }

[ApiController]
[Route("api/public/listings")]
public class PublicListingController : ControllerBase
{
    private readonly ShmsDbContext _context;

    private static readonly string[] AnonymousCreatures =
    {
        "Mongoose", "Lemur", "Otter", "Heron", "Civet", "Genet", "Pangolin", "Serval",
        "Aardvark", "Meerkat", "Cricket", "Mantis", "Beetle", "Firefly", "Dragonfly",
        "Gecko", "Chameleon", "Hornbill", "Kingfisher", "Weaver"
    };

    private static string GetAnonymousDisplayName(Guid explorerId, Guid houseId)
    {
        var combined = $"{explorerId}-{houseId}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(combined));
        var creatureIndex = hash[0] % AnonymousCreatures.Length;
        var number = (BitConverter.ToUInt16(hash, 1) % 9000) + 1000;
        return $"Anonymous {AnonymousCreatures[creatureIndex]} {number}";
    }

    private static string GetAnonymousDisplayName(string deviceId, Guid houseId)
    {
        var combined = $"{deviceId}-{houseId}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(combined));
        var creatureIndex = hash[0] % AnonymousCreatures.Length;
        var number = (BitConverter.ToUInt16(hash, 1) % 9000) + 1000;
        return $"Anonymous {AnonymousCreatures[creatureIndex]} {number}";
    }

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
        [FromQuery] string? sort,
        [FromQuery] string? houseType,
        [FromQuery] decimal? minRent,
        [FromQuery] decimal? maxRent,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var baseQuery = _context.Houses
            .Include(h => h.Flat)
            .Include(h => h.Images)
            .Include(h => h.HouseTypeRef)
            .Where(h => h.OccupancyStatus == OccupancyStatus.Vacant
                     && !h.IsListingHidden
                     && h.Images.Any());

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

        var total = await baseQuery.CountAsync();

        List<House> pagedHouses;
        Dictionary<Guid, int> likeDict;
        Dictionary<Guid, int> dislikeDict;
        Dictionary<Guid, double?> ratingDict;

        if (sort == "popular" || sort == "trending")
        {
            // Load all filtered IDs + CreatedAt (for stable secondary sort)
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

            // Restore explicit sort order
            pagedHouses = pageIds.Select(id => pageHouseList.First(h => h.Id == id)).ToList();

            likeDict = sortLikeDict.Where(kv => pageIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            dislikeDict = sortDislikeDict.Where(kv => pageIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            ratingDict = sortRatingDict.Where(kv => pageIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => (double?)kv.Value);
        }
        else
        {
            // "foryou" (default) — newest first, paginate at DB
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

        var flatIds = pagedHouses.Select(h => h.FlatId).Distinct().ToList();
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

    // GET /api/public/listings/my-interactions
    [HttpGet("my-interactions")]
    [Authorize(Roles = "Explorer")]
    public async Task<IActionResult> GetMyInteractions(
        [FromQuery] string? county,
        [FromQuery] string? constituency,
        [FromQuery] string? ward,
        [FromQuery] string? houseType,
        [FromQuery] decimal? minRent,
        [FromQuery] decimal? maxRent,
        [FromQuery] int? minRating,
        [FromQuery] string? reaction,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 6)
    {
        var explorerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var likeRecords = await _context.HouseListingLikes
            .Where(l => l.ExplorerId == explorerId)
            .Select(l => new { l.HouseId, l.IsLike, Timestamp = l.UpdatedAt })
            .ToListAsync();

        var ratingRecords = await _context.HouseListingRatings
            .Where(r => r.ExplorerId == explorerId)
            .Select(r => new { r.HouseId, r.Stars, Timestamp = r.UpdatedAt })
            .ToListAsync();

        var commentRecords = await _context.HouseListingComments
            .Where(c => c.ExplorerId == explorerId && !c.IsHidden)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.HouseId, c.Comment, c.CreatedAt })
            .ToListAsync();

        var bookmarkRecords = await _context.HouseListingBookmarks
            .Where(b => b.ExplorerId == explorerId)
            .Select(b => new { b.HouseId, b.CreatedAt })
            .ToListAsync();

        var allHouseIds = likeRecords.Select(l => l.HouseId)
            .Union(ratingRecords.Select(r => r.HouseId))
            .Union(commentRecords.Select(c => c.HouseId))
            .Union(bookmarkRecords.Select(b => b.HouseId))
            .Distinct().ToList();

        if (!allHouseIds.Any())
            return Ok(new { success = true, data = Array.Empty<object>(), total = 0, page, pageSize, totalPages = 0 });

        var housesQuery = _context.Houses
            .Include(h => h.Flat)
            .Include(h => h.Images)
            .Include(h => h.HouseTypeRef)
            .Where(h => allHouseIds.Contains(h.Id));

        if (!string.IsNullOrEmpty(county))
            housesQuery = housesQuery.Where(h => h.Flat != null && h.Flat.County == county);
        if (!string.IsNullOrEmpty(constituency))
            housesQuery = housesQuery.Where(h => h.Flat != null && h.Flat.Constituency == constituency);
        if (!string.IsNullOrEmpty(ward))
            housesQuery = housesQuery.Where(h => h.Flat != null && h.Flat.Ward == ward);
        if (!string.IsNullOrEmpty(houseType))
            housesQuery = housesQuery.Where(h => h.HouseTypeRef != null && h.HouseTypeRef.Name == houseType);
        if (minRent.HasValue)
            housesQuery = housesQuery.Where(h => h.RentFee >= minRent.Value);
        if (maxRent.HasValue)
            housesQuery = housesQuery.Where(h => h.RentFee <= maxRent.Value);

        var houses = await housesQuery.ToListAsync();

        var likeCounts = await _context.HouseListingLikes
            .Where(l => allHouseIds.Contains(l.HouseId))
            .GroupBy(l => new { l.HouseId, l.IsLike })
            .Select(g => new { g.Key.HouseId, g.Key.IsLike, Count = g.Count() })
            .ToListAsync();

        var ratings = await _context.HouseListingRatings
            .Where(r => allHouseIds.Contains(r.HouseId))
            .GroupBy(r => r.HouseId)
            .Select(g => new { HouseId = g.Key, Avg = g.Average(r => (double)r.Stars) })
            .ToListAsync();

        var likeDict = likeCounts.Where(l => l.IsLike).ToDictionary(l => l.HouseId, l => l.Count);
        var dislikeDict = likeCounts.Where(l => !l.IsLike).ToDictionary(l => l.HouseId, l => l.Count);
        var ratingDict = ratings.ToDictionary(r => r.HouseId, r => (double?)r.Avg);

        var myLikeDict = likeRecords.ToDictionary(l => l.HouseId, l => (bool?)l.IsLike);
        var myRatingDict = ratingRecords.ToDictionary(r => r.HouseId, r => (int?)r.Stars);
        var myCommentDict = commentRecords
            .GroupBy(c => c.HouseId)
            .ToDictionary(g => g.Key, g => g.First().Comment);

        var myBookmarkSet = bookmarkRecords.Select(b => b.HouseId).ToHashSet();

        var latestInteractionDict = allHouseIds.ToDictionary(hid => hid, hid =>
        {
            var timestamps = new List<DateTime>();
            var like = likeRecords.FirstOrDefault(l => l.HouseId == hid);
            if (like != null) timestamps.Add(like.Timestamp);
            var rating = ratingRecords.FirstOrDefault(r => r.HouseId == hid);
            if (rating != null) timestamps.Add(rating.Timestamp);
            var comment = commentRecords.FirstOrDefault(c => c.HouseId == hid);
            if (comment != null) timestamps.Add(comment.CreatedAt);
            var bookmark = bookmarkRecords.FirstOrDefault(b => b.HouseId == hid);
            if (bookmark != null) timestamps.Add(bookmark.CreatedAt);
            return timestamps.Any() ? timestamps.Max() : DateTime.MinValue;
        });

        var enriched = houses.Select(h =>
        {
            likeDict.TryGetValue(h.Id, out var likeCount);
            dislikeDict.TryGetValue(h.Id, out var dislikeCount);
            ratingDict.TryGetValue(h.Id, out var avgRating);
            myLikeDict.TryGetValue(h.Id, out var myLike);
            myRatingDict.TryGetValue(h.Id, out var myRating);
            myCommentDict.TryGetValue(h.Id, out var myComment);
            var myBookmark = myBookmarkSet.Contains(h.Id);
            var isAvailable = h.OccupancyStatus == OccupancyStatus.Vacant && !h.IsListingHidden && h.Images.Any();
            return new { h, likeCount, dislikeCount, avgRating, myLike, myRating, myComment, myBookmark, isAvailable };
        }).AsEnumerable();

        if (minRating.HasValue)
            enriched = enriched.Where(x => x.myRating.HasValue && x.myRating.Value >= minRating.Value);
        if (!string.IsNullOrEmpty(reaction))
        {
            enriched = reaction switch
            {
                "Liked" => enriched.Where(x => x.myLike == true),
                "Disliked" => enriched.Where(x => x.myLike == false),
                "Commented" => enriched.Where(x => x.myComment != null),
                "Bookmarked" => enriched.Where(x => x.myBookmark),
                _ => enriched
            };
        }

        var filteredList = enriched.ToList();
        var total = filteredList.Count;

        var data = filteredList
            .OrderByDescending(x => x.h.OccupancyStatus == OccupancyStatus.Vacant && !x.h.IsListingHidden && x.h.Images.Any())
            .ThenByDescending(x => latestInteractionDict.GetValueOrDefault(x.h.Id))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => (object)new
            {
                id = x.h.Id,
                houseNumber = x.h.HouseNumber,
                houseType = x.h.HouseTypeRef?.Name,
                flatName = x.h.Flat?.FlatName,
                rentFee = x.h.RentFee,
                county = x.h.Flat?.County,
                constituency = x.h.Flat?.Constituency,
                ward = x.h.Flat?.Ward,
                images = x.h.Images.OrderBy(i => i.SortOrder).Select(i => i.ImagePath).ToList(),
                avgRating = x.avgRating,
                likeCount = x.likeCount,
                dislikeCount = x.dislikeCount,
                myLike = x.myLike,
                myRating = x.myRating,
                myComment = x.myComment,
                myBookmark = x.myBookmark,
                isAvailable = x.isAvailable
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
    public async Task<IActionResult> GetListing(Guid id, [FromQuery] string? deviceId)
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
        bool myBookmark = false;
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

                myBookmark = await _context.HouseListingBookmarks
                    .AnyAsync(b => b.HouseId == id && b.ExplorerId == eid);
            }
        }
        else if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var likeRecord = await _context.HouseListingLikes
                .FirstOrDefaultAsync(l => l.HouseId == id && l.AnonymousDeviceId == deviceId);
            if (likeRecord != null) myLike = likeRecord.IsLike;

            var ratingRecord = await _context.HouseListingRatings
                .FirstOrDefaultAsync(r => r.HouseId == id && r.AnonymousDeviceId == deviceId);
            if (ratingRecord != null) myRating = ratingRecord.Stars;
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
                myRating,
                myBookmark
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

        var rawComments = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new { c.Id, c.ExplorerId, c.HouseId, c.CommenterName, c.Comment, c.CreatedAt })
            .ToListAsync();

        var comments = rawComments.Select(c => new
        {
            c.Id,
            displayName = c.ExplorerId.HasValue
                ? GetAnonymousDisplayName(c.ExplorerId.Value, c.HouseId)
                : c.CommenterName,
            c.Comment,
            c.CreatedAt
        }).ToList();

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
    [AllowAnonymous]
    public async Task<IActionResult> Like(Guid id, [FromBody] LikeDto dto)
    {
        var exists = await _context.Houses
            .AnyAsync(h => h.Id == id && h.OccupancyStatus == OccupancyStatus.Vacant && !h.IsListingHidden);
        if (!exists) return NotFound(new { success = false, message = "Listing not found." });

        var idStr = User.Identity?.IsAuthenticated == true && User.IsInRole("Explorer")
            ? User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;
        Guid? explorerId = Guid.TryParse(idStr, out var eid) ? eid : null;

        if (explorerId == null)
        {
            if (string.IsNullOrWhiteSpace(dto.DeviceId))
                return BadRequest(new { success = false, message = "DeviceId is required for anonymous interactions." });
        }

        HouseListingLike? existing;
        if (explorerId != null)
            existing = await _context.HouseListingLikes
                .FirstOrDefaultAsync(l => l.HouseId == id && l.ExplorerId == explorerId);
        else
            existing = await _context.HouseListingLikes
                .FirstOrDefaultAsync(l => l.HouseId == id && l.AnonymousDeviceId == dto.DeviceId);

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
                AnonymousDeviceId = explorerId == null ? dto.DeviceId : null,
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
    [AllowAnonymous]
    public async Task<IActionResult> Rate(Guid id, [FromBody] RateDto dto)
    {
        if (dto.Stars < 1 || dto.Stars > 5)
            return BadRequest(new { success = false, message = "Stars must be between 1 and 5." });

        var exists = await _context.Houses
            .AnyAsync(h => h.Id == id && h.OccupancyStatus == OccupancyStatus.Vacant && !h.IsListingHidden);
        if (!exists) return NotFound(new { success = false, message = "Listing not found." });

        var idStr = User.Identity?.IsAuthenticated == true && User.IsInRole("Explorer")
            ? User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;
        Guid? explorerId = Guid.TryParse(idStr, out var eid) ? eid : null;

        if (explorerId == null)
        {
            if (string.IsNullOrWhiteSpace(dto.DeviceId))
                return BadRequest(new { success = false, message = "DeviceId is required for anonymous interactions." });
        }

        HouseListingRating? existing;
        if (explorerId != null)
            existing = await _context.HouseListingRatings
                .FirstOrDefaultAsync(r => r.HouseId == id && r.ExplorerId == explorerId);
        else
            existing = await _context.HouseListingRatings
                .FirstOrDefaultAsync(r => r.HouseId == id && r.AnonymousDeviceId == dto.DeviceId);

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
                AnonymousDeviceId = explorerId == null ? dto.DeviceId : null,
                Stars = dto.Stars
            });
        }
        await _context.SaveChangesAsync();

        var avgRating = await _context.HouseListingRatings
            .Where(r => r.HouseId == id)
            .AverageAsync(r => (double?)r.Stars);
        return Ok(new { success = true, data = new { avgRating } });
    }

    // POST /api/public/listings/{id}/bookmark
    [HttpPost("{id:guid}/bookmark")]
    [Authorize(Roles = "Explorer")]
    public async Task<IActionResult> Bookmark(Guid id)
    {
        var explorerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var existing = await _context.HouseListingBookmarks
            .FirstOrDefaultAsync(b => b.HouseId == id && b.ExplorerId == explorerId);

        bool bookmarked;
        if (existing != null)
        {
            _context.HouseListingBookmarks.Remove(existing);
            bookmarked = false;
        }
        else
        {
            _context.HouseListingBookmarks.Add(new HouseListingBookmark
            {
                HouseId = id,
                ExplorerId = explorerId
            });
            bookmarked = true;
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true, data = new { bookmarked } });
    }

    // POST /api/public/listings/merge-device
    [HttpPost("merge-device")]
    [Authorize(Roles = "Explorer")]
    public async Task<IActionResult> MergeDevice([FromBody] MergeDeviceDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.DeviceId))
            return BadRequest(new { success = false, message = "DeviceId is required." });

        var explorerId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Likes
        var anonLikes = await _context.HouseListingLikes
            .Where(l => l.AnonymousDeviceId == dto.DeviceId)
            .ToListAsync();

        var anonLikeHouseIds = anonLikes.Select(l => l.HouseId).ToList();
        var explorerLikeHouseIds = await _context.HouseListingLikes
            .Where(l => l.ExplorerId == explorerId && anonLikeHouseIds.Contains(l.HouseId))
            .Select(l => l.HouseId)
            .ToListAsync();

        int mergedLikes = anonLikes.Count;
        foreach (var anonLike in anonLikes)
        {
            if (explorerLikeHouseIds.Contains(anonLike.HouseId))
                _context.HouseListingLikes.Remove(anonLike);
            else
            {
                anonLike.ExplorerId = explorerId;
                anonLike.AnonymousDeviceId = null;
            }
        }

        // Ratings
        var anonRatings = await _context.HouseListingRatings
            .Where(r => r.AnonymousDeviceId == dto.DeviceId)
            .ToListAsync();

        var anonRatingHouseIds = anonRatings.Select(r => r.HouseId).ToList();
        var explorerRatingHouseIds = await _context.HouseListingRatings
            .Where(r => r.ExplorerId == explorerId && anonRatingHouseIds.Contains(r.HouseId))
            .Select(r => r.HouseId)
            .ToListAsync();

        int mergedRatings = anonRatings.Count;
        foreach (var anonRating in anonRatings)
        {
            if (explorerRatingHouseIds.Contains(anonRating.HouseId))
                _context.HouseListingRatings.Remove(anonRating);
            else
            {
                anonRating.ExplorerId = explorerId;
                anonRating.AnonymousDeviceId = null;
            }
        }

        // Comments
        var anonComments = await _context.HouseListingComments
            .Where(c => c.AnonymousDeviceId == dto.DeviceId)
            .ToListAsync();

        var explorerForName = await _context.Explorers.FindAsync(explorerId);
        var explorerName = explorerForName != null
            ? $"{explorerForName.FirstName} {explorerForName.LastName}".Trim()
            : string.Empty;

        int mergedComments = anonComments.Count;
        foreach (var anonComment in anonComments)
        {
            anonComment.ExplorerId = explorerId;
            anonComment.AnonymousDeviceId = null;
            anonComment.CommenterName = explorerName;
        }

        await _context.SaveChangesAsync();

        return Ok(new { success = true, mergedLikes, mergedRatings, mergedComments });
    }

    // POST /api/public/listings/{id}/comments
    [HttpPost("{id:guid}/comments")]
    [AllowAnonymous]
    public async Task<IActionResult> AddComment(Guid id, [FromBody] CommentBodyDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Comment))
            return BadRequest(new { success = false, message = "Comment cannot be empty." });

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == id);
        if (house == null || house.OccupancyStatus != OccupancyStatus.Vacant || house.IsListingHidden)
            return NotFound(new { success = false, message = "Listing not found." });

        if (house.CommentsMuted)
            return BadRequest(new { success = false, message = "Comments are currently disabled for this listing." });

        HouseListingComment comment;

        var idStr = User.Identity?.IsAuthenticated == true && User.IsInRole("Explorer")
            ? User.FindFirstValue(ClaimTypes.NameIdentifier)
            : null;
        Guid? explorerId = Guid.TryParse(idStr, out var eid) ? eid : null;

        if (explorerId != null)
        {
            var explorer = await _context.Explorers.FindAsync(explorerId.Value);
            if (explorer == null) return Unauthorized();

            comment = new HouseListingComment
            {
                HouseId = id,
                ExplorerId = explorerId.Value,
                CommenterName = $"{explorer.FirstName} {explorer.LastName}".Trim(),
                Comment = dto.Comment.Trim()
            };
        }
        else
        {
            if (string.IsNullOrWhiteSpace(dto.DeviceId))
                return BadRequest(new { success = false, message = "DeviceId is required for anonymous interactions." });

            comment = new HouseListingComment
            {
                HouseId = id,
                AnonymousDeviceId = dto.DeviceId,
                CommenterName = GetAnonymousDisplayName(dto.DeviceId, id),
                Comment = dto.Comment.Trim()
            };
        }

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
