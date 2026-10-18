using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Helpers;

/// <summary>
/// Platform service charge for payments that are not monthly rent (complaint charges, vacate settlements).
/// Same band table (ServiceChargeSettings) rent payments use, keyed on the tenant's house rent so a tenant
/// pays the same flat charge on every transaction. The charge is ADDED on top of the amount owed and is
/// never exposed to tenants, landlords or agents.
/// </summary>
public static class ServiceChargeLookup
{
    public static async Task<decimal> ForHouseAsync(ShmsDbContext ctx, Guid houseId)
    {
        var rent = await ctx.Houses.Where(h => h.Id == houseId).Select(h => (decimal?)h.RentFee).FirstOrDefaultAsync();
        if (rent == null) return 0m;
        return await ForRentAsync(ctx, rent.Value);
    }

    public static async Task<decimal> ForRentAsync(ShmsDbContext ctx, decimal rent)
    {
        var setting = await ctx.ServiceChargeSettings
            .Where(s => s.IsActive && !s.IsDeleted && s.MinRent <= rent && s.MaxRent >= rent)
            .OrderBy(s => s.MinRent)
            .FirstOrDefaultAsync();
        return setting?.ServiceCharge ?? 0m;
    }
}
