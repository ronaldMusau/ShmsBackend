using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Middleware;

/// <summary>
/// Portal users (landlord, tenant, agent, explorer) must have accepted the current terms for their account type
/// before they can use the API. Until they do, every request except sign-in, sign-out, token refresh and the terms
/// pages themselves gets 403 TERMS_REQUIRED. Staff tokens are never touched (they have no portal_user_type claim),
/// and a role with no published terms asks for nothing. Lookups are cached; the admin terms editor clears the cache.
/// </summary>
public class TermsAcceptanceMiddleware
{
    private readonly RequestDelegate _next;
    public TermsAcceptanceMiddleware(RequestDelegate next) => _next = next;

    public static string CurrentKey(int role) => $"terms-cur:{role}";
    private static string AcceptedKey(Guid userId, int role, int version) => $"terms-ok:{userId}:{role}:{version}";

    private sealed record Current(int Version, bool HasContent);

    private static readonly string[] OpenPaths =
    {
        "/api/portalauth/login", "/api/portalauth/logout", "/api/portalauth/refresh-token",
        "/api/portalauth/terms", "/api/secure/key", "/api/health"
    };

    public async Task InvokeAsync(HttpContext ctx, ShmsDbContext db, IMemoryCache cache)
    {
        var path = ctx.Request.Path.Value ?? "";
        var portalClaim = ctx.User.FindFirst("portal_user_type")?.Value;
        if (ctx.User.Identity?.IsAuthenticated != true
            || portalClaim == null
            || !path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            || HttpMethods.IsOptions(ctx.Request.Method)
            || path.Contains("/mpesa/callback/", StringComparison.OrdinalIgnoreCase)
            || OpenPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || !Enum.TryParse<PortalUserType>(portalClaim, out var type)
            || !Guid.TryParse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            await _next(ctx);
            return;
        }

        var role = (int)type;
        var current = await cache.GetOrCreateAsync(CurrentKey(role), async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            var t = await db.TermsAndConditions.AsNoTracking().Where(x => x.Role == role)
                .Select(x => new { x.Version, x.Content }).FirstOrDefaultAsync();
            return new Current(t?.Version ?? 0, !string.IsNullOrWhiteSpace(t?.Content));
        });

        if (current == null || !current.HasContent) { await _next(ctx); return; }

        var key = AcceptedKey(userId, role, current.Version);
        if (!cache.TryGetValue(key, out _))
        {
            var accepted = await db.TermsAcceptances.AsNoTracking()
                .AnyAsync(a => a.PortalUserId == userId && a.Role == role && a.Version == current.Version);
            if (!accepted)
            {
                ctx.Items["terms-required"] = true; // a normal first-time step, not suspicious: keep it out of the threat guard's 403 count
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsJsonAsync(new
                {
                    success = false, code = "TERMS_REQUIRED",
                    message = "Please read and accept the terms and conditions to continue."
                });
                return;
            }
            cache.Set(key, true, TimeSpan.FromMinutes(10));
        }

        await _next(ctx);
    }
}
