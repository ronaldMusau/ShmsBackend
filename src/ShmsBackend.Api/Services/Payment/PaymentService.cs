using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using PaymentRecord = ShmsBackend.Data.Models.Entities.Portal.Payment;

namespace ShmsBackend.Api.Services.Payment;

public interface IPaymentService
{
    Task<PaymentRecord> CreateInitialPaymentAsync(Guid tenantId, Guid houseId, string phoneNumber);
    Task<STKPushResponse> InitiatePaymentAsync(Guid paymentId, string phoneNumber);
    Task ProcessCallbackAsync(MpesaCallback callback);
    Task<STKQueryResponse> QueryPaymentStatusAsync(string checkoutRequestId);
    Task<List<PaymentRecord>> GetTenantPaymentHistoryAsync(Guid tenantId);
    Task<List<PaymentRecord>> GetHousePaymentHistoryAsync(Guid houseId);
    Task<PaymentRecord?> GetCurrentMonthPaymentAsync(Guid tenantId);
    Task<decimal> GetServiceChargeAsync(decimal rentAmount);
    Task GenerateMonthlyPaymentsAsync();
    Task CheckOverduePaymentsAsync();
}

public class PaymentService : IPaymentService
{
    private readonly ShmsDbContext _context;
    private readonly IMpesaService _mpesaService;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        ShmsDbContext context,
        IMpesaService mpesaService,
        IEmailService emailService,
        INotificationService notificationService,
        ILogger<PaymentService> logger)
    {
        _context = context;
        _mpesaService = mpesaService;
        _emailService = emailService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<decimal> GetServiceChargeAsync(decimal rentAmount)
    {
        var setting = await _context.ServiceChargeSettings
            .Where(s => s.IsActive && !s.IsDeleted &&
                        s.MinRent <= rentAmount && s.MaxRent >= rentAmount)
            .OrderBy(s => s.MinRent)
            .FirstOrDefaultAsync();
        return setting?.ServiceCharge ?? 0;
    }

    public async Task<PaymentRecord> CreateInitialPaymentAsync(Guid tenantId, Guid houseId, string phoneNumber)
    {
        var tenant = await _context.Tenants
            .Include(t => t.House)
            .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(t => t.Id == tenantId)
            ?? throw new Exception("Tenant not found");

        var house = await _context.Houses
            .Include(h => h.Flat)
            .FirstOrDefaultAsync(h => h.Id == houseId)
            ?? throw new Exception("House not found");

        var flat = house.Flat ?? throw new Exception("Flat not found");
        var serviceCharge = await GetServiceChargeAsync(house.RentFee);
        var totalAmount = house.DepositFee + house.RentFee + serviceCharge;
        var now = DateTime.UtcNow;
        var dueDate = new DateTime(now.Year, now.Month,
            Math.Min(flat.RentDueDay, DateTime.DaysInMonth(now.Year, now.Month)));

        var payment = new PaymentRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            HouseId = houseId,
            FlatId = flat.Id,
            LandlordId = flat.LandlordId,
            Amount = totalAmount,
            AmountPaid = 0,
            Balance = totalAmount,
            RentAmount = house.RentFee,
            DepositAmount = house.DepositFee,
            ServiceChargeAmount = serviceCharge,
            PaymentStatus = PaymentTransactionStatus.Pending,
            PaymentType = PaymentType.InitialPayment,
            PaymentMethod = PaymentMethod.Mpesa,
            PhoneNumber = phoneNumber,
            DueDate = dueDate,
            Month = now.Month,
            Year = now.Year,
            IsInitialPayment = true,
            Description = "Initial payment: Deposit + Rent + Service Charge",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Payments.AddAsync(payment);
        await _context.SaveChangesAsync();
        return payment;
    }

    public async Task<STKPushResponse> InitiatePaymentAsync(Guid paymentId, string phoneNumber)
    {
        var payment = await _context.Payments
            .Include(p => p.House)
            .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(p => p.Id == paymentId)
            ?? throw new Exception("Payment not found");

        var house = payment.House!;
        var flat = house.Flat!;

        payment.PhoneNumber = phoneNumber;
        payment.PaymentStatus = PaymentTransactionStatus.Processing;
        payment.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var reference = $"PAY-{paymentId.ToString().Substring(0, 8).ToUpper()}";
        var description = payment.IsInitialPayment
            ? $"Deposit+Rent {flat.FlatName}"
            : $"Rent {house.HouseNumber}";

        var stkResponse = await _mpesaService.InitiateSTKPushAsync(new STKPushRequest
        {
            PhoneNumber = phoneNumber,
            Amount = payment.Balance > 0 ? payment.Balance : payment.Amount,
            AccountReference = reference,
            TransactionDesc = description,
            TenantId = payment.TenantId.ToString(),
            HouseId = payment.HouseId.ToString(),
            PaymentId = paymentId.ToString()
        });

        payment.CheckoutRequestId = stkResponse.CheckoutRequestID;
        payment.MerchantRequestId = stkResponse.MerchantRequestID;
        payment.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return stkResponse;
    }

    public async Task ProcessCallbackAsync(MpesaCallback callback)
    {
        var details = _mpesaService.ExtractPaymentDetails(callback);
        var checkoutRequestId = callback.Body.stkCallback.CheckoutRequestID;

        _logger.LogInformation("Processing M-Pesa callback for {CheckoutRequestId}, ResultCode: {ResultCode}",
            checkoutRequestId, details.ResultCode);

        var payment = await _context.Payments
            .Include(p => p.Tenant)
            .Include(p => p.House)
            .ThenInclude(h => h!.Flat)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.CheckoutRequestId == checkoutRequestId);

        if (payment == null)
        {
            _logger.LogWarning("Payment not found for CheckoutRequestId: {CheckoutRequestId}", checkoutRequestId);
            return;
        }

        payment.MpesaResultCode = details.ResultCode.ToString();
        payment.MpesaResultDesc = details.ResultDescription;
        payment.UpdatedAt = DateTime.UtcNow;

        if (details.IsSuccess && details.Amount.HasValue)
        {
            payment.AmountPaid += details.Amount.Value;
            payment.Balance = Math.Max(0, payment.Amount - payment.AmountPaid);
            payment.MpesaReceiptNumber = details.MpesaReceiptNumber;
            payment.PaidAt = DateTime.UtcNow;

            if (payment.Balance <= 0)
            {
                payment.PaymentStatus = PaymentTransactionStatus.Paid;
                var house = payment.House!;
                house.PaymentStatus = PaymentStatus.Paid;
                house.OccupancyStatus = OccupancyStatus.Occupied;
                house.UpdatedAt = DateTime.UtcNow;

                var overpayment = payment.AmountPaid - payment.Amount;
                if (overpayment > 0)
                {
                    await DistributeCreditAsync(payment.TenantId, payment.HouseId,
                        payment.FlatId, overpayment);
                }
            }
            else
            {
                payment.PaymentStatus = PaymentTransactionStatus.PartiallyPaid;
                var house = payment.House!;
                house.PaymentStatus = PaymentStatus.PartiallyPaid;
                house.UpdatedAt = DateTime.UtcNow;
            }

            if (payment.Tenant != null && !string.IsNullOrEmpty(details.MpesaReceiptNumber))
            {
                try
                {
                    await _emailService.SendPaymentReceiptEmailAsync(
                        payment.Tenant.Email,
                        payment.Tenant.FirstName,
                        details.MpesaReceiptNumber,
                        details.Amount.Value,
                        payment.House!.HouseNumber,
                        payment.House.Flat?.FlatName ?? "",
                        DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send receipt email");
                }
            }

            try
            {
                await _notificationService.SendToRolesAsync(
                    new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin,
                            NotificationAudience.Secretary, NotificationAudience.Manager,
                            NotificationAudience.Accountant },
                    $"Payment received: KES {details.Amount:N2} from {payment.Tenant?.FirstName} {payment.Tenant?.LastName} — Receipt {details.MpesaReceiptNumber}",
                    "payment");

                if (payment.LandlordId.HasValue)
                {
                    await _notificationService.SendToUserAsync(
                        payment.LandlordId.Value.ToString(),
                        $"Rent payment of KES {details.Amount:N2} received for House {payment.House?.HouseNumber}. Receipt: {details.MpesaReceiptNumber}",
                        "payment");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send payment notifications");
            }
        }
        else if (details.IsCancelled)
        {
            payment.PaymentStatus = PaymentTransactionStatus.Cancelled;
            payment.RetryCount++;
        }
        else
        {
            payment.PaymentStatus = PaymentTransactionStatus.Failed;
            payment.RetryCount++;
        }

        await _context.SaveChangesAsync();
    }

    private async Task DistributeCreditAsync(Guid tenantId, Guid houseId, Guid flatId, decimal credit)
    {
        var upcomingPayments = await _context.Payments
            .Where(p => p.TenantId == tenantId &&
                        p.HouseId == houseId &&
                        p.PaymentStatus == PaymentTransactionStatus.Pending &&
                        !p.IsInitialPayment)
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToListAsync();

        var remainingCredit = credit;

        foreach (var upcoming in upcomingPayments)
        {
            if (remainingCredit <= 0) break;
            var applied = Math.Min(remainingCredit, upcoming.Balance);
            upcoming.CreditApplied = (upcoming.CreditApplied ?? 0) + applied;
            upcoming.AmountPaid += applied;
            upcoming.Balance = Math.Max(0, upcoming.Amount - upcoming.AmountPaid);
            remainingCredit -= applied;

            if (upcoming.Balance <= 0)
                upcoming.PaymentStatus = PaymentTransactionStatus.Paid;
        }

        if (remainingCredit > 0)
            _logger.LogInformation("Remaining credit {Credit} for tenant {TenantId} will apply to future months",
                remainingCredit, tenantId);

        await _context.SaveChangesAsync();
    }

    public async Task<STKQueryResponse> QueryPaymentStatusAsync(string checkoutRequestId)
    {
        return await _mpesaService.QuerySTKStatusAsync(checkoutRequestId);
    }

    public async Task<List<PaymentRecord>> GetTenantPaymentHistoryAsync(Guid tenantId)
    {
        return await _context.Payments
            .Include(p => p.House)
            .ThenInclude(h => h!.Flat)
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .ToListAsync();
    }

    public async Task<List<PaymentRecord>> GetHousePaymentHistoryAsync(Guid houseId)
    {
        return await _context.Payments
            .Include(p => p.Tenant)
            .Where(p => p.HouseId == houseId)
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .ToListAsync();
    }

    public async Task<PaymentRecord?> GetCurrentMonthPaymentAsync(Guid tenantId)
    {
        var now = DateTime.UtcNow;
        return await _context.Payments
            .Where(p => p.TenantId == tenantId &&
                        p.Month == now.Month &&
                        p.Year == now.Year &&
                        !p.IsInitialPayment)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task GenerateMonthlyPaymentsAsync()
    {
        var now = DateTime.UtcNow;
        _logger.LogInformation("Generating monthly payments for {Month}/{Year}", now.Month, now.Year);

        var tenants = await _context.Tenants
            .Include(t => t.House)
            .ThenInclude(h => h!.Flat)
            .Where(t => t.HouseId != null && t.IsActive && !t.IsDeleted)
            .ToListAsync();

        foreach (var tenant in tenants)
        {
            try
            {
                var exists = await _context.Payments.AnyAsync(p =>
                    p.TenantId == tenant.Id &&
                    p.Month == now.Month &&
                    p.Year == now.Year &&
                    !p.IsInitialPayment);

                if (exists) continue;

                var house = tenant.House!;
                var flat = house.Flat!;
                var serviceCharge = await GetServiceChargeAsync(house.RentFee);
                var rentDueDay = Math.Min(flat.RentDueDay, DateTime.DaysInMonth(now.Year, now.Month));
                var dueDate = new DateTime(now.Year, now.Month, rentDueDay);

                var totalDue = house.RentFee + serviceCharge;
                var creditApplied = 0m;

                var existingCredit = await GetRemainingCreditAsync(tenant.Id);
                if (existingCredit > 0)
                {
                    creditApplied = Math.Min(existingCredit, totalDue);
                }

                var payment = new PaymentRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    HouseId = house.Id,
                    FlatId = flat.Id,
                    LandlordId = flat.LandlordId,
                    Amount = totalDue,
                    AmountPaid = creditApplied,
                    Balance = totalDue - creditApplied,
                    RentAmount = house.RentFee,
                    ServiceChargeAmount = serviceCharge,
                    CreditApplied = creditApplied > 0 ? creditApplied : null,
                    PaymentStatus = creditApplied >= totalDue ? PaymentTransactionStatus.Paid : PaymentTransactionStatus.Pending,
                    PaymentType = PaymentType.Rent,
                    PhoneNumber = tenant.PhoneNumber,
                    DueDate = dueDate,
                    Month = now.Month,
                    Year = now.Year,
                    Description = $"Monthly rent - {now:MMMM yyyy}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _context.Payments.AddAsync(payment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate payment for tenant {TenantId}", tenant.Id);
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task<decimal> GetRemainingCreditAsync(Guid tenantId)
    {
        var totalPaid = await _context.Payments
            .Where(p => p.TenantId == tenantId && p.PaymentStatus == PaymentTransactionStatus.Paid)
            .SumAsync(p => p.AmountPaid);

        var totalDue = await _context.Payments
            .Where(p => p.TenantId == tenantId)
            .SumAsync(p => p.Amount);

        return Math.Max(0, totalPaid - totalDue);
    }

    public async Task CheckOverduePaymentsAsync()
    {
        var overdueThreshold = DateTime.UtcNow.AddDays(-3);
        _logger.LogInformation("Checking overdue payments, threshold: {Threshold}", overdueThreshold);

        var overduePayments = await _context.Payments
            .Include(p => p.Tenant)
            .Include(p => p.House)
            .ThenInclude(h => h!.Flat)
            .Where(p => p.PaymentStatus == PaymentTransactionStatus.Pending &&
                        p.DueDate < overdueThreshold &&
                        !p.IsDeleted)
            .ToListAsync();

        foreach (var payment in overduePayments)
        {
            payment.PaymentStatus = PaymentTransactionStatus.Overdue;
            payment.UpdatedAt = DateTime.UtcNow;

            var house = payment.House;
            if (house != null)
            {
                house.PaymentStatus = PaymentStatus.Overdue;
                house.UpdatedAt = DateTime.UtcNow;
            }

            if (payment.Tenant != null)
            {
                var daysOverdue = (int)(DateTime.UtcNow - payment.DueDate).TotalDays;
                try
                {
                    await _emailService.SendPaymentOverdueEmailAsync(
                        payment.Tenant.Email,
                        payment.Tenant.FirstName,
                        payment.Balance,
                        daysOverdue,
                        payment.House?.HouseNumber ?? "",
                        payment.House?.Flat?.FlatName ?? "");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send overdue email for payment {PaymentId}", payment.Id);
                }

                if (house?.Flat?.LandlordId != null)
                {
                    try
                    {
                        await _notificationService.SendToUserAsync(
                            house.Flat.LandlordId.ToString(),
                            $"Rent for House {house.HouseNumber} is {daysOverdue} day(s) overdue.",
                            "payment");
                    }
                    catch { }
                }
            }
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Marked {Count} payments as overdue", overduePayments.Count);
    }
}
