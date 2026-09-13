using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DeductionController : ControllerBase
{
    private readonly ShmsDbContext _context;
    public DeductionController(ShmsDbContext context) { _context = context; }

    // GET /api/deduction/all
    [HttpGet("all")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? landlordId = null,
        [FromQuery] int? month = null,
        [FromQuery] int? year = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? search = null)
    {
        var query = _context.Deductions.AsQueryable();
        if (landlordId.HasValue) query = query.Where(d => d.LandlordId == landlordId.Value);
        if (month.HasValue) query = query.Where(d => d.DeductionMonth == month.Value);
        if (year.HasValue) query = query.Where(d => d.DeductionYear == year.Value);
        if (fromDate.HasValue) query = query.Where(d => d.CreatedAt >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(d => d.CreatedAt.Date <= toDate.Value.Date);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            // Deduction has scalar Landlord/Tenant/House/FlatId only (no nav properties) — same
            // correlated Any() subquery pattern as Vacate/Complaints. No numeric-Contains
            // convention exists elsewhere in this codebase, so Amount is intentionally not searched.
            query = query.Where(x =>
                _context.Landlords.Any(l => l.Id == x.LandlordId &&
                    (l.FirstName.Contains(s) || l.LastName.Contains(s) || l.Email.Contains(s))) ||
                _context.Tenants.Any(t => t.Id == x.TenantId &&
                    (t.FirstName.Contains(s) || t.LastName.Contains(s) || t.Email.Contains(s))) ||
                _context.Houses.Any(h => h.Id == x.HouseId && h.HouseNumber.Contains(s)) ||
                _context.Flats.Any(f => f.Id == x.FlatId && f.FlatName.Contains(s)) ||
                (x.Description != null && x.Description.Contains(s)));
        }
        var total = await query.CountAsync();
        var paged = await query.OrderByDescending(d => d.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var landlordIds = paged.Select(d => d.LandlordId).Distinct().ToList();
        var landlords = await _context.Landlords
            .Where(l => landlordIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => $"{l.FirstName} {l.LastName}");

        var tenantIds = paged.Select(d => d.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

        var houseIds = paged.Select(d => d.HouseId).Distinct().ToList();
        var houses = await _context.Houses.Where(h => houseIds.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var complaintIds = paged.Select(d => d.ComplaintId).Distinct().ToList();
        var complaints = await _context.Complaints.Where(c => complaintIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.TicketNumber);

        var data = paged.Select(d => new
        {
            d.Id,
            LandlordName = landlords.GetValueOrDefault(d.LandlordId, "-"),
            TenantName = tenants.GetValueOrDefault(d.TenantId, "-"),
            HouseNumber = houses.GetValueOrDefault(d.HouseId, "-"),
            TicketNumber = complaints.GetValueOrDefault(d.ComplaintId, "-"),
            d.Amount,
            d.Description,
            d.DeductionMonth,
            d.DeductionYear,
            d.CreatedAt
        }).ToList();
        var totalAmount = await query.SumAsync(d => d.Amount);

        var perLandlord = await _context.Deductions
            .Where(d => (!landlordId.HasValue || d.LandlordId == landlordId.Value) && (!month.HasValue || d.DeductionMonth == month.Value) && (!year.HasValue || d.DeductionYear == year.Value) && (!fromDate.HasValue || d.CreatedAt >= fromDate.Value) && (!toDate.HasValue || d.CreatedAt.Date <= toDate.Value.Date))
            .GroupBy(d => d.LandlordId)
            .Select(g => new { LandlordId = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync();

        var perLandlordIds = perLandlord.Select(x => x.LandlordId).Distinct().ToList();
        var perLandlordNames = await _context.Landlords
            .Where(l => perLandlordIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => $"{l.FirstName} {l.LastName}");

        var perLandlordNamed = perLandlord.Select(x => new
        {
            LandlordName = perLandlordNames.GetValueOrDefault(x.LandlordId, "Unknown"),
            x.Total,
            x.Count
        });

        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize),
            totalAmount,
            perLandlord = perLandlordNamed
        });
    }

    // GET /api/deduction/years — distinct years present in Deductions table
    [HttpGet("years")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetAvailableYears()
    {
        var years = await _context.Deductions.Select(d => d.DeductionYear).Distinct().OrderByDescending(y => y).ToListAsync();
        var currentYear = DateTime.UtcNow.Year;
        if (!years.Contains(currentYear)) years.Insert(0, currentYear);
        years = years.OrderByDescending(y => y).ToList();
        return Ok(new { success = true, years });
    }
}
