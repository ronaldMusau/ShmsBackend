using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ShmsBackend.Data.Context.ShmsDbContext _db;
    public HealthController(ShmsBackend.Data.Context.ShmsDbContext db) { _db = db; }

    // GET /api/health/ready — for load balancers/uptime monitors: 200 only when the database answers.
    [AllowAnonymous]
    [HttpGet("ready")]
    public async Task<IActionResult> Ready()
    {
        var dbOk = false;
        try { dbOk = await _db.Database.CanConnectAsync(); } catch { /* reported below */ }
        return dbOk ? Ok(new { Status = "Ready", Timestamp = DateTime.UtcNow }) : StatusCode(503, new { Status = "DatabaseUnavailable", Timestamp = DateTime.UtcNow });
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            Status = "Healthy",
            Timestamp = DateTime.UtcNow,
            Service = "SHMS Backend API",
            Version = "1.0.0"
        });
    }
}