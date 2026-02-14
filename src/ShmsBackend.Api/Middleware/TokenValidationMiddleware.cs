using ShmsBackend.Api.Services.Auth;

namespace ShmsBackend.Api.Middleware;

public class TokenValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TokenValidationMiddleware> _logger;

    public TokenValidationMiddleware(RequestDelegate next, ILogger<TokenValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITokenBlacklistService tokenBlacklistService)
    {
        var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();

        if (!string.IsNullOrEmpty(token))
        {
            var isBlacklisted = await tokenBlacklistService.IsTokenBlacklistedAsync(token);

            if (isBlacklisted)
            {
                _logger.LogWarning("Blacklisted token used");
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    message = "Token has been revoked"
                });
                return;
            }
        }

        await _next(context);
    }
}