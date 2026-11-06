using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ShmsBackend.Api.Configuration;
using ShmsBackend.Api.Hubs;
using ShmsBackend.Api.Middleware;
using ShmsBackend.Api.Services.Agreements;
using ShmsBackend.Api.Services.Auth;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services;
using ShmsBackend.Api.Services.User;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Api.Services.Portal;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Api.Services.PortalAuth;
using ShmsBackend.Api.Services.Payment;
using ShmsBackend.Api.Services.Reports;
using ShmsBackend.Api.Services.Reward;
using ShmsBackend.Api.Utilities;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Repositories;
using ShmsBackend.Data.Repositories.Interfaces;
using System.Text;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// No UseForwardedHeaders is configured in this pipeline, so the only trustworthy client-IP signal
// is the direct socket address — reading X-Forwarded-For here without a trusted-proxy setup would
// let a client spoof its own rate-limit partition.
static string GetClientIp(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

// Authenticated requests are partitioned per-user (so one user's usage across devices/IPs shares a
// single budget); anonymous requests fall back to IP.
static string GetDefaultPartitionKey(HttpContext context)
{
    if (context.User?.Identity?.IsAuthenticated == true)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
            return $"user:{userId}";
    }
    return $"ip:{GetClientIp(context)}";
}

// Add Configuration Options
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("JwtOptions"));
builder.Services.Configure<ResendEmailOptions>(builder.Configuration.GetSection("ResendEmailOptions"));
builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection("RedisOptions"));
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));

// Add DbContext
builder.Services.AddDbContext<ShmsDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
});

// Add Redis for Distributed Caching
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["RedisOptions:Configuration"];
    options.InstanceName = builder.Configuration["RedisOptions:InstanceName"];
});
// AddStackExchangeRedisCache above only exposes IDistributedCache — it does not register
// IConnectionMultiplexer itself, but CacheHelper.RemoveByPrefixAsync needs raw SCAN access for
// prefix-based bulk invalidation, so it's registered explicitly here against the same connection.
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
    StackExchange.Redis.ConnectionMultiplexer.Connect(builder.Configuration["RedisOptions:Configuration"]!));
builder.Services.AddScoped<ICacheHelper, CacheHelper>();

// Add Repositories
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Add Services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPortalAuthService, PortalAuthService>();

// Weekly shared password — singleton in-memory plaintext cache + scoped service + hourly rotation scheduler
builder.Services.AddSingleton<IWeeklyPasswordPlaintextCache, WeeklyPasswordPlaintextCache>();
builder.Services.AddScoped<IWeeklyPasswordService, WeeklyPasswordService>();

// Weekly client-portal support password — same pattern, completely separate instances
builder.Services.AddSingleton<IWeeklyClientPasswordPlaintextCache, WeeklyClientPasswordPlaintextCache>();
builder.Services.AddScoped<IWeeklyClientPasswordService, WeeklyClientPasswordService>();

