namespace ShmsBackend.Api.Models.Analytics;

public class FinancialStandingResult
{
    public decimal TotalRentCollected { get; set; }
    public decimal? TotalServiceCharge { get; set; }      // null when scope is Landlord (excluded entirely, never shown)
    public decimal TotalExpenses { get; set; }
    public decimal? TotalRefunds { get; set; }          // PAID refunds only (money that has left) — null when scope is Landlord
    public decimal? TotalPendingRefunds { get; set; }   // refunds still owed to tenants (liability, not yet paid) — null when scope is Landlord
    public decimal? TotalForfeitedAdvances { get; set; } // unused advances forfeited to Management (income) — null when scope is Landlord
    public decimal TotalDeductions { get; set; }
    public decimal TotalManagementFee { get; set; }
    public decimal? TotalRewardsRedeemed { get; set; }    // null when scope is Landlord (Management-only cost)
}
