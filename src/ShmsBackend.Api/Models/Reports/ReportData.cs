using System;
using System.Collections.Generic;

namespace ShmsBackend.Api.Models.Reports;

public class ReportColumn
{
    public string Key { get; set; } = "";
    public string Header { get; set; } = "";
}

public class ReportData
{
    public string Title { get; set; } = "";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string FilterSummary { get; set; } = "";
    public List<ReportColumn> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
}
