namespace ShmsBackend.Api.Services.Reward;

public class RedeemPointsResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? RedemptionReference { get; set; }
    public decimal? KesAmount { get; set; }
    public List<(int month, int year, decimal applied)>? Itemized { get; set; }
}

public interface IRewardService
{
    Task EarnPointsAsync(Guid tenantId, Guid houseId, decimal amountReceived, bool isInitialPayment, Guid relatedPaymentId);
    Task<RedeemPointsResult> RedeemPointsAsync(Guid tenantId, int pointsToRedeem);
}
