using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using System.Security.Claims;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ComplaintTypeController : ControllerBase
{
    private readonly ShmsDbContext _context;

    public ComplaintTypeController(ShmsDbContext context)
    {
        _context = context;
    }

    // GET /api/complainttype
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var types = await _context.ComplaintTypes
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync();
        return Ok(new { success = true, data = types });
    }

    // POST /api/complainttype
    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Create([FromBody] ComplaintTypeDto dto)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(userIdStr, out var userId);

        var type = new ComplaintType
        {
            Name = dto.Name,
            Description = dto.Description,
            IsActive = true,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _context.ComplaintTypes.AddAsync(type);
        await _context.SaveChangesAsync();
        return Ok(new { success = true, data = type });
    }

    // PUT /api/complainttype/{id}
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ComplaintTypeDto dto)
    {
        var type = await _context.ComplaintTypes.FindAsync(id);
        if (type == null) return NotFound(new { success = false, message = "Not found." });

        type.Name = dto.Name;
        type.Description = dto.Description;
        type.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { success = true, data = type });
    }

    // DELETE /api/complainttype/{id}
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var type = await _context.ComplaintTypes.FindAsync(id);
        if (type == null) return NotFound(new { success = false, message = "Not found." });

        type.IsDeleted = true;
        type.DeletedAt = DateTime.UtcNow;
        type.IsActive = false;
        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Complaint type deleted." });
    }
}

public class ComplaintTypeDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
