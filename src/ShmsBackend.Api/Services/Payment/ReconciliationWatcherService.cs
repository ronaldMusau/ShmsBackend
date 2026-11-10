using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Services.Payment;

/// <summary>
/// Runs the reconciliation check on a timer and tells the accountants when a finding appears that they have not been
/// told about yet. Each finding is announced once (its notification is the record), so restarts and repeated runs do not
/// repeat alerts, and nothing is announced for findings that were already reconciled.
/// </summary>
public class ReconciliationWatcherService : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(10);
    private const string EntityType = "ReconciliationIssue";

    private readonly IServiceProvider _sp;
    private readonly ILogger<ReconciliationWatcherService> _log;

    public ReconciliationWatcherService(IServiceProvider sp, ILogger<ReconciliationWatcherService> log) { _sp = sp; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogError(ex, "Reconciliation watcher run failed"); }
            try { await Task.Delay(Every, ct); } catch (OperationCanceledException) { return; }
        }
    }

    private static Guid IdFor(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes(key)));

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();
        var scanner = scope.ServiceProvider.GetRequiredService<ReconciliationScanner>();
        var notify = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var end = DateTime.UtcNow.Date.AddDays(1);
        var scan = await scanner.ScanAsync(end.AddDays(-31), end);
        if (scan.Open.Count == 0) return;

        var ids = scan.Open.Select(i => IdFor(i.Key)).Distinct().ToList();
        var told = (await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.EntityType == EntityType && n.EntityId != null && ids.Contains(n.EntityId.Value))
            .Select(n => n.EntityId!.Value).ToListAsync(ct)).ToHashSet();

        var fresh = scan.Open.Where(i => !told.Contains(IdFor(i.Key))).ToList();
        if (fresh.Count == 0) return;

        foreach (var i in fresh.Take(20))
        {
            var who = string.IsNullOrWhiteSpace(i.TenantName) ? "" : $" ({i.TenantName}{(string.IsNullOrWhiteSpace(i.House) ? "" : $", {i.House}")})";
            await notify.SendToRoleAsync(NotificationAudience.Accountant,
                $"Reconciliation: a payment needs checking. {i.Source} {i.Reference}{who}. {i.Detail}",
                "payment", EntityType, IdFor(i.Key).ToString());
        }
        if (fresh.Count > 20)
            await notify.SendToRoleAsync(NotificationAudience.Accountant,
                $"Reconciliation: {fresh.Count - 20} more payments need checking. Open Reconciliation to see them.",
                "payment", EntityType, IdFor("overflow|" + DateTime.UtcNow.Ticks).ToString());
        _log.LogInformation("Reconciliation watcher told the accountants about {Count} new findings", fresh.Count);
    }
}
