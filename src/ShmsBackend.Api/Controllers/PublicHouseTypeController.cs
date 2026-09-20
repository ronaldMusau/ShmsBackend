using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/public/housetypes")]
public class PublicHouseTypeController : ControllerBase
{
    // Same key as HouseTypeController — both endpoints read through one shared cache entry, kept
    // as the full entity list so the underlying cached shape matches exactly; this controller just
    // projects down to {Id, Name} for its own public-facing response after retrieval.
    private const string CacheKey = "housetypes:all";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly ShmsDbContext _context;
    private readonly ICacheHelper _cacheHelper;

    public PublicHouseTypeController(ShmsDbContext context, ICacheHelper cacheHelper)
    {
        _context = context;
        _cacheHelper = cacheHelper;
    }

    // GET /api/public/housetypes
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
    {
        var types = await _cacheHelper.GetOrSetAsync(CacheKey, CacheTtl, () =>
            _context.HouseTypes
                .Where(t => t.IsActive)
                .OrderBy(t => t.Name)
                .ToListAsync());

        var data = (types ?? new List<HouseType>()).Select(t => new { t.Id, t.Name });

        return Ok(new { success = true, data });
    }
}
