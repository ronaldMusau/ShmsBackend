using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services;

public class SessionFilters
{
    public string? Status { get; set; }
    public Guid? AgentId { get; set; }
    public Guid? FlatId { get; set; }   // resolved via a House-id subquery, no direct column
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

/// <summary>
/// Single source of truth for filtered-viewing-session queries, mirroring SessionController.GetAllSessions'
/// exact status/agentId/date filters. ListingViewingSession has no FlatId column, so FlatId filters via
/// a House-id subquery — the same relationship SessionController itself resolves House->Flat through.
/// </summary>
public class ListingViewingSessionQueryService
{
    private readonly ShmsDbContext _context;

    public ListingViewingSessionQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<ListingViewingSession> BuildFilteredQuery(SessionFilters filters)
    {
        var query = _context.ListingViewingSessions.AsQueryable();

        if (!string.IsNullOrEmpty(filters.Status))
            query = query.Where(s => s.Status == filters.Status);

        if (filters.AgentId.HasValue)
            query = query.Where(s => s.AgentId == filters.AgentId.Value);

        if (filters.FlatId.HasValue)
        {
            var houseIdsInFlat = _context.Houses
                .Where(h => h.FlatId == filters.FlatId.Value)
                .Select(h => h.Id);
            query = query.Where(s => houseIdsInFlat.Contains(s.HouseId));
        }

        if (filters.FromDate.HasValue)
            query = query.Where(s => s.ScheduledAt >= filters.FromDate.Value.Date);

        if (filters.ToDate.HasValue)
            query = query.Where(s => s.ScheduledAt < filters.ToDate.Value.Date.AddDays(1));

        return query;
    }
}
