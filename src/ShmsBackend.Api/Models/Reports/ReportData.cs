using System;
using System.Collections.Generic;
using System.Linq;

namespace ShmsBackend.Api.Models.Reports;

public class ReportColumn
{
    public string Key { get; set; } = "";
    public string Header { get; set; } = "";
    /// <summary>Numeric (money / points) column that gets a grand total in the report.</summary>
    public bool SumTotal { get; set; }
    /// <summary>Numeric column that is right-aligned but not summed (balances, day counts).</summary>
    public bool Numeric { get; set; }
    public bool RightAlign => SumTotal || Numeric;
}

public class ReportData
{
    public string Title { get; set; } = "";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string FilterSummary { get; set; } = "";
    public List<ReportColumn> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();

    /// <summary>Number of data rows (shown with the totals so long lists can be sanity-checked).</summary>
    public int RecordCount => Rows.Count;

    /// <summary>Grand total per <see cref="ReportColumn.SumTotal"/> column, keyed by column key.</summary>
    public Dictionary<string, decimal> Totals
    {
        get
        {
            var totals = new Dictionary<string, decimal>();
            foreach (var col in Columns.Where(c => c.SumTotal))
            {
                decimal sum = 0;
                foreach (var row in Rows)
                {
                    if (!row.TryGetValue(col.Key, out var v) || v == null) continue;
                    sum += v switch
                    {
                        decimal d => d,
                        double db => (decimal)db,
                        int i => i,
                        long l => l,
                        float fl => (decimal)fl,
                        _ => decimal.TryParse(v.ToString(), out var parsed) ? parsed : 0m
                    };
                }
                totals[col.Key] = sum;
            }
            return totals;
        }
    }
}
