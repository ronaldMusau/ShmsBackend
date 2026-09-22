using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using System.Security.Claims;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HouseTypeController : ControllerBase
{
    private const string CacheKey = "housetypes:all";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly ShmsDbContext _context;
    private readonly ICacheHelper _cacheHelper;

    public HouseTypeController(ShmsDbContext context, ICacheHelper cacheHelper)
    {
        _context = context;
        _cacheHelper = cacheHelper;
    }

    // GET /api/housetype
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var types = await _cacheHelper.GetOrSetAsync(CacheKey, CacheTtl, () =>
            _context.HouseTypes
                .Where(t => t.IsActive)
                .OrderBy(t => t.Name)
                .ToListAsync());
        return Ok(new { success = true, data = types });
    }

    // POST /api/housetype
    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Create([FromBody] HouseTypeDto dto)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(userIdStr, out var userId);

        var type = new HouseType
        {
            Name = dto.Name,
            Description = dto.Description,
            IsActive = true,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.HouseTypes.AddAsync(type);
        await _context.SaveChangesAsync();
        await _cacheHelper.RemoveAsync(CacheKey);
        return Ok(new { success = true, data = type });
    }

    // PUT /api/housetype/{id}
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Update(Guid id, [FromBody] HouseTypeDto dto)
    {
        var type = await _context.HouseTypes.FindAsync(id);
        if (type == null) return NotFound(new { success = false, message = "Not found." });

        type.Name = dto.Name;
        type.Description = dto.Description;
        type.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await _cacheHelper.RemoveAsync(CacheKey);
        // A rename changes both the houseType display text and the filter string on every cached
        // public listing card/page for houses of this type.
        await _cacheHelper.InvalidatePublicListingsCacheAsync();
        return Ok(new { success = true, data = type });
    }

    // DELETE /api/housetype/{id}
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var type = await _context.HouseTypes.FindAsync(id);
        if (type == null) return NotFound(new { success = false, message = "Not found." });

        type.IsDeleted = true;
        type.DeletedAt = DateTime.UtcNow;
        type.IsActive = false;
        await _context.SaveChangesAsync();
        await _cacheHelper.RemoveAsync(CacheKey);
        return Ok(new { success = true, message = "House type deleted." });
    }
}

public class HouseTypeDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
