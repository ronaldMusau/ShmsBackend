using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services;

public class AgentFilters
{
    public string? Search { get; set; }
    public string? County { get; set; }
    public bool? IsActive { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

/// <summary>
/// Single source of truth for filtered-agent queries. Same reasoning as LandlordQueryService —
/// AgentController.GetAll has no filter logic to mirror (bare ToListAsync() via the generic
/// repository), so this is a fresh, DB-level IQueryable built for the reports feature.
/// </summary>
public class AgentQueryService
{
    private readonly ShmsDbContext _context;

    public AgentQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<Agent> BuildFilteredQuery(AgentFilters filters)
    {
        var query = _context.Agents.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var search = filters.Search.Trim();
            query = query.Where(a =>
                EF.Functions.Like(a.FirstName, $"%{search}%") ||
                EF.Functions.Like(a.LastName, $"%{search}%") ||
                EF.Functions.Like(a.Email, $"%{search}%") ||
                (a.AgencyName != null && EF.Functions.Like(a.AgencyName, $"%{search}%")));
        }

        if (!string.IsNullOrWhiteSpace(filters.County))
            query = query.Where(a => a.County == filters.County);

        if (filters.IsActive.HasValue)
            query = query.Where(a => a.IsActive == filters.IsActive.Value);

        if (filters.FromDate.HasValue)
            query = query.Where(a => a.CreatedAt >= filters.FromDate.Value);

        if (filters.ToDate.HasValue)
            query = query.Where(a => a.CreatedAt <= filters.ToDate.Value.AddDays(1));

        return query;
    }
}
