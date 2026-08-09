using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/public/housetypes")]
public class PublicHouseTypeController : ControllerBase
{
    private readonly ShmsDbContext _context;

    public PublicHouseTypeController(ShmsDbContext context)
    {
        _context = context;
    }

    // GET /api/public/housetypes
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
    {
        var types = await _context.HouseTypes
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new { t.Id, t.Name })
            .ToListAsync();

        return Ok(new { success = true, data = types });
    }
}
