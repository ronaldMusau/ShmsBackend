using Microsoft.AspNetCore.Mvc;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
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