// Portal Services
builder.Services.AddScoped<ILandlordService, LandlordService>();
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<HouseService>();
builder.Services.AddScoped<FlatService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ITokenBlacklistService, TokenBlacklistService>();
builder.Services.AddScoped<IFrontendUrlService, FrontendUrlService>();
builder.Services.AddScoped<TenantQueryService>();
builder.Services.AddScoped<PaymentQueryService>();
builder.Services.AddScoped<HouseQueryService>();
builder.Services.AddScoped<VacateQueryService>();
builder.Services.AddScoped<ComplaintQueryService>();
builder.Services.AddScoped<LandlordQueryService>();
builder.Services.AddScoped<AgentQueryService>();
builder.Services.AddScoped<RewardTransactionQueryService>();
builder.Services.AddScoped<ServiceChargeQueryService>();
builder.Services.AddScoped<RefundQueryService>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Payment.LeavingBalanceService>();
builder.Services.AddScoped<DeductionQueryService>();
builder.Services.AddScoped<ForfeitedAdvanceQueryService>();
builder.Services.AddScoped<ExpenseQueryService>();
builder.Services.AddScoped<ListingViewingSessionQueryService>();
builder.Services.AddScoped<ExplorerInterestQueryService>();
builder.Services.AddScoped<IReportRenderer, ReportRenderer>();
builder.Services.AddScoped<IReceiptRenderer, ReceiptRenderer>();
builder.Services.AddScoped<TenantReportBuilder>();
builder.Services.AddScoped<PaymentReportBuilder>();
builder.Services.AddScoped<HouseReportBuilder>();
builder.Services.AddScoped<VacateReportBuilder>();
builder.Services.AddScoped<OverdueReportBuilder>();
builder.Services.AddScoped<ComplaintReportBuilder>();
builder.Services.AddScoped<LandlordReportBuilder>();
builder.Services.AddScoped<AgentReportBuilder>();
builder.Services.AddScoped<RewardReportBuilder>();
builder.Services.AddScoped<ServiceChargeReportBuilder>();
builder.Services.AddScoped<ComplaintChargeReportBuilder>();
builder.Services.AddScoped<RefundReportBuilder>();
builder.Services.AddScoped<LeavingBalanceReportBuilder>();
builder.Services.AddScoped<DeductionReportBuilder>();
builder.Services.AddScoped<ForfeitedAdvanceReportBuilder>();
builder.Services.AddScoped<ExpenseReportBuilder>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Analytics.ComplaintAnalyticsService>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Analytics.FinancialStandingService>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Analytics.RefundAnalyticsService>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Analytics.DeductionAnalyticsService>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Analytics.ServiceChargeAnalyticsService>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Analytics.ExpenseAnalyticsService>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Analytics.SessionAnalyticsService>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Analytics.ExplorerInterestAnalyticsService>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Analytics.RewardAnalyticsService>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Analytics.ForfeitedAdvanceAnalyticsService>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Analytics.AgreementAnalyticsService>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Analytics.PaymentAnalyticsService>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Analytics.OverdueAnalyticsService>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Analytics.AgentAnalyticsService>();
builder.Services.AddScoped<ShmsBackend.Api.Services.Analytics.LandlordAnalyticsService>();
// RentCollectionHelper is a static class (pure functions over an IQueryable<Payment> passed in) —
// no DI registration needed or possible.
builder.Services.AddScoped<ListingReportBuilder>();
builder.Services.AddScoped<SessionReportBuilder>();
builder.Services.AddScoped<ExplorerInterestReportBuilder>();
builder.Services.AddScoped<AgreementReportBuilder>();

// Register HttpClient factory and EmailService
builder.Services.AddHttpClient();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
builder.Services.AddScoped<IAgreementService, AgreementService>();

// M-Pesa
builder.Services.Configure<MpesaConfig>(builder.Configuration.GetSection("Mpesa"));
builder.Services.AddHttpClient("Mpesa", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<IMpesaService, MpesaService>();
builder.Services.AddScoped<IPaymentDistributionService, PaymentDistributionService>();
builder.Services.AddScoped<VacateOwedCalculator>();
builder.Services.AddScoped<VacateClosureService>();
builder.Services.AddScoped<IRewardService, RewardService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddHostedService<PaymentSchedulerService>();
builder.Services.AddSingleton<ComplaintReminderSchedulerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ComplaintReminderSchedulerService>());
builder.Services.AddSingleton<SessionSchedulerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionSchedulerService>());
builder.Services.AddHostedService<WeeklyPasswordSchedulerService>();
builder.Services.AddHostedService<WeeklyClientPasswordSchedulerService>();
builder.Services.AddHostedService<PreRegistrationExpirySchedulerService>();

// Add JWT Authentication
var jwtOptions = builder.Configuration.GetSection("JwtOptions").Get<JwtOptions>();
// A short signing secret can be guessed offline from any captured token, which would let someone forge logins.
if (string.IsNullOrWhiteSpace(jwtOptions?.Secret) || Encoding.UTF8.GetByteCount(jwtOptions.Secret) < 32)
    throw new InvalidOperationException("JwtOptions:Secret must be at least 32 characters. Set a long random value as a secret.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        // Only the algorithm we issue with. Blocks "alg: none" and algorithm-swap tricks.
        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
        ValidIssuer = jwtOptions!.Issuer,
        ValidAudience = jwtOptions.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtOptions.Secret)),
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = ClaimTypes.Role
    };

    // Configure JWT for SignalR
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            if (!string.IsNullOrEmpty(accessToken) &&
                path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
        // A valid signature is not enough: the account must still exist and be active. This is what makes
        // deactivating, deleting or locking someone take effect within a minute instead of at token expiry.
        OnTokenValidated = async context =>
        {
            if (!Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                context.Fail("Invalid token");
                return;
            }
            var cache = context.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            var cacheKey = $"auth:active:{userId}";
            if (!cache.TryGetValue(cacheKey, out bool active))
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<ShmsDbContext>();
                active = await db.Admins.AsNoTracking().AnyAsync(a => a.Id == userId && a.IsActive && !a.IsDeleted && !a.IsLockedOut)
                      || await db.PortalUsers.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive && !u.IsDeleted && !u.IsLockedOut);
                cache.Set(cacheKey, active, TimeSpan.FromSeconds(60));
            }
            if (!active) context.Fail("Account is not active");
        }
    };
});

