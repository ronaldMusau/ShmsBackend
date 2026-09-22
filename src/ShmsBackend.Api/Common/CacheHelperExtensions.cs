namespace ShmsBackend.Api.Services.Common;

// Every write site that can stale public-listing data needs to clear both listing caches together —
// this wrapper makes that one call instead of two, so it can't be done half-way at a given site.
public static class CacheHelperExtensions
{
    public static async Task InvalidatePublicListingsCacheAsync(this ICacheHelper cache)
    {
        await cache.RemoveByPrefixAsync("listings:");
        await cache.RemoveByPrefixAsync("listing-detail:");
    }
}
