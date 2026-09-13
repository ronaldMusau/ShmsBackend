using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Services.Reports;

/// <summary>
/// Small helpers shared across *ReportBuilder classes (and, where the same lookup is needed outside
/// a report, plain controllers) so neither owner duplicates a method the others depend on (previously
/// FormatDateRange lived statically on TenantReportBuilder and was called from PaymentReportBuilder).
/// </summary>
public static class ReportBuilderHelpers
{
    public static async Task<string?> ResolveFlatNameAsync(ShmsDbContext context, Guid? flatId)
    {
        if (!flatId.HasValue) return null;
        return await context.Flats
            .IgnoreQueryFilters()
            .Where(f => f.Id == flatId.Value)
            .Select(f => f.FlatName)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Resolves a "First Last" display name per creator id, where the id may belong to either a
    /// Landlord-side PortalUser (self-logged) or a staff Admin (SuperAdmin/Admin/Accountant) —
    /// looked up across both base tables and merged. Used by ExpenseReportBuilder and by
    /// ExpenseController's list endpoints so the frontend never needs its own name-lookup call.
    /// </summary>
    public static async Task<Dictionary<Guid, string>> ResolveCreatorNamesAsync(ShmsDbContext context, IEnumerable<Guid> creatorIds)
    {
        var ids = creatorIds.Distinct().ToList();
        var portalUserNames = await context.PortalUsers
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}");
        var adminNames = await context.Admins
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => $"{a.FirstName} {a.LastName}");
        var creatorNames = new Dictionary<Guid, string>(portalUserNames);
        foreach (var kv in adminNames) creatorNames[kv.Key] = kv.Value;
        return creatorNames;
    }

    public static string? FormatDateRange(DateTime? fromDate, DateTime? toDate)
    {
        string Fmt(DateTime d) => d.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

        if (fromDate.HasValue && toDate.HasValue) return $"From {Fmt(fromDate.Value)} to {Fmt(toDate.Value)}";
        if (fromDate.HasValue) return $"From {Fmt(fromDate.Value)}";
        if (toDate.HasValue) return $"To {Fmt(toDate.Value)}";
        return null;
    }
}
