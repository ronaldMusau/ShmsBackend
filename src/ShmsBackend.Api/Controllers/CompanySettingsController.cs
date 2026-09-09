using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

[ApiController]
[Route("api/companysettings")]
public class CompanySettingsController : ControllerBase
{
    private readonly ShmsDbContext _context;

    public CompanySettingsController(ShmsDbContext context)
    {
        _context = context;
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    // GET /api/companysettings
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await _context.CompanySettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new CompanySettings
            {
                Id = Guid.NewGuid(),
                CompanyName = null,
                Address = null,
                Email = null,
                Phone = null,
                Website = null,
                RegistrationNumber = null,
                LogoPath = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.CompanySettings.Add(settings);
            await _context.SaveChangesAsync();
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                settings.Id,
                settings.CompanyName,
                settings.Address,
                settings.Email,
                settings.Phone,
                settings.Website,
                settings.RegistrationNumber,
                settings.LogoPath,
                settings.UpdatedByUserId,
                settings.CreatedAt,
                settings.UpdatedAt
            }
        });
    }

    // PUT /api/companysettings
    [HttpPut]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateCompanySettingsDto dto)
    {
        var settings = await _context.CompanySettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new CompanySettings { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
            _context.CompanySettings.Add(settings);
        }

        settings.CompanyName = dto.CompanyName;
        settings.Address = dto.Address;
        settings.Email = dto.Email;
        settings.Phone = dto.Phone;
        settings.Website = dto.Website;
        settings.RegistrationNumber = dto.RegistrationNumber;
        settings.UpdatedByUserId = GetUserId();
        settings.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            data = new
            {
                settings.Id,
                settings.CompanyName,
                settings.Address,
                settings.Email,
                settings.Phone,
                settings.Website,
                settings.RegistrationNumber,
                settings.LogoPath,
                settings.UpdatedByUserId,
                settings.CreatedAt,
                settings.UpdatedAt
            }
        });
    }

    // POST /api/companysettings/logo
    [HttpPost("logo")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> UploadLogo(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { success = false, message = "A logo file is required." });

        var settings = await _context.CompanySettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new CompanySettings { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
            _context.CompanySettings.Add(settings);
        }

        var oldLogoPath = settings.LogoPath;

        var newPath = await SaveFileAsync(file, "company-logo");
        settings.LogoPath = newPath;
        settings.UpdatedByUserId = GetUserId();
        settings.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(oldLogoPath))
        {
            try
            {
                var oldDisk = ResolvePrivatePath(oldLogoPath);
                if (System.IO.File.Exists(oldDisk))
                    System.IO.File.Delete(oldDisk);
            }
            catch
            {
                // Best-effort cleanup — a stale orphaned file is not worth failing the request over.
            }
        }

        return Ok(new
        {
            success = true,
            data = new { settings.LogoPath }
        });
    }

    // GET /api/companysettings/logo
    [HttpGet("logo")]
    [Authorize]
    public async Task<IActionResult> GetLogo()
    {
        var settings = await _context.CompanySettings.FirstOrDefaultAsync();
        var result = await ReadPrivateFileAsync(settings?.LogoPath);
        if (result == null)
            return NotFound(new { success = false, message = "No company logo has been uploaded." });

        return File(result.Value.Bytes, result.Value.ContentType);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> ContentTypeByExt = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp"
    };

    /// <summary>
    /// Maps a DB-stored private relative path ("company-logo/{guid}.png") to a physical file under
    /// {cwd}/PrivateUploads. Tolerates the legacy "/uploads/..." URL fragment and falls back to the
    /// old wwwroot/uploads location so pre-move rows still resolve.
    /// </summary>
    private static string ResolvePrivatePath(string storedPath)
    {
        var rel = storedPath.Replace('\\', '/').TrimStart('/');
        if (rel.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            rel = rel["uploads/".Length..];

        var priv = Path.Combine(Directory.GetCurrentDirectory(), "PrivateUploads", rel);
        if (System.IO.File.Exists(priv)) return priv;

        var legacy = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", rel);
        return System.IO.File.Exists(legacy) ? legacy : priv;
    }

    private static async Task<(byte[] Bytes, string ContentType)?> ReadPrivateFileAsync(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;

        var disk = ResolvePrivatePath(storedPath);
        if (!System.IO.File.Exists(disk)) return null;

        var bytes = await System.IO.File.ReadAllBytesAsync(disk);
        var ext = Path.GetExtension(disk);
        var contentType = ContentTypeByExt.TryGetValue(ext, out var ct) ? ct : "application/octet-stream";
        return (bytes, contentType);
    }

    /// <summary>
    /// Saves an uploaded file to {cwd}/PrivateUploads/{subfolder}/{guid}{ext} — a folder OUTSIDE
    /// wwwroot, so it is never served by UseStaticFiles. Returns a bare private relative path
    /// ("{subfolder}/{guid}{ext}"), resolved server-side only by authenticated endpoints.
    /// </summary>
    private static async Task<string> SaveFileAsync(IFormFile file, string subfolder)
    {
        var saveDir = Path.Combine(Directory.GetCurrentDirectory(), "PrivateUploads", subfolder);
        Directory.CreateDirectory(saveDir);

        var ext = Path.GetExtension(file.FileName);
        var fileName = $"{Guid.NewGuid()}{ext}";
        var fullPath = Path.Combine(saveDir, fileName);

        using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        return $"{subfolder}/{fileName}";
    }
}

public class UpdateCompanySettingsDto
{
    public string? CompanyName { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? RegistrationNumber { get; set; }
}