// Add Authorization with Policies
builder.Services.AddAuthorization(options =>
{
    // Default-deny: any endpoint without an attribute needs a signed-in user.
    // Public endpoints must say [AllowAnonymous] explicitly.
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // Admin hierarchy policies
    options.AddPolicy("SuperAdminOnly", policy =>
        policy.RequireRole("SuperAdmin"));

    options.AddPolicy("AdminAndAbove", policy =>
        policy.RequireRole("SuperAdmin", "Admin"));

    options.AddPolicy("CanViewUsers", policy =>
        policy.RequireRole("SuperAdmin", "Admin", "Manager", "Accountant", "Secretary"));

    options.AddPolicy("CanCreateUsers", policy =>
        policy.RequireRole("SuperAdmin", "Admin"));

    options.AddPolicy("CanUpdateUsers", policy =>
        policy.RequireRole("SuperAdmin", "Admin"));

    options.AddPolicy("CanDeleteUsers", policy =>
        policy.RequireRole("SuperAdmin"));

    options.AddPolicy("CanToggleUserStatus", policy =>
        policy.RequireRole("SuperAdmin"));

    // Portal policies
    options.AddPolicy("PortalUser", policy =>
        policy.RequireRole("Landlord", "Agent", "Tenant", "Explorer"));

    options.AddPolicy("PortalAdminOrStaff", policy =>
        policy.RequireRole("SuperAdmin", "Admin", "Secretary", "Manager"));
});

// Add Rate Limiting
// Limits are configurable under "RateLimiting" in appsettings (PermitLimit + WindowSeconds per policy);
// the defaults below apply when a key is absent. Limits are per process (in-memory): with several API
// instances behind a load balancer each instance keeps its own counters.
int RlInt(string key, int fallback) => int.TryParse(builder.Configuration[$"RateLimiting:{key}"], out var v) && v > 0 ? v : fallback;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        // Sliding-window leases don't always carry a retry hint; fall back to a safe 30 s so clients
        // always get a Retry-After value.
        var retrySeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? (int)Math.Ceiling(retryAfter.TotalSeconds)
            : 30;
        context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, retrySeconds).ToString();
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            success = false,
            message = "Too many requests. Please try again shortly."
        }, cancellationToken);
    };

    // Per-IP sliding window (no boundary burst): used for anonymous, abuse-prone endpoints.
    void IpPolicy(string name, string key, int permit, int windowSeconds) =>
        options.AddPolicy(name, httpContext =>
            RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: $"{name}:ip:{GetClientIp(httpContext)}",
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = RlInt($"{key}:PermitLimit", permit),
                    Window = TimeSpan.FromSeconds(RlInt($"{key}:WindowSeconds", windowSeconds)),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                }));

    // Per-user (falls back to IP when anonymous) sliding window.
    void UserPolicy(string name, string key, int permit, int windowSeconds) =>
        options.AddPolicy(name, httpContext =>
            RateLimitPartition.GetSlidingWindowLimiter(
                partitionKey: $"{name}:{GetDefaultPartitionKey(httpContext)}",
                factory: _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = RlInt($"{key}:PermitLimit", permit),
                    Window = TimeSpan.FromSeconds(RlInt($"{key}:WindowSeconds", windowSeconds)),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                }));

    // Applied via [EnableRateLimiting("strict")] on anonymous public-listing interactions and session
    // booking — 10/min per IP, regardless of auth state.
    options.AddPolicy("strict", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"ip:{GetClientIp(httpContext)}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = RlInt("Strict:PermitLimit", 10),
                Window = TimeSpan.FromSeconds(RlInt("Strict:WindowSeconds", 60)),
                QueueLimit = 0
            }));

    // Credential endpoints (login, pre-login, OTP verify, token refresh). Account lockout after
    // repeated failures already exists; this stops one IP from spraying many accounts.
    IpPolicy("auth", "Auth", 10, 60);
    // Email/OTP-sending and account-creation endpoints (reset requests, register, verify, set password):
    // stops email bombing and mass sign-ups.
    IpPolicy("auth-sensitive", "AuthSensitive", 10, 600);
    // M-Pesa STK push initiate/retry: one user cannot flood the customer's phone.
    UserPolicy("payment", "Payment", 6, 60);
    // Reports and analytics run heavy aggregate queries.
    UserPolicy("heavy", "Heavy", 120, 60);
    // M-Pesa callback: Safaricom posts from a few IPs, so the ceiling is generous.
    IpPolicy("callback", "Callback", 600, 60);

    // Default for everything else: signed-in users share one budget across devices; anonymous
    // traffic is limited per IP, lower than signed-in.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var signedIn = httpContext.User?.Identity?.IsAuthenticated == true;
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetDefaultPartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = signedIn ? RlInt("Global:SignedInPerMinute", 1000) : RlInt("Global:AnonymousPerMinute", 300),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

