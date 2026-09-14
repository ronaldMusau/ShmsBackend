using System.Collections.Generic;

namespace ShmsBackend.Api.Models.Analytics;

public class AnalyticsCategoryItem
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public decimal? Amount { get; set; }
}

public class AnalyticsBreakdownResult
{
    public List<AnalyticsCategoryItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public decimal? TotalAmount { get; set; }
    // Only populated by breakdowns backed by a genuine outstanding-balance figure (e.g. Payment status
    // breakdown's Balance sum). Null for every other domain's breakdown.
    public decimal? TotalOutstanding { get; set; }
}

public class AnalyticsTrendResult
{
    public List<string> Labels { get; set; } = new();
    public List<int> Values { get; set; } = new();
    // Populated instead of Values for money-summed trends (Refunds/Deductions/Service Charges) —
    // Values stays int-only (Complaint-count trends), so a decimal amount is never truncated into it.
    public List<decimal>? AmountValues { get; set; }
    public string Granularity { get; set; } = ""; // "weekly" or "monthly"
}
