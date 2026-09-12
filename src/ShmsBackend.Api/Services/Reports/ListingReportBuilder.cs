using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Api.Services;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Reports;

/// <summary>
/// Builds public-listing-moderation ReportData off the same HouseQueryService/HouseFilters as
/// HouseReportBuilder — there is no dedicated "Listing" entity, the public listing IS the House row
/// plus its like/rating/comment satellite tables. Replicates HouseController.GetAllListingStats' batch
/// like/dislike/avg-rating/comment-count/assigned-agent lookups using the query's DEFAULT sort
/// (OrderByDescending(CreatedAt)) only — the popular/trending in-memory sort in that controller is a
/// live-UI ranking concern, not report data, and is deliberately not replicated here.
/// </summary>
public class ListingReportBuilder
{
    private readonly HouseQueryService _houseQueryService;
    private readonly ShmsDbContext _context;

    public ListingReportBuilder(HouseQueryService houseQueryService, ShmsDbContext context)
    {
        _houseQueryService = houseQueryService;
        _context = context;
    }

    public async Task<ReportData> BuildAsync(HouseFilters filters)
    {
        var houses = await _houseQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync();

        var houseIds = houses.Select(h => h.Id).ToList();
        var flatIds = houses.Select(h => h.FlatId).Distinct().ToList();

        var likeCounts = await _context.HouseListingLikes
            .Where(l => houseIds.Contains(l.HouseId))
            .GroupBy(l => new { l.HouseId, l.IsLike })
            .Select(g => new { g.Key.HouseId, g.Key.IsLike, Count = g.Count() })
            .ToListAsync();
        var likeDict = likeCounts.Where(l => l.IsLike).ToDictionary(l => l.HouseId, l => l.Count);
        var dislikeDict = likeCounts.Where(l => !l.IsLike).ToDictionary(l => l.HouseId, l => l.Count);

        var ratings = await _context.HouseListingRatings
            .Where(r => houseIds.Contains(r.HouseId))
            .GroupBy(r => r.HouseId)
            .Select(g => new { HouseId = g.Key, Avg = g.Average(r => (double)r.Stars) })
            .ToListAsync();
        var ratingDict = ratings.ToDictionary(r => r.HouseId, r => (double?)r.Avg);

        var commentCounts = await _context.HouseListingComments
            .Where(c => houseIds.Contains(c.HouseId))
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

        var rows = houses.Select(h =>
        {
            likeDict.TryGetValue(h.Id, out var likeCount);
            dislikeDict.TryGetValue(h.Id, out var dislikeCount);
            ratingDict.TryGetValue(h.Id, out var avgRating);
            commentCountDict.TryGetValue(h.Id, out var commentCount);
            agentDict.TryGetValue(h.FlatId, out var agent);

            var isPubliclyVisible = h.OccupancyStatus == OccupancyStatus.Vacant
                                  && !h.IsListingHidden
                                  && h.Images.Any();

            return new Dictionary<string, object?>
            {
                ["houseNumber"] = h.HouseNumber,
                ["flatName"] = h.Flat?.FlatName,
                ["county"] = h.Flat?.County,
                ["rent"] = h.RentFee,
                ["isListingHidden"] = h.IsListingHidden ? "Yes" : "No",
                ["commentsMuted"] = h.CommentsMuted ? "Yes" : "No",
                ["assignedAgent"] = agent != null ? $"{agent.FirstName} {agent.LastName}".Trim() : "",
                ["likeCount"] = likeCount,
                ["dislikeCount"] = dislikeCount,
                ["avgRating"] = avgRating,
                ["commentCount"] = commentCount,
                ["isPubliclyVisible"] = isPubliclyVisible ? "Yes" : "No"
            };
        }).ToList();

        var flatName = await ReportBuilderHelpers.ResolveFlatNameAsync(_context, filters.FlatId);

        return new ReportData
        {
            Title = "Listings Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters, flatName),
            Columns = new List<ReportColumn>
            {
                new() { Key = "houseNumber", Header = "House/Unit" },
                new() { Key = "flatName", Header = "Flat" },
                new() { Key = "county", Header = "County" },
                new() { Key = "rent", Header = "Rent" },
                new() { Key = "isListingHidden", Header = "Hidden" },
                new() { Key = "commentsMuted", Header = "Comments Muted" },
                new() { Key = "assignedAgent", Header = "Assigned Agent" },
                new() { Key = "likeCount", Header = "Likes" },
                new() { Key = "dislikeCount", Header = "Dislikes" },
                new() { Key = "avgRating", Header = "Avg Rating" },
                new() { Key = "commentCount", Header = "Comments" },
                new() { Key = "isPubliclyVisible", Header = "Publicly Visible" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(HouseFilters filters, string? flatName)
    {
        var parts = new List<string>();
        if (filters.FlatId.HasValue) parts.Add($"Flat: {flatName ?? filters.FlatId.ToString()}");
        if (!string.IsNullOrWhiteSpace(filters.County)) parts.Add($"County: {filters.County}");
        if (filters.MinRent.HasValue) parts.Add($"Rent from {filters.MinRent:N0}");
        if (filters.MaxRent.HasValue) parts.Add($"Rent up to {filters.MaxRent:N0}");
        if (filters.IsListingHidden.HasValue) parts.Add($"Hidden: {(filters.IsListingHidden.Value ? "Yes" : "No")}");
        if (filters.CommentsMuted.HasValue) parts.Add($"Comments Muted: {(filters.CommentsMuted.Value ? "Yes" : "No")}");

        return parts.Count == 0 ? "All listings" : string.Join(" | ", parts);
    }
}
