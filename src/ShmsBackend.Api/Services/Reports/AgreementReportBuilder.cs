using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Enums;

namespace ShmsBackend.Api.Services.Reports;

public class AgreementFilters
{
    public string? Role { get; set; }    // "Landlord"/"Agent"/"Tenant", matches GetAllUserAgreementStatusesAsync's existing role-string handling
    public string? Status { get; set; }  // AgreementStatus enum
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }  // both against UploadedAt
}

/// <summary>
/// Builds agreement-status ReportData by replicating AgreementService.GetAllUserAgreementStatusesAsync's
/// exact base query (PortalUsers filtered by role) and batch-loading logic (UserAgreements +
/// Tenant-only House/Flat context) — there is no separate query service for this report, matching that
/// method's own shape (no pagination, a handful of batch dictionary lookups). No FlatId filter: it
/// genuinely doesn't apply across Landlord/Agent/Tenant rows alike.
/// </summary>
public class AgreementReportBuilder
{
    private readonly ShmsDbContext _context;

    public AgreementReportBuilder(ShmsDbContext context)
    {
        _context = context;
    }

    public async Task<ReportData> BuildAsync(AgreementFilters filters)
    {
        var usersQuery = _context.PortalUsers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filters.Role) &&
            Enum.TryParse<PortalUserType>(filters.Role, true, out var parsedRole) &&
            Enum.IsDefined(typeof(PortalUserType), parsedRole))
        {
            usersQuery = usersQuery.Where(u => (int)u.PortalUserType == (int)parsedRole);
        }

        var users = await usersQuery
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.PortalUserType })
            .ToListAsync();

        var userIds = users.Select(u => u.Id).ToList();

        var agreements = (await _context.UserAgreements
                .Where(a => userIds.Contains(a.PortalUserId)).ToListAsync())
            .GroupBy(a => a.PortalUserId).ToDictionary(g => g.Key, g => g.First());

        var tenantContext = (await _context.Tenants
                .Where(t => userIds.Contains(t.Id))
                .Include(t => t.House).ThenInclude(h => h!.Flat)
                .Select(t => new { t.Id, HouseNumber = t.House != null ? t.House.HouseNumber : null, FlatName = t.House != null && t.House.Flat != null ? t.House.Flat.FlatName : null })
                .ToListAsync())
            .ToDictionary(x => x.Id, x =>
                x.HouseNumber == null ? null
                : x.FlatName == null ? $"House {x.HouseNumber}"
                : $"House {x.HouseNumber} — {x.FlatName}");

        string? parsedStatusFilter = null;
        if (!string.IsNullOrWhiteSpace(filters.Status) &&
            Enum.TryParse<AgreementStatus>(filters.Status, true, out var parsedStatus))
        {
            parsedStatusFilter = parsedStatus.ToString();
        }

        var rows = new List<Dictionary<string, object?>>();
        foreach (var u in users)
        {
            agreements.TryGetValue(u.Id, out var a);

            var status = (a?.Status ?? AgreementStatus.NotSent).ToString();
            if (parsedStatusFilter != null && status != parsedStatusFilter)
                continue;

            var uploadedAt = a?.UploadedAt;
            if (filters.FromDate.HasValue && (uploadedAt == null || uploadedAt < filters.FromDate.Value))
                continue;
            if (filters.ToDate.HasValue && (uploadedAt == null || uploadedAt > filters.ToDate.Value.AddDays(1)))
                continue;

            var property = u.PortalUserType == PortalUserType.Tenant
                ? tenantContext.TryGetValue(u.Id, out var tc) ? tc : null
                : null;

            rows.Add(new Dictionary<string, object?>
            {
                ["name"] = $"{u.FirstName} {u.LastName}".Trim(),
                ["role"] = u.PortalUserType.ToString(),
                ["templateVersion"] = a?.TemplateVersion ?? 0,
                ["status"] = status,
                ["uploadedDate"] = uploadedAt,
                ["verifiedDate"] = a?.VerifiedAt,
                ["property"] = property ?? ""
            });
        }

        return new ReportData
        {
            Title = "Agreements Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters),
            Columns = new List<ReportColumn>
            {
                new() { Key = "name", Header = "Name" },
                new() { Key = "role", Header = "Role" },
                new() { Key = "templateVersion", Header = "Template Version" },
                new() { Key = "status", Header = "Status" },
                new() { Key = "uploadedDate", Header = "Uploaded Date" },
                new() { Key = "verifiedDate", Header = "Verified Date" },
                new() { Key = "property", Header = "Property" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(AgreementFilters filters)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(filters.Role)) parts.Add($"Role: {filters.Role}");
        if (!string.IsNullOrWhiteSpace(filters.Status)) parts.Add($"Status: {filters.Status}");

        var dateRange = ReportBuilderHelpers.FormatDateRange(filters.FromDate, filters.ToDate);
        if (dateRange != null) parts.Add(dateRange);

        return parts.Count == 0 ? "All agreements" : string.Join(" | ", parts);
    }
}
