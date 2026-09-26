using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace ShmsBackend.Api.Services.Payment;

public class PreRegistrationExpirySchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PreRegistrationExpirySchedulerService> _logger;
    private DateTime? _lastRunTime;
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan ExpiryAge = TimeSpan.FromHours(72);

    public PreRegistrationExpirySchedulerService(
        IServiceProvider serviceProvider,
        ILogger<PreRegistrationExpirySchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Pre-Registration Expiry Scheduler Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                // Runs every 6 hours (not once daily) — 72h expiry needs reasonably tight precision.
                if (_lastRunTime == null || now - _lastRunTime.Value >= RunInterval)
                {
                    await RunExpirySweep();
                    _lastRunTime = now;
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Pre-Registration Expiry Scheduler");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task RunExpirySweep()
    {
        _logger.LogInformation("Running unpaid pre-registration expiry sweep");
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();

        var cutoff = DateTime.UtcNow - ExpiryAge;

        // Same "nothing financial to preserve" reasoning as TenantService.CreateAsync's replace-stale-
        // registration logic — this is the time-triggered equivalent, freeing the house automatically
        // instead of waiting for a new registration attempt to trigger the replace.
        var staleTenants = await context.Tenants
            .Where(t => !t.IsDeleted
                && !t.HasCompletedInitialPayment
                && t.TenantStatus != TenantStatus.SettlingVacate
                && t.HouseId != null
                && t.CreatedAt < cutoff)
            .ToListAsync();

        if (staleTenants.Count == 0)
        {
            _logger.LogInformation("No stale unpaid pre-registrations found");
            return;
        }

        foreach (var tenant in staleTenants)
        {
            tenant.IsDeleted = true;
            tenant.DeletedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync();
        _logger.LogInformation("Expired {Count} stale unpaid pre-registration(s) older than 72 hours", staleTenants.Count);
    }
}
