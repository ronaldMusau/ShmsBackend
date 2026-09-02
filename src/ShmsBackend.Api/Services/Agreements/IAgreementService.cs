using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ShmsBackend.Api.Services.Agreements;

public interface IAgreementService
{
    // ── Template management (admin) ──────────────────────────────────────────
    Task UploadTemplateAsync(int role, IFormFile file, Guid adminId);
    Task<AgreementTemplateDto?> GetTemplateAsync(int role);
    Task<IReadOnlyList<AgreementTemplateHistoryDto>> GetTemplateHistoryAsync(int role);

    // ── Lifecycle ───────────────────────────────────────────────────────────
    /// <summary>Create/refresh a UserAgreement row (Status=Sent, current TemplateVersion) and send the always-on "please sign" email.</summary>
    Task SendAgreementForSigningAsync(Guid portalUserId, int role);
    Task UploadSignedAgreementAsync(Guid portalUserId, IFormFile file);
    Task VerifyAgreementAsync(Guid portalUserId, Guid adminId);
    Task RejectAgreementAsync(Guid portalUserId, Guid adminId, string reason);
    Task SendReminderAsync(Guid portalUserId, Guid adminId);

    // ── Overview (admin) ────────────────────────────────────────────────────
    Task<IReadOnlyList<UserAgreementStatusDto>> GetAllUserAgreementStatusesAsync(int? roleFilter);

    // ── ID documents ────────────────────────────────────────────────────────
    Task UploadIdDocumentAsync(Guid portalUserId, IFormFile? front, IFormFile? back);

    // ── Portal-side reads ───────────────────────────────────────────────────
    Task<MyAgreementDto> GetMyAgreementAsync(Guid portalUserId);
    Task<MyIdDocumentDto> GetMyIdDocumentAsync(Guid portalUserId);
}

public class AgreementTemplateDto
{
    public int Role { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime UploadedAt { get; set; }
    public Guid? UploadedByAdminId { get; set; }
}

public class AgreementTemplateHistoryDto
{
    public int Role { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime UploadedAt { get; set; }
    public DateTime ArchivedAt { get; set; }
    public Guid? UploadedByAdminId { get; set; }
}

public class UserAgreementStatusDto
{
    public Guid PortalUserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    /// <summary>Role-context: "House 12 — Flat A" for a tenant, agency name for an agent/landlord, else null.</summary>
    public string? Context { get; set; }
    public string AgreementStatus { get; set; } = string.Empty;
    public int TemplateVersion { get; set; }
    public string? UploadedFilePath { get; set; }
    public DateTime? AgreementUploadedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? LastReminderSentAt { get; set; }
    public bool HasIdFront { get; set; }
    public bool HasIdBack { get; set; }
    public DateTime? IdUploadedAt { get; set; }
}

public class MyAgreementDto
{
    public string? TemplateFilePath { get; set; }
    public int TemplateVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? MyUploadedFilePath { get; set; }
    public DateTime? MyUploadedAt { get; set; }
    public string? RejectionReason { get; set; }
}

public class MyIdDocumentDto
{
    public string? FrontImagePath { get; set; }
    public string? BackImagePath { get; set; }
    public DateTime? UploadedAt { get; set; }
}
