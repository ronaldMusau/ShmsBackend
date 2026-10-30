using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace ShmsBackend.Api.Filters;

/// <summary>
/// Caches a successful GET result in memory for a short time. Chart and summary endpoints run heavy
/// aggregates over the whole data set; many users (and a page's several charts re-fetching on every
/// filter click) ask the same question within seconds, so the answer is reused instead of recomputed.
/// The key includes the signed-in user, role and the full query string, so one user's scoped data
/// (e.g. a landlord's own flats) is never served to another. Add ?refresh=true to force a fresh read.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ShortCacheAttribute : Attribute, IAsyncActionFilter
{
    private readonly int _seconds;
    public ShortCacheAttribute(int seconds = 60) { _seconds = seconds; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!HttpMethods.IsGet(http.Request.Method) || http.Request.Query.ContainsKey("refresh"))
        {
            await next();
            return;
        }

        var cache = http.RequestServices.GetRequiredService<IMemoryCache>();
        var user = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anon";
        var role = http.User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        var key = $"shortcache:{user}:{role}:{http.Request.Path}{http.Request.QueryString}";

        if (cache.TryGetValue(key, out object? cached) && cached is not null)
        {
            http.Response.Headers["X-Cache"] = "HIT";
            context.Result = new OkObjectResult(cached);
            return;
        }

        var executed = await next();
        if (executed.Exception == null && executed.Result is OkObjectResult { Value: not null } ok && ok.StatusCode is null or 200)
        {
            cache.Set(key, ok.Value, TimeSpan.FromSeconds(_seconds));
            http.Response.Headers["X-Cache"] = "MISS";
        }
    }
}
