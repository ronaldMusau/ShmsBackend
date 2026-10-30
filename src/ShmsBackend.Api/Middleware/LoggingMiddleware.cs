namespace ShmsBackend.Api.Middleware;

public class LoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(RequestDelegate next, ILogger<LoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var startTime = DateTime.UtcNow;

        _logger.LogInformation(
            "Incoming {Method} request to {Path}",
            context.Request.Method,
            context.Request.Path
        );

        // Server-Timing lets the browser (and any tunnel/proxy in between) see the API's own processing
        // time, separate from network latency.
        context.Response.OnStarting(() =>
        {
            var ms = (DateTime.UtcNow - startTime).TotalMilliseconds;
            context.Response.Headers["Server-Timing"] = $"app;dur={ms:0.#}";
            return Task.CompletedTask;
        });

        await _next(context);

        var duration = DateTime.UtcNow - startTime;

        if (duration.TotalMilliseconds > 500)
            _logger.LogWarning("SLOW {Method} {Path} took {Duration}ms", context.Request.Method, context.Request.Path, (int)duration.TotalMilliseconds);

        _logger.LogInformation(
            "Completed {Method} {Path} with status {StatusCode} in {Duration}ms",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            duration.TotalMilliseconds
        );
    }
}