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

        await _next(context);

        var duration = DateTime.UtcNow - startTime;

        _logger.LogInformation(
            "Completed {Method} {Path} with status {StatusCode} in {Duration}ms",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            duration.TotalMilliseconds
        );
    }
}