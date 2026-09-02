using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShmsBackend.Api.Services.Agreements;

namespace ShmsBackend.Api.Controllers;

/// <summary>
/// Portal-side agreement + ID-document endpoints. Shares the /api/portalauth route prefix with
/// PortalAuthController but owns distinct sub-paths.
/// </summary>
[ApiController]
[Route("api/portalauth")]
[Authorize(Roles = "Landlord,Agent,Tenant")]
public class PortalAgreementController : ControllerBase
{
    private readonly IAgreementService _agreementService;

    public PortalAgreementController(IAgreementService agreementService)
    {
        _agreementService = agreementService;
    }

    private Guid Uid() =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    private static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".webp" };

    // GET /api/portalauth/my-agreement
    [HttpGet("my-agreement")]
    public async Task<IActionResult> GetMyAgreement()
    {
        var uid = Uid();
        if (uid == Guid.Empty) return Unauthorized(new { success = false, message = "Invalid token." });
        return Ok(new { success = true, data = await _agreementService.GetMyAgreementAsync(uid) });
    }

    // POST /api/portalauth/my-agreement  (multipart: file — signed PDF)
    [HttpPost("my-agreement")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadMyAgreement([FromForm] IFormFile file)
    {
        var uid = Uid();
        if (uid == Guid.Empty) return Unauthorized(new { success = false, message = "Invalid token." });
        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "A PDF file is required." });
        if (Path.GetExtension(file.FileName).ToLowerInvariant() != ".pdf")
            return BadRequest(new { success = false, message = "Please upload your signed agreement as a PDF." });

        await _agreementService.UploadSignedAgreementAsync(uid, file);
        return Ok(new { success = true, message = "Signed agreement uploaded. It is now pending verification." });
    }

    // GET /api/portalauth/my-id-document
    [HttpGet("my-id-document")]
    public async Task<IActionResult> GetMyIdDocument()
    {
        var uid = Uid();
        if (uid == Guid.Empty) return Unauthorized(new { success = false, message = "Invalid token." });
        return Ok(new { success = true, data = await _agreementService.GetMyIdDocumentAsync(uid) });
    }

    // POST /api/portalauth/my-id-document  (multipart: front, back — either optional, images)
    [HttpPost("my-id-document")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadMyIdDocument([FromForm] IFormFile? front, [FromForm] IFormFile? back)
    {
        var uid = Uid();
        if (uid == Guid.Empty) return Unauthorized(new { success = false, message = "Invalid token." });
        if ((front == null || front.Length == 0) && (back == null || back.Length == 0))
            return BadRequest(new { success = false, message = "Provide at least one image (front or back)." });

        foreach (var f in new[] { front, back })
        {
            if (f != null && f.Length > 0 && !ImageExt.Contains(Path.GetExtension(f.FileName).ToLowerInvariant()))
                return BadRequest(new { success = false, message = "ID images must be JPG, PNG, or WEBP." });
        }

        await _agreementService.UploadIdDocumentAsync(uid, front, back);
        return Ok(new { success = true, message = "ID document uploaded." });
    }

    // ── Authenticated file serving — always the CALLER's own files ─────────

    private IActionResult InlineFile(AgreementFileResult f)
    {
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{f.DownloadName}\"";
        return File(f.Bytes, f.ContentType);
    }

    // GET /api/portalauth/my-agreement/template-file — blank template for the caller's own role
    [HttpGet("my-agreement/template-file")]
    public async Task<IActionResult> GetMyAgreementTemplateFile()
    {
        var uid = Uid();
        if (uid == Guid.Empty) return Unauthorized(new { success = false, message = "Invalid token." });
        var f = await _agreementService.GetMyTemplateFileAsync(uid);
        return f == null
            ? NotFound(new { success = false, message = "No agreement template is available for your role yet." })
            : InlineFile(f);
    }

    // GET /api/portalauth/my-agreement/uploaded-file — the caller's own uploaded signed copy
    [HttpGet("my-agreement/uploaded-file")]
    public async Task<IActionResult> GetMyAgreementUploadedFile()
    {
        var uid = Uid();
        if (uid == Guid.Empty) return Unauthorized(new { success = false, message = "Invalid token." });
        var f = await _agreementService.GetUploadedAgreementFileAsync(uid);
        return f == null
            ? NotFound(new { success = false, message = "You have not uploaded a signed agreement yet." })
            : InlineFile(f);
    }

    // GET /api/portalauth/my-id-document/{side}/file — side = "front" | "back", caller's own image
    [HttpGet("my-id-document/{side}/file")]
    public async Task<IActionResult> GetMyIdDocumentFile(string side)
    {
        var uid = Uid();
        if (uid == Guid.Empty) return Unauthorized(new { success = false, message = "Invalid token." });
        if (!string.Equals(side, "front", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(side, "back", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success = false, message = "Side must be 'front' or 'back'." });

        var f = await _agreementService.GetIdDocumentFileAsync(uid, side);
        return f == null
            ? NotFound(new { success = false, message = $"No {side.ToLowerInvariant()} ID image on file." })
            : InlineFile(f);
    }
}
