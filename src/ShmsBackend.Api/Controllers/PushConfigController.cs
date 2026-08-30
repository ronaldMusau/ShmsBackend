using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/push")]
public class PushConfigController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public PushConfigController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // GET /api/push/vapid-public-key
    // The browser needs the VAPID public key to create a PushSubscription, before any
    // authenticated context necessarily exists — so this is deliberately anonymous.
    [HttpGet("vapid-public-key")]
    [AllowAnonymous]
    public IActionResult GetVapidPublicKey()
    {
        var publicKey = _configuration["WebPush:VapidPublicKey"];
        return Ok(new { publicKey });
    }
}
