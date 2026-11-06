using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShmsBackend.Api.Services.Security;

namespace ShmsBackend.Api.Controllers;

/// <summary>Hands the browser the server's public encryption key (and server time, so clock skew cannot break requests).</summary>
[ApiController]
[Route("api/secure")]
public class SecureChannelController : ControllerBase
{
    private readonly PayloadEncryptionService _enc;
    public SecureChannelController(PayloadEncryptionService enc) => _enc = enc;

    [AllowAnonymous]
    [HttpGet("key")]
    public IActionResult GetKey() => Ok(new
    {
        kid = _enc.CurrentKeyId,
        publicKey = _enc.GetPublicKeyBase64(_enc.CurrentKeyId),
        serverTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    });
}
