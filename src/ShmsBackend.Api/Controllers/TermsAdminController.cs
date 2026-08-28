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
            terms.Content = dto.Content ?? string.Empty;
            terms.Version += 1;
            terms.UpdatedAt = DateTime.UtcNow;
            terms.UpdatedByAdminId = adminId;
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Terms updated.", data = new { role, terms.Version } });
    }
}

public class UpdateTermsDto
{
    public string Content { get; set; } = string.Empty;
}
