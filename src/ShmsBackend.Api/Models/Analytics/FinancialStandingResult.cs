namespace ShmsBackend.Api.Models.Analytics;

public class FinancialStandingResult
{
    public decimal TotalRentCollected { get; set; }
    public decimal? TotalServiceCharge { get; set; }      // null when scope is Landlord (excluded entirely, never shown)
    public decimal TotalExpenses { get; set; }
    public decimal TotalRefunds { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalManagementFee { get; set; }
    public decimal? TotalRewardsRedeemed { get; set; }    // null when scope is Landlord (Management-only cost)
}
