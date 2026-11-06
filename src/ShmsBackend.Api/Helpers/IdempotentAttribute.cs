using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using StackExchange.Redis;

namespace ShmsBackend.Api.Helpers;

/// <summary>
/// Makes a money-moving POST safe to repeat. When the caller sends an Idempotency-Key header, the first request runs
/// and its successful answer is remembered for 10 minutes; a repeat with the same key (a retry after a dropped
/// connection, say) gets the same answer instead of sending a second M-Pesa prompt. A repeat that arrives while the
/// first is still running gets 409. Failed requests are not remembered, so they can be retried with the same key.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IAsyncActionFilter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private static readonly Regex KeyShape = new("^[A-Za-z0-9-]{8,64}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrEmpty(key)) { await next(); return; }

        if (!KeyShape.IsMatch(key))
        {
            context.Result = new BadRequestObjectResult(new { success = false, message = "Invalid request key." });
            return;
        }

        var db = http.RequestServices.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        var user = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anon";
        var redisKey = $"shms:idem:{user}:{http.Request.Path.Value}:{key}";

        if (!await db.StringSetAsync(redisKey, "inflight", Window, When.NotExists))
        {
            var stored = (string?)await db.StringGetAsync(redisKey);
            if (stored == null || stored == "inflight")
            {
                context.Result = new ObjectResult(new { success = false, message = "This request is already being processed." }) { StatusCode = 409 };
            }
            else
            {
                var sep = stored.IndexOf('|');
                context.Result = new ContentResult
                {
                    StatusCode = int.Parse(stored[..sep]),
                    ContentType = "application/json",
                    Content = stored[(sep + 1)..]
                };
            }
            return;
        }

        var executed = await next();
        if (executed.Exception == null && executed.Result is ObjectResult { Value: not null } res
            && (res.StatusCode ?? 200) is >= 200 and < 300)
        {
            await db.StringSetAsync(redisKey, $"{res.StatusCode ?? 200}|{JsonSerializer.Serialize(res.Value, Json)}", Window);
        }
        else
        {
            await db.KeyDeleteAsync(redisKey); // nothing worth replaying: let the caller retry with the same key
        }
    }
}
