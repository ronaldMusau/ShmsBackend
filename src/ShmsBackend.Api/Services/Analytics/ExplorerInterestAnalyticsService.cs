using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;
using ShmsBackend.Api.Services;

namespace ShmsBackend.Api.Services.Analytics;

public class ExplorerInterestAnalyticsService
{
    private readonly ExplorerInterestQueryService _explorerInterestQueryService;

    public ExplorerInterestAnalyticsService(ExplorerInterestQueryService explorerInterestQueryService)
    {
        _explorerInterestQueryService = explorerInterestQueryService;
    }

    public async Task<AnalyticsBreakdownResult> GetStatusBreakdownAsync(ExplorerFilters filters)
    {
        var baseQuery = _explorerInterestQueryService.BuildFilteredQuery(filters);

        var grouped = await baseQuery
            .GroupBy(ei => ei.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        // Exhaustive, re-confirmed set of ExplorerInterest.Status string literals.
        string[] allStatuses = { "Pending", "Superseded", "Converted" };
        var items = allStatuses
            .Select(status => new AnalyticsCategoryItem
            {
                Label = status,
                Count = grouped.FirstOrDefault(g => g.Status == status)?.Count ?? 0,
                Amount = null // no natural monetary figure for this domain
            })
            .ToList();

        return new AnalyticsBreakdownResult
        {
            Items = items,
            TotalCount = items.Sum(i => i.Count),
            TotalAmount = null
        };
    }
}
