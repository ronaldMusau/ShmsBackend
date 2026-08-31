using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Services.Auth;

/// <summary>
/// Rotates the weekly client-portal support password exactly 7 days after the last generation
/// (no fixed day/time): on startup and hourly thereafter, if the current active row has passed its
/// ExpiresAt — or none exists yet — it calls GenerateAndRotateAsync(). Independent of the
/// management-side WeeklyPasswordSchedulerService; mirrors the PaymentSchedulerService pattern.
/// </summary>
public class WeeklyClientPasswordSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WeeklyClientPasswordSchedulerService> _logger;

    public WeeklyClientPasswordSchedulerService(
        IServiceProvider serviceProvider,
        ILogger<WeeklyClientPasswordSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Weekly Client Password Scheduler Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunRotationCheck();
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Weekly Client Password Scheduler");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    private async Task RunRotationCheck()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();
        var weeklyClientPasswordService = scope.ServiceProvider.GetRequiredService<IWeeklyClientPasswordService>();

        var now = DateTime.UtcNow;
        var current = await context.WeeklyClientPasswords
            .Where(w => w.IsActive)
            .OrderByDescending(w => w.GeneratedAt)
            .FirstOrDefaultAsync();

        if (current == null || current.ExpiresAt <= now)
        {
            _logger.LogInformation("Weekly client-portal support password rotation due ({State})",
                current == null ? "none exists" : $"expired {current.ExpiresAt:o}");
            await weeklyClientPasswordService.GenerateAndRotateAsync();
        }
    }
}
