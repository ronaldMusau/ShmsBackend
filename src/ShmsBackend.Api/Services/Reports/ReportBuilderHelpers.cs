using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Services.Reports;

/// <summary>
/// Small helpers shared across *ReportBuilder classes so neither builder owns a method the others
/// depend on (previously FormatDateRange lived statically on TenantReportBuilder and was called from
/// PaymentReportBuilder).
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

    public static string? FormatDateRange(DateTime? fromDate, DateTime? toDate)
    {
        string Fmt(DateTime d) => d.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

        if (fromDate.HasValue && toDate.HasValue) return $"From {Fmt(fromDate.Value)} to {Fmt(toDate.Value)}";
        if (fromDate.HasValue) return $"From {Fmt(fromDate.Value)}";
        if (toDate.HasValue) return $"To {Fmt(toDate.Value)}";
        return null;
    }
}
