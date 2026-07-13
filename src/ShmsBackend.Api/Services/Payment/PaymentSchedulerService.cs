using ShmsBackend.Api.Services.Email;
using ShmsBackend.Data.Context;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Payment;

public class PaymentSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentSchedulerService> _logger;
    private DateTime? _lastMonthlyGenerationDate;
    private DateTime? _lastOverdueCheckDate;
    private DateTime? _lastReminderDate;

    public PaymentSchedulerService(
        IServiceProvider serviceProvider,
        ILogger<PaymentSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Payment Scheduler Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;

                // Run monthly generation on 1st of month at 00:05
                if (now.Day == 1 && now.Hour == 0 && now.Minute >= 5 && now.Minute < 7
                    && _lastMonthlyGenerationDate?.Date != now.Date)
                {
                    await RunMonthlyGeneration();
                    _lastMonthlyGenerationDate = now.Date;
                }

                // Run overdue check daily at 08:00
                if (now.Hour == 8 && now.Minute >= 0 && now.Minute < 2
                    && _lastOverdueCheckDate?.Date != now.Date)
                {
                    await RunOverdueCheck();
                    _lastOverdueCheckDate = now.Date;
                }

                // Run payment reminders daily at 09:00
                if (now.Hour == 9 && now.Minute >= 0 && now.Minute < 2
                    && _lastReminderDate?.Date != now.Date)
                {
                    await RunPaymentReminders();
                    _lastReminderDate = now.Date;
                }

                // Auto-timeout payments stuck in Processing on every tick
                await RunStuckPaymentTimeout();

                // Wait 1 minute before next check
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Payment Scheduler");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task RunMonthlyGeneration()
    {
        _logger.LogInformation("Running monthly payment generation");
        using var scope = _serviceProvider.CreateScope();
        var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();
        await paymentService.GenerateMonthlyPaymentsAsync();
    }

    private async Task RunOverdueCheck()
    {
        _logger.LogInformation("Running overdue payment check");
        using var scope = _serviceProvider.CreateScope();
        var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();
        await paymentService.CheckOverduePaymentsAsync();
    }

    private async Task RunStuckPaymentTimeout()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();

        var stuckThreshold = DateTime.UtcNow.AddMinutes(-2);
        var stuckPayments = await dbContext.Payments
            .Where(p => p.PaymentStatus == PaymentTransactionStatus.Processing && p.UpdatedAt < stuckThreshold)
            .ToListAsync();

        foreach (var stuck in stuckPayments)
        {
            stuck.PaymentStatus = PaymentTransactionStatus.Failed;
            stuck.MpesaResultDesc = "No response from user (auto-timeout).";
            stuck.RetryCount++;
            stuck.UpdatedAt = DateTime.UtcNow;
        }

        if (stuckPayments.Count > 0)
        {
            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Auto-timed out {Count} stuck payments", stuckPayments.Count);
        }
    }

    private async Task RunPaymentReminders()
    {
        _logger.LogInformation("Running payment reminders");
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var now = DateTime.UtcNow;
        var reminderDate = now.AddDays(3).Date;

        var upcomingPayments = await context.Payments
            .Include(p => p.Tenant)
            .Include(p => p.House)
            .ThenInclude(h => h!.Flat)
            .Where(p => p.PaymentStatus == PaymentTransactionStatus.Pending &&
                        p.DueDate.Date == reminderDate &&
                        !p.IsDeleted)
            .ToListAsync();

        foreach (var payment in upcomingPayments)
        {
            try
            {
                if (payment.Tenant != null)
                {
                    await emailService.SendPaymentReminderEmailAsync(
                        payment.Tenant.Email,
                        payment.Tenant.FirstName,
                        payment.Balance,
                        payment.DueDate,
                        payment.House?.HouseNumber ?? "",
                        payment.House?.Flat?.FlatName ?? "");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send reminder for payment {PaymentId}", payment.Id);
            }
        }

        _logger.LogInformation("Sent {Count} payment reminders", upcomingPayments.Count);
    }
}
