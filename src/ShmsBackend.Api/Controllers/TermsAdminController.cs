using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/admin/terms")]
[Authorize]
public class TermsAdminController : ControllerBase
{
    private readonly ShmsDbContext _context;

    public TermsAdminController(ShmsDbContext context)
    {
        _context = context;
    }

    // GET /api/admin/terms — current terms for every portal role
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> GetAllTerms()
    {
        var rows = await _context.TermsAndConditions.ToListAsync();

        var data = Enum.GetValues<PortalUserType>()
            .Select(r =>
            {
                var row = rows.FirstOrDefault(x => x.Role == (int)r);
                return new
                {
                    role = (int)r,
                    roleName = r.ToString(),
                    content = row?.Content,
                    version = row?.Version ?? 0,
                    updatedAt = row?.UpdatedAt
                };
            })
            .ToList();

        return Ok(new { success = true, data });
    }

    // PUT /api/admin/terms/{role} — replace the text for one role, bump its version
    [HttpPut("{role:int}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> UpdateTerms(int role, [FromBody] UpdateTermsDto dto)
    {
        if (!Enum.IsDefined(typeof(PortalUserType), role))
            return BadRequest(new { success = false, message = "Invalid role." });

        var adminIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid? adminId = Guid.TryParse(adminIdStr, out var parsed) ? parsed : null;

        var terms = await _context.TermsAndConditions.FirstOrDefaultAsync(t => t.Role == role);

        if (terms == null)
        {
            terms = new TermsAndConditions
            {
                Id = Guid.NewGuid(),
                Role = role,
                Content = dto.Content ?? string.Empty,
                Version = 1,
                UpdatedAt = DateTime.UtcNow,
                UpdatedByAdminId = adminId
            };
            _context.TermsAndConditions.Add(terms);
        }
        else
        {
            // Archive the version that's about to be replaced (not the new content).
            _context.TermsHistories.Add(new TermsHistory
            {
                Id = Guid.NewGuid(),
                Role = terms.Role,
                Content = terms.Content,
                Version = terms.Version,
                UpdatedAt = terms.UpdatedAt,
                ArchivedAt = DateTime.UtcNow,
                UpdatedByAdminId = terms.UpdatedByAdminId
            });

            terms.Content = dto.Content ?? string.Empty;
            terms.Version += 1;
            terms.UpdatedAt = DateTime.UtcNow;
            terms.UpdatedByAdminId = adminId;
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Terms updated.", data = new { role, terms.Version } });
    }

    // GET /api/admin/terms/{role}/history — archived (superseded) versions for one role
    [HttpGet("{role:int}/history")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> GetTermsHistory(int role)
    {
        if (!Enum.IsDefined(typeof(PortalUserType), role))
            return BadRequest(new { success = false, message = "Invalid role." });

        var data = await _context.TermsHistories
            .Where(h => h.Role == role)
            .OrderByDescending(h => h.Version)
            .Select(h => new
            {
                version = h.Version,
                content = h.Content,
                updatedAt = h.UpdatedAt,
                archivedAt = h.ArchivedAt,
                updatedByAdminId = h.UpdatedByAdminId
            })
            .ToListAsync();

        return Ok(new { success = true, data });
    }
}

public class UpdateTermsDto
{
    public string Content { get; set; } = string.Empty;
}
