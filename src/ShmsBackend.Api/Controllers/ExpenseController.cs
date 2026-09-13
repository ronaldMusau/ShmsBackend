using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services;
using ShmsBackend.Api.Services.Reports;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/expenses")]
public class ExpenseController : ControllerBase
{
    private readonly ShmsDbContext _context;
    private readonly ExpenseQueryService _expenseQueryService;

    public ExpenseController(ShmsDbContext context, ExpenseQueryService expenseQueryService)
    {
        _context = context;
        _expenseQueryService = expenseQueryService;
    }

    private Guid? GetLandlordId()
    {
        var landlordIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(landlordIdStr, out var landlordId) ? landlordId : null;
    }

    private Guid GetCallerId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    // Expense has scalar FlatId/HouseId only (no nav properties), so Flat/House display names are
    // resolved via bulk dictionary lookups here, same pattern as ExpenseReportBuilder.
    private async Task<(Dictionary<Guid, string> Flats, Dictionary<Guid, string> Houses)> ResolveFlatHouseNamesAsync(
        IEnumerable<Expense> expenses)
    {
        var flatIds = expenses.Where(x => x.FlatId.HasValue).Select(x => x.FlatId!.Value).Distinct().ToList();
        var flats = await _context.Flats
            .Where(f => flatIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var houseIds = expenses.Where(x => x.HouseId.HasValue).Select(x => x.HouseId!.Value).Distinct().ToList();
        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        return (flats, houses);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Landlord — logs expenses against their own flats/houses
    // ═══════════════════════════════════════════════════════════════════

    // POST /api/expenses/landlord
    [HttpPost("landlord")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> CreateLandlordExpense([FromBody] CreateExpenseDto dto)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();

        if (!dto.FlatId.HasValue)
            return BadRequest(new { success = false, message = "FlatId is required." });

        var flat = await _context.Flats.FirstOrDefaultAsync(f => f.Id == dto.FlatId.Value);
        if (flat == null || flat.LandlordId != landlordId.Value)
            return StatusCode(403, new { success = false, message = "This flat does not belong to you." });

        if (dto.HouseId.HasValue)
        {
            var houseOk = await _context.Houses.AnyAsync(h => h.Id == dto.HouseId.Value && h.FlatId == dto.FlatId.Value);
            if (!houseOk)
                return BadRequest(new { success = false, message = "This house does not belong to the specified flat." });
        }

        var expense = new Expense
        {
            LandlordId = landlordId.Value,
            CreatedByUserId = landlordId.Value,
            FlatId = dto.FlatId,
            HouseId = dto.HouseId,
            Amount = dto.Amount,
            Description = dto.Description ?? "",
            ExpenseDate = dto.ExpenseDate
        };
        await _context.Expenses.AddAsync(expense);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, data = expense });
    }

