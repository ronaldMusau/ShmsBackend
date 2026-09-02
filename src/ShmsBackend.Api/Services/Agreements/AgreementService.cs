using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Models.Enums;

namespace ShmsBackend.Api.Services.Agreements;

public class AgreementService : IAgreementService
{
    private readonly ShmsDbContext _context;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<AgreementService> _logger;

    public AgreementService(
        ShmsDbContext context,
        IEmailService emailService,
        INotificationService notificationService,
        ILogger<AgreementService> logger)
    {
        _context = context;
        _emailService = emailService;
        _notificationService = notificationService;
        _logger = logger;
    }

    // ── Template management ─────────────────────────────────────────────────

    public async Task UploadTemplateAsync(int role, IFormFile file, Guid adminId)
    {
        var current = await _context.AgreementTemplates.FirstOrDefaultAsync(t => t.Role == role);
        var path = await SaveFileAsync(file, "agreements");

        if (current == null)
        {
            _context.AgreementTemplates.Add(new AgreementTemplate
            {
                Id = Guid.NewGuid(),
                Role = role,
                FilePath = path,
                Version = 1,
                UploadedAt = DateTime.UtcNow,
                UploadedByAdminId = adminId
            });
        }
        else
        {
            // Archive the version that's about to be replaced.
            _context.AgreementTemplateHistories.Add(new AgreementTemplateHistory
            {
                Id = Guid.NewGuid(),
                Role = current.Role,
                FilePath = current.FilePath,
                Version = current.Version,
                UploadedAt = current.UploadedAt,
                ArchivedAt = DateTime.UtcNow,
                UploadedByAdminId = current.UploadedByAdminId
            });

            current.FilePath = path;
            current.Version += 1;
            current.UploadedAt = DateTime.UtcNow;
            current.UploadedByAdminId = adminId;
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Agreement template uploaded for role {Role}", role);
    }

    public async Task<AgreementTemplateDto?> GetTemplateAsync(int role)
    {
        var t = await _context.AgreementTemplates.FirstOrDefaultAsync(x => x.Role == role);
        if (t == null) return null;
        return new AgreementTemplateDto
        {
            Role = t.Role,
            RoleName = RoleName(t.Role),
            FilePath = t.FilePath,
            Version = t.Version,
            UploadedAt = t.UploadedAt,
            UploadedByAdminId = t.UploadedByAdminId
        };
    }

    public async Task<IReadOnlyList<AgreementTemplateHistoryDto>> GetTemplateHistoryAsync(int role)
    {
        return await _context.AgreementTemplateHistories
            .Where(h => h.Role == role)
            .OrderByDescending(h => h.Version)
            .Select(h => new AgreementTemplateHistoryDto
            {
                Role = h.Role,
                FilePath = h.FilePath,
                Version = h.Version,
                UploadedAt = h.UploadedAt,
                ArchivedAt = h.ArchivedAt,
                UploadedByAdminId = h.UploadedByAdminId
            })
            .ToListAsync();
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    public async Task SendAgreementForSigningAsync(Guid portalUserId, int role)
    {
        var user = await _context.PortalUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == portalUserId);
        if (user == null)
        {
            _logger.LogWarning("SendAgreementForSigningAsync: portal user {UserId} not found", portalUserId);
            return;
        }

        var template = await _context.AgreementTemplates
            .Where(t => t.Role == role)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync();
        var templateVersion = template?.Version ?? 0;

        var ua = await _context.UserAgreements.FirstOrDefaultAsync(a => a.PortalUserId == portalUserId);
        if (ua == null)
        {
            ua = new UserAgreement
            {
                Id = Guid.NewGuid(),
                PortalUserId = portalUserId,
                TemplateVersion = templateVersion,
                Status = AgreementStatus.Sent
            };
            _context.UserAgreements.Add(ua);
        }
        else if (ua.Status != AgreementStatus.Verified)
        {
            ua.TemplateVersion = templateVersion;
            ua.Status = AgreementStatus.Sent;
            ua.UploadedFilePath = null;
            ua.UploadedAt = null;
            ua.RejectionReason = null;
            ua.VerifiedByAdminId = null;
            ua.VerifiedAt = null;
        }

        await _context.SaveChangesAsync();

        try
        {
            await _emailService.SendAgreementReadyToSignEmailAsync(user.Email, user.FirstName, RoleName(role));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send agreement-ready email to {Email}", user.Email);
        }

        _logger.LogInformation("Agreement sent for signing to portal user {UserId} (role {Role}, template v{Version})",
            portalUserId, role, templateVersion);
    }

    public async Task UploadSignedAgreementAsync(Guid portalUserId, IFormFile file)
    {
        var ua = await _context.UserAgreements.FirstOrDefaultAsync(a => a.PortalUserId == portalUserId);
        if (ua == null)
        {
            var user = await _context.PortalUsers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == portalUserId);
            var template = user == null ? null : await _context.AgreementTemplates
                .Where(t => t.Role == (int)user.PortalUserType)
                .OrderByDescending(t => t.Version)
                .FirstOrDefaultAsync();

            ua = new UserAgreement
            {
                Id = Guid.NewGuid(),
                PortalUserId = portalUserId,
                TemplateVersion = template?.Version ?? 0
            };
            _context.UserAgreements.Add(ua);
        }

        ua.UploadedFilePath = await SaveFileAsync(file, "agreements");
        ua.UploadedAt = DateTime.UtcNow;
        ua.Status = AgreementStatus.PendingVerification;
        ua.RejectionReason = null;
        ua.VerifiedByAdminId = null;
        ua.VerifiedAt = null;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Signed agreement uploaded by portal user {UserId}", portalUserId);

        // Alert management that a signed copy is now pending verification (gated — Account group).
        var portalUser = await _context.PortalUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == portalUserId);
        if (portalUser != null)
        {
            try
            {
                await _notificationService.SendToRolesAsync(
                    new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin },
                    $"{portalUser.FirstName} {portalUser.LastName} uploaded a signed agreement awaiting your verification.",
                    "security", "Agreement", portalUserId.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to notify management of signed agreement upload by {UserId}", portalUserId);
            }
        }
    }

    public async Task VerifyAgreementAsync(Guid portalUserId, Guid adminId)
    {
        var ua = await _context.UserAgreements.FirstOrDefaultAsync(a => a.PortalUserId == portalUserId);
        if (ua == null) return;

        ua.Status = AgreementStatus.Verified;
        ua.VerifiedByAdminId = adminId;
        ua.VerifiedAt = DateTime.UtcNow;
        ua.RejectionReason = null;
        await _context.SaveChangesAsync();

        var user = await _context.PortalUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == portalUserId);
        if (user != null)
        {
            try { await _emailService.SendAgreementVerifiedEmailAsync(user.Email, user.FirstName, user.Id.ToString(), true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to send agreement-verified email to {Email}", user.Email); }

            try
            {
                await _notificationService.SendToUserAsync(portalUserId.ToString(),
                    "Your signed agreement has been verified.", "security", "Agreement", ua.Id.ToString());
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to push agreement-verified notification to {UserId}", portalUserId); }
        }

        _logger.LogInformation("Agreement verified for portal user {UserId} by admin {AdminId}", portalUserId, adminId);
    }

    public async Task RejectAgreementAsync(Guid portalUserId, Guid adminId, string reason)
    {
        var ua = await _context.UserAgreements.FirstOrDefaultAsync(a => a.PortalUserId == portalUserId);
        if (ua == null) return;

        ua.Status = AgreementStatus.Rejected;
        ua.RejectionReason = reason;
        ua.UploadedFilePath = null;   // must re-upload
        ua.UploadedAt = null;
        ua.VerifiedByAdminId = null;
        ua.VerifiedAt = null;
        await _context.SaveChangesAsync();

        var user = await _context.PortalUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == portalUserId);
        if (user != null)
        {
            try { await _emailService.SendAgreementRejectedEmailAsync(user.Email, user.FirstName, reason, user.Id.ToString(), true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to send agreement-rejected email to {Email}", user.Email); }

            try
            {
                await _notificationService.SendToUserAsync(portalUserId.ToString(),
                    $"Your signed agreement was rejected: {reason}. Please re-sign the agreement and upload it again.",
                    "security", "Agreement", ua.Id.ToString());
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to push agreement-rejected notification to {UserId}", portalUserId); }
        }

        _logger.LogInformation("Agreement rejected for portal user {UserId} by admin {AdminId}", portalUserId, adminId);
    }

    public async Task SendReminderAsync(Guid portalUserId, Guid adminId)
    {
        var user = await _context.PortalUsers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == portalUserId);
        if (user == null) return;

        var ua = await _context.UserAgreements.FirstOrDefaultAsync(a => a.PortalUserId == portalUserId);
        if (ua == null)
        {
            var template = await _context.AgreementTemplates
                .Where(t => t.Role == (int)user.PortalUserType)
                .OrderByDescending(t => t.Version)
                .FirstOrDefaultAsync();
            ua = new UserAgreement
            {
                Id = Guid.NewGuid(),
                PortalUserId = portalUserId,
                TemplateVersion = template?.Version ?? 0,
                Status = AgreementStatus.Sent
            };
            _context.UserAgreements.Add(ua);
        }

        ua.LastReminderSentAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var roleLabel = RoleName((int)user.PortalUserType);

        // Attach the current agreement PDF if we can read it off disk. Never let this block the reminder —
        // if there's no template or the read fails, the email still goes out without an attachment.
        string? attachmentFileName = null;
        byte[]? attachmentBytes = null;
        try
        {
            var template = await _context.AgreementTemplates
                .FirstOrDefaultAsync(x => x.Role == (int)user.PortalUserType);
            if (template == null || string.IsNullOrWhiteSpace(template.FilePath))
            {
                _logger.LogWarning("No agreement template on file for role {Role}; sending reminder to {Email} without attachment",
                    (int)user.PortalUserType, user.Email);
            }
            else
            {
                var diskPath = ResolvePrivatePath(template.FilePath);
                if (File.Exists(diskPath))
                {
                    attachmentBytes = await File.ReadAllBytesAsync(diskPath);
                    attachmentFileName = $"{roleLabel}-Agreement.pdf";
                }
                else
                {
                    _logger.LogWarning("Agreement template file missing on disk ({Path}); sending reminder to {Email} without attachment",
                        diskPath, user.Email);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load agreement template PDF for reminder to {Email}; sending without attachment", user.Email);
            attachmentFileName = null;
            attachmentBytes = null;
        }

        try
        {
            await _emailService.SendAgreementReminderEmailAsync(
                user.Email, user.FirstName, roleLabel, user.Id.ToString(), true,
                attachmentFileName, attachmentBytes);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to send agreement-reminder email to {Email}", user.Email); }

        try
        {
            await _notificationService.SendToUserAsync(portalUserId.ToString(),
                "Reminder: please sign your agreement and upload the signed copy.",
                "security", "Agreement", ua.Id.ToString());
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to push agreement-reminder notification to {UserId}", portalUserId); }

        _logger.LogInformation("Agreement reminder sent to portal user {UserId} by admin {AdminId}", portalUserId, adminId);
    }

    // ── Overview ───────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<UserAgreementStatusDto>> GetAllUserAgreementStatusesAsync(int? roleFilter)
    {
        var usersQuery = _context.PortalUsers.AsQueryable();
        if (roleFilter.HasValue)
            usersQuery = usersQuery.Where(u => (int)u.PortalUserType == roleFilter.Value);

        var users = await usersQuery
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.PortalUserType })
            .ToListAsync();

        var userIds = users.Select(u => u.Id).ToList();

        var agreements = (await _context.UserAgreements
                .Where(a => userIds.Contains(a.PortalUserId)).ToListAsync())
            .GroupBy(a => a.PortalUserId).ToDictionary(g => g.Key, g => g.First());

        var idDocs = (await _context.UserIdDocuments
                .Where(d => userIds.Contains(d.PortalUserId)).ToListAsync())
            .GroupBy(d => d.PortalUserId).ToDictionary(g => g.Key, g => g.First());

        // Role-context lookups
        var tenantContext = (await _context.Tenants
                .Where(t => userIds.Contains(t.Id))
                .Include(t => t.House).ThenInclude(h => h!.Flat)
                .Select(t => new { t.Id, HouseNumber = t.House != null ? t.House.HouseNumber : null, FlatName = t.House != null && t.House.Flat != null ? t.House.Flat.FlatName : null })
                .ToListAsync())
            .ToDictionary(x => x.Id, x =>
                x.HouseNumber == null ? null
                : x.FlatName == null ? $"House {x.HouseNumber}"
                : $"House {x.HouseNumber} — {x.FlatName}");

        var landlordAgency = (await _context.Landlords
                .Where(l => userIds.Contains(l.Id))
                .Select(l => new { l.Id, l.AgencyName }).ToListAsync())
            .ToDictionary(x => x.Id, x => x.AgencyName);

        var agentAgency = (await _context.Agents
                .Where(a => userIds.Contains(a.Id))
                .Select(a => new { a.Id, a.AgencyName }).ToListAsync())
            .ToDictionary(x => x.Id, x => x.AgencyName);

        return users.Select(u =>
        {
            agreements.TryGetValue(u.Id, out var a);
            idDocs.TryGetValue(u.Id, out var d);

            string? context = u.PortalUserType switch
            {
                PortalUserType.Tenant => tenantContext.TryGetValue(u.Id, out var tc) ? tc : null,
                PortalUserType.Landlord => landlordAgency.TryGetValue(u.Id, out var lc) ? lc : null,
                PortalUserType.Agent => agentAgency.TryGetValue(u.Id, out var ac) ? ac : null,
                _ => null
            };

            return new UserAgreementStatusDto
            {
                PortalUserId = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                Role = u.PortalUserType.ToString(),
                Context = context,
                AgreementStatus = (a?.Status ?? AgreementStatus.NotSent).ToString(),
                TemplateVersion = a?.TemplateVersion ?? 0,
                UploadedFilePath = a?.UploadedFilePath,
                AgreementUploadedAt = a?.UploadedAt,
                RejectionReason = a?.RejectionReason,
                LastReminderSentAt = a?.LastReminderSentAt,
                HasIdFront = d?.FrontImagePath != null,
                HasIdBack = d?.BackImagePath != null,
                IdUploadedAt = d?.UploadedAt
            };
        }).ToList();
    }

    // ── ID documents ───────────────────────────────────────────────────────

    public async Task UploadIdDocumentAsync(Guid portalUserId, IFormFile? front, IFormFile? back)
    {
        var doc = await _context.UserIdDocuments.FirstOrDefaultAsync(d => d.PortalUserId == portalUserId);
        if (doc == null)
        {
            doc = new UserIdDocument { Id = Guid.NewGuid(), PortalUserId = portalUserId };
            _context.UserIdDocuments.Add(doc);
        }

        if (front != null && front.Length > 0)
            doc.FrontImagePath = await SaveFileAsync(front, "id-documents");
        if (back != null && back.Length > 0)
            doc.BackImagePath = await SaveFileAsync(back, "id-documents");

        doc.UploadedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        _logger.LogInformation("ID document updated for portal user {UserId}", portalUserId);
    }

    // ── Portal-side reads ──────────────────────────────────────────────────

    public async Task<MyAgreementDto> GetMyAgreementAsync(Guid portalUserId)
    {
        var user = await _context.PortalUsers.FirstOrDefaultAsync(u => u.Id == portalUserId);
        var role = user == null ? -1 : (int)user.PortalUserType;

        var template = role < 0 ? null : await _context.AgreementTemplates
            .Where(t => t.Role == role)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync();

        var ua = await _context.UserAgreements.FirstOrDefaultAsync(a => a.PortalUserId == portalUserId);

        return new MyAgreementDto
        {
            TemplateFilePath = template?.FilePath,
            TemplateVersion = template?.Version ?? 0,
            Status = (ua?.Status ?? AgreementStatus.NotSent).ToString(),
            MyUploadedFilePath = ua?.UploadedFilePath,
            MyUploadedAt = ua?.UploadedAt,
            RejectionReason = ua?.RejectionReason
        };
    }

    public async Task<MyIdDocumentDto> GetMyIdDocumentAsync(Guid portalUserId)
    {
        var d = await _context.UserIdDocuments.FirstOrDefaultAsync(x => x.PortalUserId == portalUserId);
        return new MyIdDocumentDto
        {
            FrontImagePath = d?.FrontImagePath,
            BackImagePath = d?.BackImagePath,
            UploadedAt = d?.UploadedAt
        };
    }

    // ── Authenticated file serving ─────────────────────────────────────────

    public async Task<AgreementFileResult?> GetTemplateFileAsync(int role)
    {
        var t = await _context.AgreementTemplates.FirstOrDefaultAsync(x => x.Role == role);
        return t == null ? null : await ReadPrivateFileAsync(t.FilePath, $"{RoleName(role)}-Agreement.pdf");
    }

    public async Task<AgreementFileResult?> GetTemplateHistoryFileAsync(int role, int version)
    {
        var h = await _context.AgreementTemplateHistories
            .FirstOrDefaultAsync(x => x.Role == role && x.Version == version);
        return h == null ? null : await ReadPrivateFileAsync(h.FilePath, $"{RoleName(role)}-Agreement-v{version}.pdf");
    }

    public async Task<AgreementFileResult?> GetUploadedAgreementFileAsync(Guid portalUserId)
    {
        var ua = await _context.UserAgreements.FirstOrDefaultAsync(a => a.PortalUserId == portalUserId);
        return ua == null ? null : await ReadPrivateFileAsync(ua.UploadedFilePath, "Signed-Agreement.pdf");
    }

    public async Task<AgreementFileResult?> GetIdDocumentFileAsync(Guid portalUserId, string side)
    {
        var d = await _context.UserIdDocuments.FirstOrDefaultAsync(x => x.PortalUserId == portalUserId);
        if (d == null) return null;

        var path = side?.ToLowerInvariant() switch
        {
            "front" => d.FrontImagePath,
            "back" => d.BackImagePath,
            _ => null
        };
        return await ReadPrivateFileAsync(path, $"ID-{side?.ToLowerInvariant() ?? "image"}");
    }

    public async Task<AgreementFileResult?> GetMyTemplateFileAsync(Guid portalUserId)
    {
        var user = await _context.PortalUsers.FirstOrDefaultAsync(u => u.Id == portalUserId);
        return user == null ? null : await GetTemplateFileAsync((int)user.PortalUserType);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string RoleName(int role) =>
        Enum.IsDefined(typeof(PortalUserType), role) ? ((PortalUserType)role).ToString() : $"Role {role}";

    private static readonly Dictionary<string, string> ContentTypeByExt = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp"
    };

    /// <summary>
    /// Maps a DB-stored private relative path ("agreements/{guid}.pdf") to a physical file under
    /// {cwd}/PrivateUploads. Tolerates the legacy "/uploads/..." URL fragment and falls back to the
    /// old wwwroot/uploads location so pre-move rows still resolve.
    /// </summary>
    private static string ResolvePrivatePath(string storedPath)
    {
        var rel = storedPath.Replace('\\', '/').TrimStart('/');
        if (rel.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            rel = rel["uploads/".Length..];

        var priv = Path.Combine(Directory.GetCurrentDirectory(), "PrivateUploads", rel);
        if (File.Exists(priv)) return priv;

        var legacy = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", rel);
        return File.Exists(legacy) ? legacy : priv;
    }

    private static async Task<AgreementFileResult?> ReadPrivateFileAsync(string? storedPath, string downloadName)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;

        var disk = ResolvePrivatePath(storedPath);
        if (!File.Exists(disk)) return null;

        var bytes = await File.ReadAllBytesAsync(disk);
        var ext = Path.GetExtension(disk);
        var contentType = ContentTypeByExt.TryGetValue(ext, out var ct) ? ct : "application/octet-stream";
        var name = Path.HasExtension(downloadName) ? downloadName : downloadName + ext;
        return new AgreementFileResult(bytes, contentType, name);
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
