using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Auth;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/explorer")]
[Authorize]
public class ExplorerController : ControllerBase
{
    private readonly ShmsDbContext _context;
    private readonly ITokenBlacklistService _tokenBlacklistService;
    private readonly IEmailService _emailService;

    public ExplorerController(
        ShmsDbContext context,
        ITokenBlacklistService tokenBlacklistService,
        IEmailService emailService)
    {
        _context = context;
        _tokenBlacklistService = tokenBlacklistService;
        _emailService = emailService;
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetAll()
    {
        var explorers = await _context.Explorers
            .Select(e => new {
                e.Id, e.FirstName, e.LastName, e.Email,
                e.PhoneNumber, e.County, e.Constituency, e.Ward,
                e.IsActive, e.IsEmailVerified, e.CreatedAt
            })
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

        return Ok(new { success = true, data = explorers });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var explorer = await _context.Explorers.FirstOrDefaultAsync(e => e.Id == id);
        if (explorer == null)
            return NotFound(new { success = false, message = "Explorer not found." });

        return Ok(new { success = true, data = new {
            explorer.Id, explorer.FirstName, explorer.LastName,
            explorer.Email, explorer.PhoneNumber, explorer.County,
            explorer.Constituency, explorer.Ward, explorer.IsActive,
            explorer.IsEmailVerified, explorer.DateOfBirth,
            explorer.NationalId, explorer.CreatedAt
        }});
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var explorer = await _context.Explorers.FirstOrDefaultAsync(e => e.Id == id);
        if (explorer == null)
            return NotFound(new { success = false, message = "Explorer not found." });

        _context.Explorers.Remove(explorer);
        await _context.SaveChangesAsync();
        return Ok(new { success = true, message = "Explorer deleted." });
    }

    [HttpPatch("{id:guid}/toggle-status")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> ToggleStatus(Guid id)
    {
        var explorer = await _context.Explorers.FirstOrDefaultAsync(e => e.Id == id);
        if (explorer == null)
            return NotFound(new { success = false, message = "Explorer not found." });

        explorer.IsActive = !explorer.IsActive;
        explorer.UpdatedAt = DateTime.UtcNow;

        if (!explorer.IsActive)
        {
            if (!string.IsNullOrEmpty(explorer.RefreshToken))
                await _tokenBlacklistService.BlacklistTokenAsync(explorer.RefreshToken, TimeSpan.FromDays(30));

            try { await _emailService.SendAccountDeactivatedEmailAsync(explorer.Email, explorer.FirstName); }
            catch { /* log but don't fail */ }
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true, data = new { explorer.Id, explorer.IsActive },
            message = explorer.IsActive ? "Account activated." : "Account deactivated." });
    }
}
