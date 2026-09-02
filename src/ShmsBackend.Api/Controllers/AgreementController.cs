using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ShmsBackend.Api.Services.Agreements;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/agreements")]
[Authorize(Roles = "SuperAdmin,Admin")]
public class AgreementController : ControllerBase
{
    private readonly IAgreementService _agreementService;

    public AgreementController(IAgreementService agreementService)
    {
        _agreementService = agreementService;
    }

    private Guid AdminId() =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;

    private static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".webp" };

    // POST /api/agreements/template/{role}
    [HttpPost("template/{role:int}")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadTemplate(int role, [FromForm] IFormFile file)
    {
        if (!Enum.IsDefined(typeof(PortalUserType), role) || role == (int)PortalUserType.Explorer)
            return BadRequest(new { success = false, message = "Role must be Landlord, Agent, or Tenant." });
        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "A PDF file is required." });
        if (Path.GetExtension(file.FileName).ToLowerInvariant() != ".pdf")
            return BadRequest(new { success = false, message = "Agreement templates must be a PDF." });

        await _agreementService.UploadTemplateAsync(role, file, AdminId());
        return Ok(new { success = true, message = "Agreement template uploaded." });
    }

    // GET /api/agreements/template/{role}
    [HttpGet("template/{role:int}")]
    public async Task<IActionResult> GetTemplate(int role)
        => Ok(new { success = true, data = await _agreementService.GetTemplateAsync(role) });

    // GET /api/agreements/template/{role}/history
    [HttpGet("template/{role:int}/history")]
    public async Task<IActionResult> GetTemplateHistory(int role)
        => Ok(new { success = true, data = await _agreementService.GetTemplateHistoryAsync(role) });

    // GET /api/agreements/all-statuses?role={optional}
    // role accepts the PortalUserType name the frontend sends ("Landlord", "Agent", "Tenant"),
    // or the numeric value; omitted/blank = all roles.
    [HttpGet("all-statuses")]
    public async Task<IActionResult> GetAllStatuses([FromQuery] string? role = null)
    {
        int? roleFilter = null;
        if (!string.IsNullOrWhiteSpace(role))
        {
            if (!Enum.TryParse<PortalUserType>(role, ignoreCase: true, out var parsedRole)
                || !Enum.IsDefined(typeof(PortalUserType), parsedRole))
                return BadRequest(new { success = false, message = "Invalid role. Use 'Landlord', 'Agent', or 'Tenant'." });
            roleFilter = (int)parsedRole;
        }

        return Ok(new { success = true, data = await _agreementService.GetAllUserAgreementStatusesAsync(roleFilter) });
    }

    // POST /api/agreements/{portalUserId}/verify
    [HttpPost("{portalUserId:guid}/verify")]
    public async Task<IActionResult> Verify(Guid portalUserId)
    {
        await _agreementService.VerifyAgreementAsync(portalUserId, AdminId());
        return Ok(new { success = true, message = "Agreement verified." });
    }

    // POST /api/agreements/{portalUserId}/reject
    [HttpPost("{portalUserId:guid}/reject")]
    public async Task<IActionResult> Reject(Guid portalUserId, [FromBody] RejectAgreementDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Reason))
            return BadRequest(new { success = false, message = "A rejection reason is required." });

        await _agreementService.RejectAgreementAsync(portalUserId, AdminId(), dto.Reason.Trim());
        return Ok(new { success = true, message = "Agreement rejected. The user has been asked to re-sign." });
    }

    // POST /api/agreements/{portalUserId}/remind
    [HttpPost("{portalUserId:guid}/remind")]
    public async Task<IActionResult> Remind(Guid portalUserId)
    {
        await _agreementService.SendReminderAsync(portalUserId, AdminId());
        return Ok(new { success = true, message = "Reminder sent." });
    }

    // ── Authenticated file serving (private folder) ────────────────────────

    private IActionResult InlineFile(AgreementFileResult f)
    {
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{f.DownloadName}\"";
        return File(f.Bytes, f.ContentType);
    }

    // GET /api/agreements/template/{role}/file — current blank template, inline
    [HttpGet("template/{role:int}/file")]
    public async Task<IActionResult> GetTemplateFile(int role)
    {
        var f = await _agreementService.GetTemplateFileAsync(role);
        return f == null
            ? NotFound(new { success = false, message = "No template file found for this role." })
            : InlineFile(f);
    }

    // GET /api/agreements/template/{role}/history/{version}/file — a specific archived version
    [HttpGet("template/{role:int}/history/{version:int}/file")]
    public async Task<IActionResult> GetTemplateHistoryFile(int role, int version)
    {
        var f = await _agreementService.GetTemplateHistoryFileAsync(role, version);
        return f == null
            ? NotFound(new { success = false, message = "No archived template file found for that version." })
            : InlineFile(f);
    }

    // GET /api/agreements/{portalUserId}/uploaded-file — that user's uploaded signed copy
    [HttpGet("{portalUserId:guid}/uploaded-file")]
    public async Task<IActionResult> GetUploadedFile(Guid portalUserId)
    {
        var f = await _agreementService.GetUploadedAgreementFileAsync(portalUserId);
        return f == null
            ? NotFound(new { success = false, message = "This user has no uploaded signed agreement." })
            : InlineFile(f);
    }

    // GET /api/agreements/{portalUserId}/id-document/{side} — side = "front" | "back"
    [HttpGet("{portalUserId:guid}/id-document/{side}")]
    public async Task<IActionResult> GetIdDocumentFile(Guid portalUserId, string side)
    {
        if (!string.Equals(side, "front", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(side, "back", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success = false, message = "Side must be 'front' or 'back'." });

        var f = await _agreementService.GetIdDocumentFileAsync(portalUserId, side);
        return f == null
            ? NotFound(new { success = false, message = $"No {side.ToLowerInvariant()} ID image on file for this user." })
            : InlineFile(f);
    }
}

public class RejectAgreementDto
{
    public string Reason { get; set; } = string.Empty;
}
