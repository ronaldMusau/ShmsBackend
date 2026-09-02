using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Agreements;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Models.Enums;
using PaymentRecord = ShmsBackend.Data.Models.Entities.Portal.Payment;

namespace ShmsBackend.Api.Services.Payment;

public interface IPaymentService
{
    Task<PaymentRecord> CreateInitialPaymentAsync(Guid tenantId, Guid houseId, string phoneNumber);
    Task<STKPushResponse> InitiatePaymentAsync(Guid paymentId, string phoneNumber, decimal? overrideAmount = null);
    Task ProcessCallbackAsync(MpesaCallback callback);
    Task<STKQueryResponse> QueryPaymentStatusAsync(string checkoutRequestId);
    Task<List<PaymentRecord>> GetTenantPaymentHistoryAsync(Guid tenantId);
    Task<List<PaymentRecord>> GetHousePaymentHistoryAsync(Guid houseId);
    Task<PaymentRecord?> GetCurrentMonthPaymentAsync(Guid tenantId);
    Task<decimal> GetServiceChargeAsync(decimal rentAmount);
    Task GenerateMonthlyPaymentsAsync();
    Task CheckOverduePaymentsAsync();
    Task<VacateSettlementResult> CalculateVacateSettlementAsync(Guid vacateRequestId);
}

