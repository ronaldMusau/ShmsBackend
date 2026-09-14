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
}

public class AnalyticsTrendResult
{
    public List<string> Labels { get; set; } = new();
    public List<int> Values { get; set; } = new();
    public string Granularity { get; set; } = ""; // "weekly" or "monthly"
}
