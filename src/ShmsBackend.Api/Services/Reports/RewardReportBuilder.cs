using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Api.Services;

namespace ShmsBackend.Api.Services.Reports;

/// <summary>
/// Builds reward-transaction-listing ReportData off RewardTransactionQueryService/RewardTransactionFilters.
/// FlatName here is resolved via the Tenant.House.Flat nav chain already loaded onto each row (see
/// RewardTransactionQueryService's Include chain) — NOT via ReportBuilderHelpers.ResolveFlatNameAsync,
/// which expects a direct FlatId column that RewardTransaction doesn't have.
/// </summary>
public class RewardReportBuilder
{
    private readonly RewardTransactionQueryService _rewardTransactionQueryService;

    public RewardReportBuilder(RewardTransactionQueryService rewardTransactionQueryService)
    {
        _rewardTransactionQueryService = rewardTransactionQueryService;
    }

    public async Task<ReportData> BuildAsync(RewardTransactionFilters filters)
    {
        var transactions = await _rewardTransactionQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        var rows = transactions.Select(t =>
        {
            var houseNumber = t.Tenant?.House?.HouseNumber;
            var flatName = t.Tenant?.House?.Flat?.FlatName;
            var flatUnit = houseNumber != null || flatName != null
                ? $"{houseNumber ?? "-"} - {flatName ?? "-"}"
                : "-";

            return new Dictionary<string, object?>
            {
                ["tenantName"] = t.Tenant != null ? $"{t.Tenant.FirstName} {t.Tenant.LastName}".Trim() : "-",
                ["flatUnit"] = flatUnit,
                ["transactionType"] = t.TransactionType,
                ["points"] = t.Points,
                ["balanceAfter"] = t.BalanceAfter,
                ["reference"] = t.RedemptionReference,
                ["date"] = t.CreatedAt
            };
        }).ToList();

        return new ReportData
        {
            Title = "Rewards Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters),
            Columns = new List<ReportColumn>
            {
                new() { Key = "tenantName", Header = "Tenant Name" },
                new() { Key = "flatUnit", Header = "Flat/Unit" },
                new() { Key = "transactionType", Header = "Transaction Type" },
                new() { Key = "points", Header = "Points" },
                new() { Key = "balanceAfter", Header = "Balance After" },
                new() { Key = "reference", Header = "Reference" },
                new() { Key = "date", Header = "Date" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(RewardTransactionFilters filters)
    {
        var parts = new List<string>();
        if (filters.TenantId.HasValue) parts.Add($"Tenant: {filters.TenantId}");
        if (filters.FlatId.HasValue) parts.Add($"Flat: {filters.FlatId}");
        if (filters.HouseId.HasValue) parts.Add($"House: {filters.HouseId}");
        if (!string.IsNullOrWhiteSpace(filters.TransactionType)) parts.Add($"Type: {filters.TransactionType}");
        if (!string.IsNullOrWhiteSpace(filters.Search)) parts.Add($"Search: \"{filters.Search}\"");

        var dateRange = ReportBuilderHelpers.FormatDateRange(filters.FromDate, filters.ToDate);
        if (dateRange != null) parts.Add(dateRange);

        if (filters.MinPoints.HasValue) parts.Add($"Points from {filters.MinPoints:N0}");
        if (filters.MaxPoints.HasValue) parts.Add($"Points up to {filters.MaxPoints:N0}");

        return parts.Count == 0 ? "All reward transactions" : string.Join(" | ", parts);
    }
}