    // GET /api/expenses/landlord
    [HttpGet("landlord")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> GetLandlordExpenses(
        [FromQuery] ExpenseFilters filters,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();
        filters.LandlordId = landlordId;

        var query = _expenseQueryService.BuildFilteredQuery(filters);
        var total = await query.CountAsync();
        var totalAmount = await query.SumAsync(x => x.Amount);
        var expenses = await query
            .OrderByDescending(x => x.ExpenseDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var creatorNames = await ReportBuilderHelpers.ResolveCreatorNamesAsync(
            _context, expenses.Select(x => x.CreatedByUserId));
        var (flatNames, houseNumbers) = await ResolveFlatHouseNamesAsync(expenses);
        var data = expenses.Select(x => new
        {
            x.Id,
            x.LandlordId,
            x.CreatedByUserId,
            x.FlatId,
            x.HouseId,
            x.Amount,
            x.Description,
            x.ExpenseDate,
            x.CreatedAt,
            x.UpdatedAt,
            FlatName = x.FlatId.HasValue ? flatNames.GetValueOrDefault(x.FlatId.Value, "-") : null,
            HouseNumber = x.HouseId.HasValue ? houseNumbers.GetValueOrDefault(x.HouseId.Value, "-") : null,
            LoggedByName = creatorNames.GetValueOrDefault(x.CreatedByUserId, "-")
        });

        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize),
            totalAmount
        });
    }

    // PUT /api/expenses/landlord/{id}
    [HttpPut("landlord/{id:guid}")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> UpdateLandlordExpense(Guid id, [FromBody] UpdateExpenseDto dto)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();

        var expense = await _context.Expenses.FirstOrDefaultAsync(x => x.Id == id);
        if (expense == null) return NotFound(new { success = false, message = "Expense not found." });
        if (expense.LandlordId != landlordId.Value)
            return StatusCode(403, new { success = false, message = "Not authorized for this expense." });

        var effectiveFlatId = dto.FlatId ?? expense.FlatId;
        if (dto.FlatId.HasValue)
        {
            var flat = await _context.Flats.FirstOrDefaultAsync(f => f.Id == dto.FlatId.Value);
            if (flat == null || flat.LandlordId != landlordId.Value)
                return StatusCode(403, new { success = false, message = "This flat does not belong to you." });
        }

        if (dto.HouseId.HasValue)
        {
            var houseOk = effectiveFlatId.HasValue &&
                await _context.Houses.AnyAsync(h => h.Id == dto.HouseId.Value && h.FlatId == effectiveFlatId.Value);
            if (!houseOk)
                return BadRequest(new { success = false, message = "This house does not belong to the specified flat." });
        }

        if (dto.FlatId.HasValue) expense.FlatId = dto.FlatId;
        if (dto.HouseId.HasValue) expense.HouseId = dto.HouseId;
        if (dto.Amount.HasValue) expense.Amount = dto.Amount.Value;
        if (dto.Description != null) expense.Description = dto.Description;
        if (dto.ExpenseDate.HasValue) expense.ExpenseDate = dto.ExpenseDate.Value;
        expense.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { success = true, data = expense });
    }

    // DELETE /api/expenses/landlord/{id}
    [HttpDelete("landlord/{id:guid}")]
    [Authorize(Roles = "Landlord")]
    public async Task<IActionResult> DeleteLandlordExpense(Guid id)
    {
        var landlordId = GetLandlordId();
        if (landlordId == null) return Unauthorized();

        var expense = await _context.Expenses.FirstOrDefaultAsync(x => x.Id == id);
        if (expense == null) return NotFound(new { success = false, message = "Expense not found." });
        if (expense.LandlordId != landlordId.Value)
            return StatusCode(403, new { success = false, message = "Not authorized for this expense." });

        expense.IsDeleted = true;
        expense.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Deleted." });
    }

    // ═══════════════════════════════════════════════════════════════════
    // Management — logs expenses independent of any landlord
    // ═══════════════════════════════════════════════════════════════════

    // POST /api/expenses
    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> CreateExpense([FromBody] CreateExpenseDto dto)
    {
        if (dto.FlatId.HasValue && dto.HouseId.HasValue)
        {
            var houseOk = await _context.Houses.AnyAsync(h => h.Id == dto.HouseId.Value && h.FlatId == dto.FlatId.Value);
            if (!houseOk)
                return BadRequest(new { success = false, message = "This house does not belong to the specified flat." });
        }

        var expense = new Expense
        {
            LandlordId = null,
            CreatedByUserId = GetCallerId(),
            FlatId = dto.FlatId,
            HouseId = dto.HouseId,
            Amount = dto.Amount,
            Description = dto.Description ?? "",
            ExpenseDate = dto.ExpenseDate
        };
        await _context.Expenses.AddAsync(expense);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, data = expense });
    }

    // GET /api/expenses
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> GetExpenses(
        [FromQuery] ExpenseFilters filters,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        filters.LandlordId = null;

        var query = _expenseQueryService.BuildFilteredQuery(filters);
        var total = await query.CountAsync();
        var totalAmount = await query.SumAsync(x => x.Amount);
        var expenses = await query
            .OrderByDescending(x => x.ExpenseDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var creatorNames = await ReportBuilderHelpers.ResolveCreatorNamesAsync(
            _context, expenses.Select(x => x.CreatedByUserId));
        var (flatNames, houseNumbers) = await ResolveFlatHouseNamesAsync(expenses);
        var data = expenses.Select(x => new
        {
            x.Id,
            x.LandlordId,
            x.CreatedByUserId,
            x.FlatId,
            x.HouseId,
            x.Amount,
            x.Description,
            x.ExpenseDate,
            x.CreatedAt,
            x.UpdatedAt,
            FlatName = x.FlatId.HasValue ? flatNames.GetValueOrDefault(x.FlatId.Value, "-") : null,
            HouseNumber = x.HouseId.HasValue ? houseNumbers.GetValueOrDefault(x.HouseId.Value, "-") : null,
            LoggedByName = creatorNames.GetValueOrDefault(x.CreatedByUserId, "-")
        });

        return Ok(new
        {
            success = true,
            data,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize),
            totalAmount
        });
    }

    // PUT /api/expenses/{id}
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> UpdateExpense(Guid id, [FromBody] UpdateExpenseDto dto)
    {
        var expense = await _context.Expenses.FirstOrDefaultAsync(x => x.Id == id && x.LandlordId == null);
        if (expense == null) return NotFound(new { success = false, message = "Expense not found." });

        var effectiveFlatId = dto.FlatId ?? expense.FlatId;
        if (dto.HouseId.HasValue)
        {
            var houseOk = effectiveFlatId.HasValue &&
                await _context.Houses.AnyAsync(h => h.Id == dto.HouseId.Value && h.FlatId == effectiveFlatId.Value);
            if (!houseOk)
                return BadRequest(new { success = false, message = "This house does not belong to the specified flat." });
        }

        if (dto.FlatId.HasValue) expense.FlatId = dto.FlatId;
        if (dto.HouseId.HasValue) expense.HouseId = dto.HouseId;
        if (dto.Amount.HasValue) expense.Amount = dto.Amount.Value;
        if (dto.Description != null) expense.Description = dto.Description;
        if (dto.ExpenseDate.HasValue) expense.ExpenseDate = dto.ExpenseDate.Value;
        expense.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { success = true, data = expense });
    }

    // DELETE /api/expenses/{id}
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> DeleteExpense(Guid id)
    {
        var expense = await _context.Expenses.FirstOrDefaultAsync(x => x.Id == id && x.LandlordId == null);
        if (expense == null) return NotFound(new { success = false, message = "Expense not found." });

        expense.IsDeleted = true;
        expense.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Deleted." });
    }
}

public class CreateExpenseDto
{
    public Guid? FlatId { get; set; }
    public Guid? HouseId { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime ExpenseDate { get; set; }
}

public class UpdateExpenseDto
{
    public Guid? FlatId { get; set; }
    public Guid? HouseId { get; set; }
    public decimal? Amount { get; set; }
    public string? Description { get; set; }
    public DateTime? ExpenseDate { get; set; }
}