public class PaymentService : IPaymentService
{
    private readonly ShmsDbContext _context;
    private readonly IMpesaService _mpesaService;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly IFrontendUrlService _frontendUrlService;
    private readonly IAgreementService _agreementService;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        ShmsDbContext context,
        IMpesaService mpesaService,
        IEmailService emailService,
        INotificationService notificationService,
        IFrontendUrlService frontendUrlService,
        IAgreementService agreementService,
        ILogger<PaymentService> logger)
    {
        _context = context;
        _mpesaService = mpesaService;
        _emailService = emailService;
        _notificationService = notificationService;
        _frontendUrlService = frontendUrlService;
        _agreementService = agreementService;
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

        var existingInitial = await _context.Payments
            .Where(p => p.TenantId == tenantId
                && p.HouseId == houseId
                && p.PaymentType == PaymentType.InitialPayment
                && p.PaymentStatus != PaymentTransactionStatus.Paid
                && !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (existingInitial != null)
            return existingInitial;

        var serviceCharge = await GetServiceChargeAsync(house.RentFee);
        var totalAmount = house.DepositFee + house.RentFee;
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
            TenancyCycle = tenant.TenancyCycle,
            Description = "Initial payment: Deposit + Rent",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Payments.AddAsync(payment);
        await _context.SaveChangesAsync();
        return payment;
    }

    public async Task<STKPushResponse> InitiatePaymentAsync(Guid paymentId, string phoneNumber, decimal? overrideAmount = null)
    {
        var payment = await _context.Payments
            .Include(p => p.House)
            .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(p => p.Id == paymentId)
            ?? throw new Exception("Payment not found");

        var house = payment.House!;
        var flat = house.Flat!;

        payment.PhoneNumber = phoneNumber;

        var reference = $"PAY-{paymentId.ToString().Substring(0, 8).ToUpper()}";
        var description = payment.IsInitialPayment
            ? $"Deposit+Rent {flat.FlatName}"
            : $"Rent {house.HouseNumber}";

        var stkResponse = await _mpesaService.InitiateSTKPushAsync(new STKPushRequest
        {
            PhoneNumber = phoneNumber,
            Amount = overrideAmount ?? ((payment.Balance > 0 ? payment.Balance : payment.Amount) + (payment.ServiceChargeAmount ?? 0m)),
            AccountReference = reference,
            TransactionDesc = description,
            TenantId = payment.TenantId.ToString(),
            HouseId = payment.HouseId.ToString(),
            PaymentId = paymentId.ToString()
        });

        payment.CheckoutRequestId = stkResponse.CheckoutRequestID;
        payment.MerchantRequestId = stkResponse.MerchantRequestID;
        payment.UpdatedAt = DateTime.UtcNow;

        _context.PaymentCheckoutAttempts.Add(new PaymentCheckoutAttempt
        {
            PaymentId = payment.Id,
            CheckoutRequestId = stkResponse.CheckoutRequestID,
            AttemptStatus = "Processing"
        });

        await _context.SaveChangesAsync();

        return stkResponse;
    }

    public async Task ProcessCallbackAsync(MpesaCallback callback)
    {
        var details = _mpesaService.ExtractPaymentDetails(callback);
        var checkoutRequestId = callback.Body.stkCallback.CheckoutRequestID;

        _logger.LogInformation("Processing M-Pesa callback for {CheckoutRequestId}, ResultCode: {ResultCode}",
            checkoutRequestId, details.ResultCode);

        var checkoutAttempt = await _context.PaymentCheckoutAttempts
            .FirstOrDefaultAsync(a => a.CheckoutRequestId == checkoutRequestId);

        if (checkoutAttempt != null && checkoutAttempt.ProcessedAt != null)
        {
            _logger.LogWarning("Duplicate callback received for already-processed CheckoutRequestId: {CheckoutRequestId} — ignoring.", checkoutRequestId);
            return;
        }

        // ── Vacate Settlement Branch ─────────────────────────────────────────────
        if (checkoutAttempt == null)
        {
            var vacateAttempt = await _context.VacateCheckoutAttempts
                .Include(a => a.VacateSettlement)
                .FirstOrDefaultAsync(a => a.CheckoutRequestId == checkoutRequestId);

            if (vacateAttempt != null)
            {
                if (vacateAttempt.ProcessedAt != null)
                {
                    _logger.LogWarning("Duplicate callback received for already-processed vacate CheckoutRequestId: {CheckoutRequestId} — ignoring.", checkoutRequestId);
                    return;
                }

                vacateAttempt.ProcessedAt = DateTime.UtcNow;
                vacateAttempt.ResultCode = details.ResultCode.ToString();
                vacateAttempt.ResultDesc = details.ResultDescription;

                var settlement = vacateAttempt.VacateSettlement!;

                if (details.IsSuccess)
                {
                    vacateAttempt.AttemptStatus = "Success";
                    settlement.PaidAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    var vacateTenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == settlement.TenantId);
                    var vacateHouse = await _context.Houses.FirstOrDefaultAsync(h => h.Id == settlement.HouseId);
                    var vacateHouseNumber = vacateHouse?.HouseNumber ?? "";

                    if (vacateTenant != null)
                    {
                        try { await _notificationService.SendToUserAsync(settlement.TenantId.ToString(), $"Your vacate settlement payment for house {vacateHouseNumber} has been received.", "property"); }
                        catch (Exception ex) { _logger.LogError(ex, "Failed to notify tenant of vacate settlement payment"); }

                        try { await _emailService.SendVacateSettlementPaidTenantEmailAsync(vacateTenant.Email, vacateTenant.FirstName, vacateHouseNumber, vacateTenant.Id.ToString(), true); }
                        catch (Exception ex) { _logger.LogError(ex, "Failed to send vacate settlement paid email to tenant"); }
                    }

                    try
                    {
                        await _notificationService.SendToRolesAsync(
                            new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                            $"Vacate settlement payment received for house {vacateHouseNumber}.",
                            "property");
                    }
                    catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of vacate settlement payment"); }
                }
                else
                {
                    var mappedDesc = details.ResultCode switch
                    {
                        1 or 1001 => "Insufficient M-Pesa balance",
                        1002      => "M-Pesa transaction limit exceeded",
                        2001      => "Wrong M-Pesa PIN entered",
                        1037      => "Payment timed out — phone not reached",
                        _         => !string.IsNullOrWhiteSpace(details.ResultDescription)
                                         ? details.ResultDescription
                                         : "Payment failed"
                    };
                    vacateAttempt.AttemptStatus = "Failed";
                    vacateAttempt.ResultDesc = mappedDesc;
                    vacateAttempt.RetryCount++;
                    await _context.SaveChangesAsync();

                    var vacateTenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == settlement.TenantId);
                    if (vacateTenant != null)
                    {
                        try { await _notificationService.SendToUserAsync(settlement.TenantId.ToString(), $"Your vacate settlement payment failed: {mappedDesc}. Please try again from the tenant portal.", "property"); }
                        catch (Exception ex) { _logger.LogError(ex, "Failed to notify tenant of vacate settlement payment failure"); }
                    }
                }

                return;
            }
        }

        var payment = checkoutAttempt != null
            ? await _context.Payments
                .Include(p => p.Tenant)
                .Include(p => p.House)
                .ThenInclude(h => h!.Flat)
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.Id == checkoutAttempt.PaymentId)
            : await _context.Payments
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

        var isSupersededAttempt = payment.CheckoutRequestId != checkoutRequestId;
        if (isSupersededAttempt && details.IsSuccess)
        {
            _logger.LogWarning(
                "Late SUCCESS callback for superseded CheckoutRequestId {CheckoutRequestId} on Payment {PaymentId} — payment already completed via a newer attempt. Tenant may have been charged twice on M-Pesa. Flagging for manual reconciliation, no money applied.",
                checkoutRequestId, payment.Id);

            try
            {
                await _notificationService.SendToRolesAsync(
                    new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Accountant },
                    $"Possible duplicate M-Pesa charge detected for {payment.Tenant?.FirstName} {payment.Tenant?.LastName} (House {payment.House?.HouseNumber}) — a late successful payment callback arrived for an already-completed payment. Please verify with the tenant and reconcile manually. CheckoutRequestId: {checkoutRequestId}",
                    "payment");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send duplicate-charge reconciliation notification");
            }

            if (checkoutAttempt != null)
            {
                checkoutAttempt.ProcessedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            return;
        }

        if (details.IsSuccess && details.Amount.HasValue)
        {
            payment.MpesaResultCode = details.ResultCode.ToString();
            payment.MpesaResultDesc = details.ResultDescription;
            payment.UpdatedAt = DateTime.UtcNow;

            List<(int month, int year, decimal applied)>? itemizedBreakdown = null;
            var netCollected = details.Amount.Value - (payment.ServiceChargeAmount ?? 0m);
            var priorAmountPaid = payment.AmountPaid;
            var capacity = Math.Max(0, payment.Amount - priorAmountPaid);
            var appliedToThisRow = Math.Min(netCollected, capacity);

            payment.AmountPaid = priorAmountPaid + appliedToThisRow;
            payment.Balance = Math.Max(0, payment.Amount - payment.AmountPaid);
            payment.MpesaReceiptNumber = details.MpesaReceiptNumber;
            payment.PaidAt = DateTime.UtcNow;

            if (appliedToThisRow > 0)
            {
                _context.PaymentApplications.Add(new PaymentApplication
                {
                    PaymentId = payment.Id,
                    MpesaReceiptNumber = details.MpesaReceiptNumber ?? string.Empty,
                    AmountApplied = appliedToThisRow,
                    CheckoutRequestId = checkoutRequestId,
                    AppliedAt = DateTime.UtcNow
                });
            }

            if (payment.Balance <= 0)
            {
                payment.PaymentStatus = PaymentTransactionStatus.Paid;
                var house = payment.House!;
                house.PaymentStatus = PaymentStatus.Paid;
                house.OccupancyStatus = OccupancyStatus.Occupied;
                house.UpdatedAt = DateTime.UtcNow;

                if (payment.IsInitialPayment)
                {
                    var tenant = await _context.PortalUsers
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(u => u.Id == payment.TenantId) as Tenant;

                    if (tenant != null)
                    {
                        tenant.HasCompletedInitialPayment = true;
                        tenant.TenantStatus = TenantStatus.Pending;

                        if (tenant.HouseId.HasValue)
                        {
                            try
                            {
                                var historyHouse = await _context.Houses
                                    .Include(h => h.Flat)
                                    .FirstOrDefaultAsync(h => h.Id == tenant.HouseId.Value);
                                if (historyHouse != null)
                                {
                                    await _context.TenantHouseHistories.AddAsync(new TenantHouseHistory
                                    {
                                        Id = Guid.NewGuid(),
                                        HouseId = historyHouse.Id,
                                        TenantId = tenant.Id,
                                        TenantFirstName = tenant.FirstName,
                                        TenantLastName = tenant.LastName,
                                        TenantEmail = tenant.Email,
                                        TenantPhone = tenant.PhoneNumber,
                                        HouseNumber = historyHouse.HouseNumber,
                                        FlatName = historyHouse.Flat?.FlatName ?? "",
                                        AssignedAt = DateTime.UtcNow,
                                        RemovedAt = null,
                                        TenancyCycle = tenant.TenancyCycle
                                    });
                                }
                            }
                            catch (Exception ex) { _logger.LogError(ex, "Failed to write TenantHouseHistory for tenant {TenantId}", tenant.Id); }
                        }

                        // TemporaryInitialPassword is intentionally NOT cleared here — it stays intact until
                        // the tenant actually verifies (see PortalAuthService.VerifyEmailAsync), so a resend
                        // remains possible indefinitely if this send fails or the tenant never clicks the link.
                        string? verificationLink = null;
                        var tempPassword = tenant.TemporaryInitialPassword;
                        if (!string.IsNullOrEmpty(tenant.EmailVerificationToken) &&
                            !string.IsNullOrEmpty(tempPassword))
                        {
                            verificationLink = _frontendUrlService.GetPortalEmailVerificationUrl(
                                tenant.EmailVerificationToken, tenant.Email, PortalUserType.Tenant);
                        }

                        if (verificationLink != null)
                        {
                            var emailSent = false;
                            for (var attempt = 1; attempt <= 3 && !emailSent; attempt++)
                            {
                                try
                                {
                                    await _emailService.SendPortalVerifyWithPasswordEmailAsync(
                                        tenant.Email, tenant.FirstName, verificationLink, tempPassword!);
                                    emailSent = true;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Failed to send verification email to tenant {Email} (attempt {Attempt}/3)", tenant.Email, attempt);
                                    if (attempt < 3) await Task.Delay(2000);
                                }
                            }

                            if (emailSent)
                            {
                                tenant.VerificationEmailSentAt = DateTime.UtcNow;
                            }
                        }
                        await _context.SaveChangesAsync();

                        // First successful payment → send the signable agreement.
                        try { await _agreementService.SendAgreementForSigningAsync(tenant.Id, (int)PortalUserType.Tenant); }
                        catch (Exception ex) { _logger.LogError(ex, "Failed to send agreement for signing to tenant {TenantId}", tenant.Id); }
                    }
                }

                var distributionBase = payment.RequestedDistributionAmount ?? payment.AmountPaid;
                var overpayment = distributionBase - capacity;
                if (overpayment > 0)
                {
                    itemizedBreakdown = await DistributePaymentAsync(payment.TenantId, payment.HouseId,
                        overpayment, payment.TenancyCycle, details.MpesaReceiptNumber, checkoutRequestId, payment.Id);
                }
            }
            else
            {
                payment.PaymentStatus = PaymentTransactionStatus.PartiallyPaid;
                var house = payment.House!;
                house.PaymentStatus = PaymentStatus.PartiallyPaid;
                house.UpdatedAt = DateTime.UtcNow;
            }

            if (checkoutAttempt != null)
                checkoutAttempt.AttemptStatus = "Success";

            if (payment.Tenant != null && !string.IsNullOrEmpty(details.MpesaReceiptNumber))
            {
                try
                {
                    if (itemizedBreakdown != null && itemizedBreakdown.Count > 0)
                        await _emailService.SendItemizedPaymentReceiptEmailAsync(
                            payment.Tenant.Email,
                            payment.Tenant.FirstName,
                            details.MpesaReceiptNumber,
                            details.Amount.Value,
                            itemizedBreakdown,
                            payment.House!.HouseNumber,
                            payment.House.Flat?.FlatName ?? "",
                            DateTime.UtcNow,
                            payment.Tenant.Id.ToString(), true);
                    else
                        await _emailService.SendPaymentReceiptEmailAsync(
                            payment.Tenant.Email,
                            payment.Tenant.FirstName,
                            details.MpesaReceiptNumber,
                            details.Amount.Value,
                            payment.House!.HouseNumber,
                            payment.House.Flat?.FlatName ?? "",
                            DateTime.UtcNow,
                            payment.Tenant.Id.ToString(), true);
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
            if (checkoutAttempt != null)
            {
                checkoutAttempt.AttemptStatus = "Cancelled";
                checkoutAttempt.ResultCode = details.ResultCode.ToString();
                checkoutAttempt.ResultDesc = details.ResultDescription;
                checkoutAttempt.RetryCount++;
            }
        }
        else
        {
            if (checkoutAttempt != null)
            {
                var mappedDesc = details.ResultCode switch
                {
                    1 or 1001 => "Insufficient M-Pesa balance",
                    1002 => "M-Pesa transaction limit exceeded",
                    2001 => "Wrong M-Pesa PIN entered",
                    1037 => "Payment timed out — phone not reached",
                    _ => !string.IsNullOrWhiteSpace(details.ResultDescription) ? details.ResultDescription : "Payment failed"
                };
                checkoutAttempt.AttemptStatus = "Failed";
                checkoutAttempt.ResultCode = details.ResultCode.ToString();
                checkoutAttempt.ResultDesc = mappedDesc;
                checkoutAttempt.RetryCount++;
            }
        }

        if (checkoutAttempt != null)
        {
            checkoutAttempt.ProcessedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }

    private async Task<List<(int month, int year, decimal applied)>> DistributePaymentAsync(
        Guid tenantId, Guid houseId, decimal excessAmount, int tenancyCycle,
        string? receiptNumber = null, string? checkoutRequestId = null, Guid? excludePaymentId = null)
    {
        var remaining = excessAmount;
        var itemized = new List<(int month, int year, decimal applied)>();
        var currentDate = DateTime.UtcNow;

        var existingUnpaid = await _context.Payments
            .Where(p => p.TenantId == tenantId && p.HouseId == houseId
                     && p.TenancyCycle == tenancyCycle
                     && p.Balance > 0 && !p.IsInitialPayment && !p.IsDeleted
                     && p.Id != excludePaymentId)
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToListAsync();

        var house = await _context.Houses
            .Include(h => h.Flat)
            .FirstOrDefaultAsync(h => h.Id == houseId)
            ?? throw new Exception($"House {houseId} not found during payment distribution");

        foreach (var row in existingUnpaid)
        {
            if (remaining <= 0) break;
            var applyAmount = Math.Min(remaining, row.Balance);
            row.AmountPaid += applyAmount;
            row.Balance = Math.Max(0, row.Amount - row.AmountPaid);
            row.PaymentStatus = row.Balance <= 0
                ? PaymentTransactionStatus.Paid
                : PaymentTransactionStatus.PartiallyPaid;
            if (!string.IsNullOrEmpty(receiptNumber) && applyAmount > 0)
            {
                row.MpesaReceiptNumber = receiptNumber;
                row.PaidAt = DateTime.UtcNow;
                _context.PaymentApplications.Add(new PaymentApplication
                {
                    PaymentId = row.Id,
                    MpesaReceiptNumber = receiptNumber,
                    AmountApplied = applyAmount,
                    CheckoutRequestId = checkoutRequestId,
                    AppliedAt = DateTime.UtcNow
                });
            }
            remaining -= applyAmount;
            itemized.Add((row.Month, row.Year, applyAmount));
        }

        var serviceCharge = await GetServiceChargeAsync(house.RentFee);
        var cursorMonth = currentDate.Month;
        var cursorYear = currentDate.Year;
        var totalIterations = 0;

        while (remaining > 0)
        {
            if (++totalIterations > 60) break;
            if (itemized.Count > 36) break;

            var existingMonthRow = await _context.Payments.FirstOrDefaultAsync(p =>
                p.TenantId == tenantId && p.HouseId == houseId
                && p.TenancyCycle == tenancyCycle
                && p.Month == cursorMonth && p.Year == cursorYear && !p.IsDeleted);

            if (existingMonthRow != null && existingMonthRow.Balance > 0)
            {
                // Existing unpaid row — apply funds to it rather than creating a duplicate
                var applyAmount = Math.Min(remaining, existingMonthRow.Balance);
                existingMonthRow.AmountPaid += applyAmount;
                existingMonthRow.Balance = Math.Max(0, existingMonthRow.Amount - existingMonthRow.AmountPaid);
                existingMonthRow.PaymentStatus = existingMonthRow.Balance <= 0
                    ? PaymentTransactionStatus.Paid
                    : PaymentTransactionStatus.PartiallyPaid;
                if (!string.IsNullOrEmpty(receiptNumber) && applyAmount > 0)
                {
                    existingMonthRow.MpesaReceiptNumber = receiptNumber;
                    existingMonthRow.PaidAt = DateTime.UtcNow;
                    _context.PaymentApplications.Add(new PaymentApplication
                    {
                        PaymentId = existingMonthRow.Id,
                        MpesaReceiptNumber = receiptNumber,
                        AmountApplied = applyAmount,
                        CheckoutRequestId = checkoutRequestId,
                        AppliedAt = DateTime.UtcNow
                    });
                }
                remaining -= applyAmount;
                itemized.Add((cursorMonth, cursorYear, applyAmount));
            }
            else if (existingMonthRow == null)
            {
                // No row yet — create one
                var monthlyTotal = house.RentFee;
                var applyAmount = Math.Min(remaining, monthlyTotal);
                var newPayment = new PaymentRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    HouseId = houseId,
                    FlatId = house.FlatId,
                    LandlordId = house.Flat?.LandlordId,
                    TenancyCycle = tenancyCycle,
                    Amount = monthlyTotal,
                    AmountPaid = applyAmount,
                    Balance = monthlyTotal - applyAmount,
                    RentAmount = house.RentFee,
                    ServiceChargeAmount = serviceCharge,
                    Month = cursorMonth,
                    Year = cursorYear,
                    IsInitialPayment = false,
                    PaymentType = PaymentType.Rent,
                    DueDate = new DateTime(cursorYear, cursorMonth, Math.Min(house.Flat!.RentDueDay, DateTime.DaysInMonth(cursorYear, cursorMonth))),
                    PaymentStatus = applyAmount >= monthlyTotal
                        ? PaymentTransactionStatus.Paid
                        : applyAmount > 0
                            ? PaymentTransactionStatus.PartiallyPaid
                            : PaymentTransactionStatus.Pending,
                    Description = $"Monthly rent - {new DateTime(cursorYear, cursorMonth, 1):MMMM yyyy}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Payments.Add(newPayment);
                if (!string.IsNullOrEmpty(receiptNumber) && applyAmount > 0)
                {
                    newPayment.MpesaReceiptNumber = receiptNumber;
                    newPayment.PaidAt = DateTime.UtcNow;
                    _context.PaymentApplications.Add(new PaymentApplication
                    {
                        PaymentId = newPayment.Id,
                        MpesaReceiptNumber = receiptNumber,
                        AmountApplied = applyAmount,
                        CheckoutRequestId = checkoutRequestId,
                        AppliedAt = DateTime.UtcNow
                    });
                }
                remaining -= applyAmount;
                itemized.Add((cursorMonth, cursorYear, applyAmount));
            }
            // else: existingMonthRow.Balance <= 0 — month fully settled, advance cursor only

            cursorMonth++;
            if (cursorMonth > 12) { cursorMonth = 1; cursorYear++; }
        }

        var currentMonthPayment = await _context.Payments
            .Where(p => p.TenantId == tenantId && p.HouseId == houseId && p.TenancyCycle == tenancyCycle
                && p.Month == currentDate.Month && p.Year == currentDate.Year
                && !p.IsInitialPayment && !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (currentMonthPayment != null)
        {
            house.PaymentStatus = currentMonthPayment.Balance <= 0
                ? PaymentStatus.Paid
                : (currentMonthPayment.DueDate != default && currentMonthPayment.DueDate < currentDate.AddDays(-3)
                    ? PaymentStatus.Overdue
                    : PaymentStatus.PartiallyPaid);
            house.OccupancyStatus = OccupancyStatus.Occupied;
            house.UpdatedAt = currentDate;
        }

        await _context.SaveChangesAsync();
        return itemized;
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

        var duePriceChanges = await _context.PendingRentChanges
            .Where(pc => pc.AppliedAt == null && pc.EffectiveMonth == now.Month && pc.EffectiveYear == now.Year)
            .ToListAsync();
        foreach (var change in duePriceChanges)
        {
            var house = await _context.Houses.FindAsync(change.HouseId);
            if (house != null)
            {
                house.RentFee = change.NewRentFee;
                house.DepositFee = change.NewDepositFee;
            }
            change.AppliedAt = DateTime.UtcNow;
        }
        if (duePriceChanges.Any())
            await _context.SaveChangesAsync();

        var tenants = await _context.Tenants
            .Include(t => t.House)
            .ThenInclude(h => h!.Flat)
            .Where(t => t.HouseId != null && !t.IsDeleted && t.HasCompletedInitialPayment
                && (t.TenantStatus == TenantStatus.Active || t.TenantStatus == TenantStatus.Pending))
            .ToListAsync();

        foreach (var tenant in tenants)
        {
            try
            {
                var exists = await _context.Payments.AnyAsync(p =>
                    p.TenantId == tenant.Id &&
                    p.Month == now.Month &&
                    p.Year == now.Year &&
                    p.TenancyCycle == tenant.TenancyCycle &&
                    p.HouseId == tenant.HouseId &&
                    !p.IsInitialPayment &&
                    !p.IsDeleted);

                if (exists) continue;

                var activeVacate = await _context.VacateRequests.FirstOrDefaultAsync(r =>
                    r.TenantId == tenant.Id && !r.IsDeleted
                    && r.Status != "Closed" && r.Status != "Cancelled");
                if (activeVacate != null)
                {
                    bool isAfterVacateMonth = activeVacate.VacateYear < now.Year || (activeVacate.VacateYear == now.Year && activeVacate.VacateMonth < now.Month);
                    bool isVacateMonthItself = activeVacate.VacateYear == now.Year && activeVacate.VacateMonth == now.Month;
                    if (isAfterVacateMonth) continue;
                    if (isVacateMonthItself && activeVacate.SitDeposit) continue;
                }

                if (tenant.LeaseStartMonth.HasValue && tenant.LeaseStartYear.HasValue)
                {
                    bool isBeforeLeaseStart = now.Year < tenant.LeaseStartYear.Value
                        || (now.Year == tenant.LeaseStartYear.Value && now.Month < tenant.LeaseStartMonth.Value);
                    if (isBeforeLeaseStart) continue;
                }

                var house = tenant.House!;
                var flat = house.Flat!;
                var serviceCharge = await GetServiceChargeAsync(house.RentFee);
                var rentDueDay = Math.Min(flat.RentDueDay, DateTime.DaysInMonth(now.Year, now.Month));
                var dueDate = new DateTime(now.Year, now.Month, rentDueDay);

                var totalDue = house.RentFee;
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
                    PaymentStatus = creditApplied >= totalDue
                        ? PaymentTransactionStatus.Paid
                        : creditApplied > 0
                            ? PaymentTransactionStatus.PartiallyPaid
                            : PaymentTransactionStatus.Pending,
                    PaymentType = PaymentType.Rent,
                    PhoneNumber = tenant.PhoneNumber,
                    DueDate = dueDate,
                    Month = now.Month,
                    Year = now.Year,
                    TenancyCycle = tenant.TenancyCycle,
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
        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant == null) return 0;

        var totalPaid = await _context.Payments
            .Where(p => p.TenantId == tenantId && p.TenancyCycle == tenant.TenancyCycle && !p.IsDeleted)
            .SumAsync(p => p.AmountPaid);

        var totalDue = await _context.Payments
            .Where(p => p.TenantId == tenantId
                && p.TenancyCycle == tenant.TenancyCycle
                && !p.IsDeleted
                && p.PaymentStatus != PaymentTransactionStatus.Cancelled
                && p.PaymentStatus != PaymentTransactionStatus.Failed)
            .SumAsync(p => p.Amount);

        return Math.Max(0, totalPaid - totalDue);
    }

    public async Task CheckOverduePaymentsAsync()
    {
        var overdueThreshold = DateTime.UtcNow.AddDays(-3);
        _logger.LogInformation("Checking overdue payments, threshold: {Threshold}", overdueThreshold);

        var overduePayments = (await _context.Payments
            .Include(p => p.Tenant)
            .Include(p => p.House)
            .ThenInclude(h => h!.Flat)
            .Where(p => (p.PaymentStatus == PaymentTransactionStatus.Pending || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid) &&
                        p.DueDate < overdueThreshold &&
                        !p.IsDeleted)
            .ToListAsync())
            .Where(p => p.Tenant != null && p.TenancyCycle == p.Tenant.TenancyCycle)
            .ToList();

        var groups = overduePayments
            .GroupBy(p => new { p.TenantId, p.HouseId })
            .ToList();

        foreach (var group in groups)
        {
            var orderedRows = group.OrderBy(p => p.Year).ThenBy(p => p.Month).ToList();

            foreach (var payment in orderedRows)
            {
                payment.PaymentStatus = PaymentTransactionStatus.Overdue;
                payment.UpdatedAt = DateTime.UtcNow;
            }

            var breakdown = orderedRows
                .Select(p => (MonthLabel: new DateTime(p.Year, p.Month, 1).ToString("MMMM yyyy"), Balance: p.Balance))
                .ToList();
            var totalArrears = orderedRows.Sum(p => p.Balance);

            var firstRow = orderedRows.First();
            var house = firstRow.House;
            if (house != null)
            {
                house.PaymentStatus = PaymentStatus.Overdue;
                house.UpdatedAt = DateTime.UtcNow;
            }

            var tenant = firstRow.Tenant;
            if (tenant != null)
            {
                try
                {
                    await _emailService.SendPaymentOverdueEmailAsync(
                        tenant.Email,
                        tenant.FirstName,
                        breakdown,
                        totalArrears,
                        house?.HouseNumber ?? "",
                        house?.Flat?.FlatName ?? "",
                        tenant.Id.ToString(), true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send overdue email for tenant {TenantId}", tenant.Id);
                }

                if (house?.Flat?.LandlordId != null)
                {
                    try
                    {
                        await _notificationService.SendToUserAsync(
                            house.Flat.LandlordId.ToString(),
                            $"Rent for House {house.HouseNumber} has KES {totalArrears:N2} in overdue arrears across {orderedRows.Count} month(s).",
                            "payment");
                    }
                    catch { }
                }
            }
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Marked {Count} payments as overdue", overduePayments.Count);
    }

    public async Task<VacateSettlementResult> CalculateVacateSettlementAsync(Guid vacateRequestId)
    {
        var vacateRequest = await _context.VacateRequests
            .Include(v => v.InspectionLines)
            .FirstOrDefaultAsync(v => v.Id == vacateRequestId);
        if (vacateRequest == null) throw new InvalidOperationException("Vacate request not found.");

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == vacateRequest.TenantId);
        if (tenant == null) throw new InvalidOperationException("Tenant not found.");

        var house = await _context.Houses.FirstOrDefaultAsync(h => h.Id == vacateRequest.HouseId);
        var depositFee = house?.DepositFee ?? 0m;

        var totalDamages = vacateRequest.InspectionLines.Sum(l => l.AssessedAmount ?? 0m);
        var totalOwed = totalDamages;

        var advanceBalance = await GetRemainingCreditAsync(tenant.Id);
        var advanceAppliedToDamages = Math.Min(advanceBalance, totalOwed);
        var advanceForfeitedUnused = advanceBalance - advanceAppliedToDamages;
        var remainingAfterAdvance = Math.Max(0, totalOwed - advanceBalance);

        decimal depositApplied;
        decimal remainingAfterDeposit;
        decimal depositRefund = 0m;

        if (vacateRequest.SitDeposit)
        {
            depositApplied = Math.Min(depositFee, remainingAfterAdvance);
            remainingAfterDeposit = Math.Max(0, remainingAfterAdvance - depositFee);
        }
        else
        {
            depositApplied = Math.Min(depositFee, remainingAfterAdvance);
            remainingAfterDeposit = Math.Max(0, remainingAfterAdvance - depositFee);
            depositRefund = Math.Max(0, depositFee - remainingAfterAdvance);
        }

        if (advanceBalance > 0)
        {
            _context.VacateForfeitedAdvances.Add(new VacateForfeitedAdvance
            {
                VacateRequestId = vacateRequest.Id,
                TenantId = vacateRequest.TenantId,
                HouseId = vacateRequest.HouseId,
                FlatId = vacateRequest.FlatId,
                LandlordId = vacateRequest.LandlordId,
                TotalAdvanceAmount = advanceBalance,
                AmountAppliedToDamages = advanceAppliedToDamages,
                AmountForfeitedUnused = advanceForfeitedUnused
            });
        }

        string direction;
        decimal settlementAmount;
        string description;

        if (remainingAfterDeposit > 0)
        {
            direction = "TenantOwes";
            settlementAmount = remainingAfterDeposit;
            description = $"Vacate settlement: damages of {totalOwed:N2} exceeded advance credit and deposit.";
        }
        else if (depositRefund > 0)
        {
            direction = "ManagementOwes";
            settlementAmount = depositRefund;
            description = $"Vacate settlement: deposit refund after deducting {totalOwed:N2} in damages.";
        }
        else
        {
            direction = "Settled";
            settlementAmount = 0m;
            description = "Vacate settlement: fully covered by advance credit and deposit, no balance either way.";
        }

        _context.VacateSettlements.Add(new VacateSettlement
        {
            VacateRequestId = vacateRequest.Id,
            LandlordId = vacateRequest.LandlordId,
            TenantId = vacateRequest.TenantId,
            HouseId = vacateRequest.HouseId,
            FlatId = vacateRequest.FlatId,
            Direction = direction,
            Amount = settlementAmount,
            Description = description
        });

        await _context.SaveChangesAsync();

        return new VacateSettlementResult
        {
            TotalDamages = totalDamages,
            DepositApplied = depositApplied,
            DepositRefunded = depositRefund,
            AdvanceApplied = advanceAppliedToDamages,
            AdvanceForfeited = advanceForfeitedUnused,
            Direction = direction,
            FinalAmount = settlementAmount
        };
    }
}

public class VacateSettlementResult
{
    public decimal TotalDamages { get; set; }
    public decimal DepositApplied { get; set; }
    public decimal DepositRefunded { get; set; }
    public decimal AdvanceApplied { get; set; }
    public decimal AdvanceForfeited { get; set; }
    public string Direction { get; set; } = string.Empty;
    public decimal FinalAmount { get; set; }
}