// Behind a reverse proxy / load balancer the socket address is the proxy's, so every visitor would share
// one rate-limit bucket. List the proxy under ForwardedHeaders:KnownProxies (array of IPs) to trust its
// X-Forwarded-For; when empty nothing is trusted and the socket address is used.
var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? Array.Empty<string>();
if (knownProxies.Length > 0)
{
    builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
        foreach (var ip in knownProxies) o.KnownProxies.Add(System.Net.IPAddress.Parse(ip));
    });
}

// Add CORS
// Origins come from Cors:AllowedOrigins (appsettings / environment variables). In Development the
// localhost and dev-tunnel origins are always added so the portals keep working through the tunnel.
var devOrigins = new[]
{
    "http://localhost:4200", "http://localhost:4201",
    "https://n6lscp1j-4200.eun1.devtunnels.ms", "https://n6lscp1j-4201.eun1.devtunnels.ms"
};
var corsOrigins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
    .Where(o => !string.IsNullOrWhiteSpace(o) && !o.Contains("your-production-frontend"))
    .Concat(builder.Environment.IsDevelopment() ? devOrigins : Array.Empty<string>())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
if (corsOrigins.Length == 0) corsOrigins = devOrigins;
builder.Services.AddSingleton<ShmsBackend.Api.Services.Security.PayloadEncryptionService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.WithOrigins(corsOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()
            .WithExposedHeaders("Server-Timing", "Retry-After");
    });
});

// Compress JSON responses (Brotli/gzip): large list and chart payloads shrink several-fold, which
// matters most on slow mobile links and through tunnels/proxies.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.AddMemoryCache();

// Add Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
    });

// Add SignalR
builder.Services.AddSignalR();

// Add Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SHMS Backend API",
        Version = "v1",
        Description = "Student Housing Management System Backend API"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "SHMS Backend API v1");
        c.RoutePrefix = "swagger";
    });
}

if (knownProxies.Length > 0) app.UseForwardedHeaders();
app.UseResponseCompression();

// Security headers on every API response (API serves JSON, so the CSP is the strictest form).
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var h = context.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        h["Cross-Origin-Resource-Policy"] = "cross-origin"; // portals on other origins read the API
        // Frame blocking + CSP only for JSON: file responses (PDFs, images) may be previewed in an iframe.
        var isJson = context.Response.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true;
        if (isJson)
        {
            h["X-Frame-Options"] = "DENY";
            h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        }
        // Authenticated API responses carry personal or financial data: no storage by shared caches.
        var path = context.Request.Path;
        if (path.StartsWithSegments("/api") && !path.Value!.Contains("/public", StringComparison.OrdinalIgnoreCase)
            && !h.ContainsKey("Cache-Control"))
            h["Cache-Control"] = "no-store";
        return Task.CompletedTask;
    });
    await next();
});


// TEMPORARILY DISABLED for VS Code Dev Tunnel testing — port 5001 (HTTP) is being used
// because port 7001 (HTTPS) breaks TLS/SNI through the tunnel. This redirect was sending
// tunnel traffic back to the broken HTTPS port, causing CORS preflights to fail silently.
// RESTORE THIS before production: app.UseHttpsRedirection();
// Turn on with Security:UseHttpsRedirection=true (and keep it off for the dev tunnel).
if (builder.Configuration.GetValue<bool>("Security:UseHttpsRedirection"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowAll");

// Always-on payload encryption: every /api request/response body is encrypted (see PayloadEncryptionMiddleware).
// Exception + logging middleware sit INSIDE it so error responses are encrypted too and logs never see ciphertext-only noise.
app.UseMiddleware<ShmsBackend.Api.Middleware.PayloadEncryptionMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<LoggingMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TokenValidationMiddleware>();
app.UseRateLimiter();

// Fail fast if the encryption key is missing in non-development environments.
app.Services.GetRequiredService<ShmsBackend.Api.Services.Security.PayloadEncryptionService>();

app.MapControllers();

// Map SignalR Hub
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();
