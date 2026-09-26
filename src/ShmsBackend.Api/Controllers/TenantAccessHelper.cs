using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

public static class TenantAccessHelper
{
    // A pre-registered tenant can log in and view their own data, but stays read-only until their
    // expected move-in month actually arrives — computed from LeaseStartMonth/LeaseStartYear rather
    // than a stored flag, to avoid drift. Same comparison PaymentService.GenerateMonthlyPaymentsAsync
    // already uses to decide whether a monthly rent payment is due yet.
    public static bool IsReadOnlyUntilMoveIn(Tenant tenant)
    {
        if (!tenant.LeaseStartMonth.HasValue || !tenant.LeaseStartYear.HasValue)
            return false;

        var now = DateTime.UtcNow;
        return now.Year < tenant.LeaseStartYear.Value
            || (now.Year == tenant.LeaseStartYear.Value && now.Month < tenant.LeaseStartMonth.Value);
    }
}
