using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Api.Services.Payment;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Reward;

public class RewardService : IRewardService
{
    private readonly ShmsDbContext _context;
    private readonly IPaymentDistributionService _distributionService;
    private readonly IEmailService _emailService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<RewardService> _logger;

    public RewardService(
        ShmsDbContext context,
        IPaymentDistributionService distributionService,
        IEmailService emailService,
        INotificationService notificationService,
        ILogger<RewardService> logger)
    {
        _context = context;
        _distributionService = distributionService;
        _emailService = emailService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task EarnPointsAsync(Guid tenantId, Guid houseId, decimal amountReceived, bool isInitialPayment, Guid relatedPaymentId)
    {
        var house = await _context.Houses.Include(h => h.Flat).FirstOrDefaultAsync(h => h.Id == houseId);
        if (house?.Flat == null || !house.Flat.RewardEnabled) return;

        var settings = await _context.RewardSettings.FirstOrDefaultAsync();
        if (settings == null || !settings.IsGlobalEnabled) return;

        var rate = isInitialPayment ? settings.InitialPaymentEarnRate : settings.RegularPaymentEarnRate;
        var points = amountReceived * rate;
        if (points <= 0) return;

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant == null) return;

        tenant.PointsBalance += points;

        _context.RewardTransactions.Add(new RewardTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TransactionType = "Earned",
            Source = isInitialPayment ? "InitialPayment" : "RegularPayment",
            Points = points,
            AmountPaidOrRedeemed = amountReceived,
            BalanceAfter = tenant.PointsBalance,
            TenancyCycle = tenant.TenancyCycle,
            RelatedPaymentId = relatedPaymentId,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        try
        {
            await _emailService.SendPointsEarnedEmailAsync(tenant.Email, tenant.FirstName, points, tenant.PointsBalance, tenant.Id.ToString(), true);
            await _notificationService.SendForcedToUserAsync(tenant.Id.ToString(),
                $"You earned {points} points! Your new balance is {tenant.PointsBalance} points.", "rewards", "PointsEarned", relatedPaymentId.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send points-earned notification to tenant {TenantId}", tenantId);
        }
    }

    public async Task<RedeemPointsResult> RedeemPointsAsync(Guid tenantId, decimal pointsToRedeem)
    {
        var tenant = await _context.Tenants
            .Include(t => t.House)
                .ThenInclude(h => h!.Flat)
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null || tenant.House == null)
            return new RedeemPointsResult { Success = false, Message = "No house assigned." };

        var settings = await _context.RewardSettings.FirstOrDefaultAsync();
        if (settings == null || !settings.IsGlobalEnabled || tenant.House.Flat == null || !tenant.House.Flat.RewardEnabled)
            return new RedeemPointsResult { Success = false, Message = "Rewards redemption is currently unavailable for this property." };

        if (pointsToRedeem <= 0 || pointsToRedeem > tenant.PointsBalance)
            return new RedeemPointsResult { Success = false, Message = "Insufficient points." };

        var kesAmount = pointsToRedeem * settings.RedemptionRate;
        if (kesAmount <= 0)
            return new RedeemPointsResult { Success = false, Message = "Insufficient points." };

        var redemptionReference = await GenerateRedemptionReferenceAsync();

        var itemized = await _distributionService.DistributePaymentAsync(
            tenantId, tenant.House.Id, kesAmount, tenant.TenancyCycle,
            redemptionReference: redemptionReference);

        tenant.PointsBalance -= pointsToRedeem;

        Guid? relatedPaymentId = null;
        if (itemized.Count > 0)
        {
            var (month, year, _) = itemized[0];
            relatedPaymentId = await _context.Payments
                .Where(p => p.TenantId == tenantId && p.HouseId == tenant.House.Id
                         && p.Month == month && p.Year == year && !p.IsDeleted)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefaultAsync();
        }

        _context.RewardTransactions.Add(new RewardTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TransactionType = "Redeemed",
            Source = "Redemption",
            Points = pointsToRedeem,
            AmountPaidOrRedeemed = kesAmount,
            BalanceAfter = tenant.PointsBalance,
            TenancyCycle = tenant.TenancyCycle,
            RelatedPaymentId = relatedPaymentId,
            RedemptionReference = redemptionReference,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        try
        {
            await _emailService.SendPointsRedeemedEmailAsync(tenant.Email, tenant.FirstName, pointsToRedeem, kesAmount, redemptionReference, tenant.PointsBalance, tenant.Id.ToString(), true);
            await _notificationService.SendForcedToUserAsync(tenant.Id.ToString(),
                $"You redeemed {pointsToRedeem} points for KES {kesAmount:N2}. Reference: {redemptionReference}.", "rewards", "PointsRedeemed", redemptionReference);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send points-redeemed notification to tenant {TenantId}", tenantId);
        }

        return new RedeemPointsResult
        {
            Success = true,
            Message = "Points redeemed successfully.",
            RedemptionReference = redemptionReference,
            KesAmount = kesAmount,
            Itemized = itemized
        };
    }

    private async Task<string> GenerateRedemptionReferenceAsync()
    {
        var today = DateTime.UtcNow.Date;
        var countToday = await _context.RewardTransactions
            .CountAsync(t => t.Source == "Redemption" && t.CreatedAt >= today && t.CreatedAt < today.AddDays(1));
        var sequence = (countToday + 1).ToString("D6");
        return $"RDM-{today:yyyyMMdd}-{sequence}";
    }
}
