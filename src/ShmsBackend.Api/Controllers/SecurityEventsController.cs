using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using System.Security.Claims;

namespace ShmsBackend.Api.Controllers;

/// <summary>Security alert centre. SuperAdmin and Admin only; covers the whole system.</summary>
[ApiController]
[Route("api/security-events")]
[Authorize(Roles = "SuperAdmin,Admin")]
public class SecurityEventsController : ControllerBase
{
    private readonly ShmsDbContext _db;
    public SecurityEventsController(ShmsDbContext db) => _db = db;

    /// <summary>Anything about a SuperAdmin account is for SuperAdmins only.</summary>
    private IQueryable<SecurityEvent> Visible(IQueryable<SecurityEvent> q) =>
        User.IsInRole("SuperAdmin") ? q : q.Where(e => e.EventType != "SuperAdminTargeted" && (e.ActorRole == null || e.ActorRole != "SuperAdmin"));

    private IQueryable<SecurityEvent> Filter(DateTime? from, DateTime? to, string? severity, string? source,
        bool? acknowledged, string? search)
    {
        var q = Visible(_db.SecurityEvents.AsNoTracking().AsQueryable());
        if (from.HasValue) q = q.Where(e => e.CreatedAt >= from.Value.ToUniversalTime());
        if (to.HasValue) q = q.Where(e => e.CreatedAt < to.Value.ToUniversalTime().AddDays(1));
        if (!string.IsNullOrWhiteSpace(severity)) q = q.Where(e => e.Severity == severity);
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(e => e.Source == source);
        if (acknowledged.HasValue) q = q.Where(e => e.IsAcknowledged == acknowledged.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e => e.Summary.Contains(s) || e.EventType.Contains(s) || (e.IpAddress != null && e.IpAddress.Contains(s)));
        }
        return q;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? severity, [FromQuery] string? source, [FromQuery] bool? acknowledged,
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(page, 1);
        var q = Filter(from, to, severity, source, acknowledged, search);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(e => e.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int days = 7)
    {
        days = Math.Clamp(days, 1, 90);
        var since = DateTime.UtcNow.AddDays(-days);
        var rows = await Visible(_db.SecurityEvents.AsNoTracking()).Where(e => e.CreatedAt >= since).ToListAsync();
        return Ok(new
        {
            days,
            total = rows.Count,
            unacknowledged = rows.Count(r => !r.IsAcknowledged),
            bySeverity = rows.GroupBy(r => r.Severity).Select(g => new { key = g.Key, count = g.Count() }),
            bySource = rows.GroupBy(r => r.Source).Select(g => new { key = g.Key, count = g.Count() }),
            byDay = rows.GroupBy(r => r.CreatedAt.Date).OrderBy(g => g.Key).Select(g => new { date = g.Key, count = g.Count() })
        });
    }

    [HttpPatch("{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id)
    {
        var e = await _db.SecurityEvents.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        e.IsAcknowledged = true;
        e.AcknowledgedBy = User.FindFirstValue(ClaimTypes.NameIdentifier);
        e.AcknowledgedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpPatch("acknowledge-all")]
    public async Task<IActionResult> AcknowledgeAll()
    {
        var by = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var now = DateTime.UtcNow;
        var n = await _db.SecurityEvents.Where(e => !e.IsAcknowledged)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsAcknowledged, true)
                                      .SetProperty(e => e.AcknowledgedBy, by)
                                      .SetProperty(e => e.AcknowledgedAt, now));
        return Ok(new { acknowledged = n });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? severity, [FromQuery] string? source, [FromQuery] bool? acknowledged,
        [FromQuery] string? search)
    {
        var rows = await Filter(from, to, severity, source, acknowledged, search)
            .OrderByDescending(e => e.CreatedAt).Take(5000).ToListAsync();
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Security events");
        string[] h = { "Time (UTC)", "Severity", "Source", "Event", "Summary", "Role", "IP", "Acknowledged" };
        for (var i = 0; i < h.Length; i++) ws.Cell(1, i + 1).Value = h[i];
        ws.Row(1).Style.Font.Bold = true;
        for (var r = 0; r < rows.Count; r++)
        {
            var e = rows[r]; var n = r + 2;
            ws.Cell(n, 1).Value = e.CreatedAt; ws.Cell(n, 2).Value = e.Severity; ws.Cell(n, 3).Value = e.Source;
            ws.Cell(n, 4).Value = e.EventType; ws.Cell(n, 5).Value = e.Summary; ws.Cell(n, 6).Value = e.ActorRole ?? "";
            ws.Cell(n, 7).Value = e.IpAddress ?? ""; ws.Cell(n, 8).Value = e.IsAcknowledged ? "Yes" : "No";
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream(); wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"security-events-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }
}
