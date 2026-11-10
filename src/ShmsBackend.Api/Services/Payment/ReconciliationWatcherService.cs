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
    private const string PaidEntityType = "ReconciliationPaid";
    private bool _first = true;

    private readonly IServiceProvider _sp;
    private readonly ILogger<ReconciliationWatcherService> _log;

    public ReconciliationWatcherService(IServiceProvider sp, ILogger<ReconciliationWatcherService> log) { _sp = sp; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogError(ex, "Reconciliation watcher run failed"); }
            try { await Task.Delay(Every, ct); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>M-Pesa has taken the money but no callback reached the app: tell the accountant once, with the exact next step.</summary>
    private static async Task AnnouncePaidAsync(ShmsDbContext db, INotificationService notify, List<ReconciliationController.Issue> paid, CancellationToken ct)
    {
        if (paid.Count == 0) return;
        var ids = paid.Select(i => IdFor("paid|" + i.Key)).Distinct().ToList();
        var told = (await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.EntityType == PaidEntityType && n.EntityId != null && ids.Contains(n.EntityId.Value))
            .Select(n => n.EntityId!.Value).ToListAsync(ct)).ToHashSet();
        foreach (var i in paid.Where(i => !told.Contains(IdFor("paid|" + i.Key))).Take(20))
        {
            var who = string.IsNullOrWhiteSpace(i.TenantName) ? "" : $" for {i.TenantName}{(string.IsNullOrWhiteSpace(i.House) ? "" : $" ({i.House})")}";
            await notify.SendToRoleAsync(NotificationAudience.Accountant,
                $"M-Pesa confirms a prompt{who} was paid, but the app never received it. Open Reconciliation, check it with M-Pesa and record it with the receipt number from the M-Pesa message.",
                "payment", PaidEntityType, IdFor("paid|" + i.Key).ToString());
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

        // fix what can be fixed safely first; only what is left needs a person
        var healer = scope.ServiceProvider.GetRequiredService<ReconciliationHealer>();
        // the first run after a start looks harder: the server may have been down while payments were made
        var healed = await healer.HealAsync(scan.Open, _first ? 60 : 10);
        _first = false;
        await AnnouncePaidAsync(db, notify, healer.PaidAtMpesa, ct);
        var open = scan.Open.Where(i => !healed.Contains(i.Key)).ToList();
        if (healed.Count > 0) _log.LogInformation("Reconciliation auto-fixed {Count} findings", healed.Count);

        await RemindAboutExpiringAsync(db, notify, open, ct);
        if (open.Count == 0) return;

        var ids = open.Select(i => IdFor(i.Key)).Distinct().ToList();
        var told = (await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(n => n.EntityType == EntityType && n.EntityId != null && ids.Contains(n.EntityId.Value))
            .Select(n => n.EntityId!.Value).ToListAsync(ct)).ToHashSet();

        var fresh = open.Where(i => !told.Contains(IdFor(i.Key))).ToList();
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

    /// <summary>
    /// Once a day (from 08:00 East Africa time) tells the accountants about open findings that are within a day of falling out of
    /// M-Pesa's 3-day lookup, after which M-Pesa can no longer be asked about them.
    /// </summary>
    private async Task RemindAboutExpiringAsync(ShmsDbContext db, INotificationService notify, List<Controllers.ReconciliationController.Issue> open, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (now.Hour < 5) return;
        var reminderId = IdFor("reminder|" + now.ToString("yyyy-MM-dd"));
        if (await db.Notifications.IgnoreQueryFilters().AnyAsync(n => n.EntityType == "ReconciliationReminder" && n.EntityId == reminderId, ct)) return;

        var expiring = open.Where(i => !string.IsNullOrEmpty(i.CheckoutRequestId) && now - i.At >= TimeSpan.FromHours(48) && now - i.At <= TimeSpan.FromDays(3)).OrderBy(i => i.At).ToList();
        if (expiring.Count == 0) return;
        var sample = string.Join("; ", expiring.Take(3).Select(i => $"{i.Source} {i.Reference}"));
        await notify.SendToRoleAsync(NotificationAudience.Accountant,
            $"Reconciliation reminder: {expiring.Count} payment{(expiring.Count == 1 ? "" : "s")} still need checking and will soon be too old for M-Pesa to look up ({sample}{(expiring.Count > 3 ? "…" : "")}). Please check {(expiring.Count == 1 ? "it" : "them")} today.",
            "payment", "ReconciliationReminder", reminderId.ToString());
    }
}
