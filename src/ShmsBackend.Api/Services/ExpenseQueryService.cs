using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services;

public class ExpenseFilters
{
    public Guid? FlatId { get; set; }
    public Guid? HouseId { get; set; }
    public int? Year { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public string? Search { get; set; }
    public Guid? LandlordId { get; set; }  // set server-side only — Landlord's own Id when scoping to their log, or explicitly null to scope to Management's log; never bound from a client filter param
}

/// <summary>
/// Single source of truth for filtered-expense queries. Expense has scalar FlatId/HouseId only
/// (no nav properties), so Flat/House name matches in Search use correlated Any() subqueries, same
/// pattern as Vacate/Complaints.
/// </summary>
public class ExpenseQueryService
{
    private readonly ShmsDbContext _context;

    public ExpenseQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<Expense> BuildFilteredQuery(ExpenseFilters filters)
    {
        // LandlordId == null correctly matches Management-logged rows (LandlordId also null) —
        // this is not a bug, it's how the shared scoping field distinguishes the two logs.
        var query = _context.Expenses.Where(x => x.LandlordId == filters.LandlordId).AsQueryable();

        if (filters.FlatId.HasValue) query = query.Where(x => x.FlatId == filters.FlatId.Value);
        if (filters.HouseId.HasValue) query = query.Where(x => x.HouseId == filters.HouseId.Value);
        if (filters.Year.HasValue) query = query.Where(x => x.ExpenseDate.Year == filters.Year.Value);
        if (filters.FromDate.HasValue) query = query.Where(x => x.ExpenseDate >= filters.FromDate.Value);
        if (filters.ToDate.HasValue) query = query.Where(x => x.ExpenseDate <= filters.ToDate.Value.Date);
        if (filters.MinAmount.HasValue) query = query.Where(x => x.Amount >= filters.MinAmount.Value);
        if (filters.MaxAmount.HasValue) query = query.Where(x => x.Amount <= filters.MaxAmount.Value);

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var s = filters.Search.Trim();
            query = query.Where(x =>
                x.Description.Contains(s) ||
                (x.FlatId.HasValue && _context.Flats.Any(f => f.Id == x.FlatId.Value && f.FlatName.Contains(s))) ||
                (x.HouseId.HasValue && _context.Houses.Any(h => h.Id == x.HouseId.Value && h.HouseNumber.Contains(s))) ||
                x.Amount.ToString().Contains(s) ||
                // Parameterless ToString() is what EF Core's SQL Server provider can actually translate
                // to SQL (via CONVERT) — a custom format string like "d MMM yyyy" is NOT translatable
                // and would throw at runtime, so we deliberately don't use one here.
                x.ExpenseDate.ToString().Contains(s) ||
                // CreatedByUserId can be either a Landlord (PortalUser) or a staff member (Admin) —
                // same correlated-subquery shape as the Flat/House checks above, checked against both
                // base tables since we don't know up front which one a given row's creator belongs to.
                _context.PortalUsers.Any(u => u.Id == x.CreatedByUserId && (u.FirstName.Contains(s) || u.LastName.Contains(s))) ||
                _context.Admins.Any(a => a.Id == x.CreatedByUserId && (a.FirstName.Contains(s) || a.LastName.Contains(s))));
        }

        return query;
    }
}
