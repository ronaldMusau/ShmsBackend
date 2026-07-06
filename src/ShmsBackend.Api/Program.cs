using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ShmsBackend.Api.Configuration;
using ShmsBackend.Api.Hubs;
using ShmsBackend.Api.Middleware;
using ShmsBackend.Api.Services.Auth;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.OTP;
using ShmsBackend.Api.Services.User;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Api.Services.Portal;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Api.Services.PortalAuth;
using ShmsBackend.Api.Services.Payment;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Repositories;
using ShmsBackend.Data.Repositories.Interfaces;
using System.Text;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add Configuration Options
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("JwtOptions"));
builder.Services.Configure<ResendEmailOptions>(builder.Configuration.GetSection("ResendEmailOptions"));
builder.Services.Configure<RedisOptions>(builder.Configuration.GetSection("RedisOptions"));
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));

// Add DbContext
builder.Services.AddDbContext<ShmsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Redis for Distributed Caching
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["RedisOptions:Configuration"];
    options.InstanceName = builder.Configuration["RedisOptions:InstanceName"];
});

// Add Repositories
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Add Services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPortalAuthService, PortalAuthService>();

// Portal Services
builder.Services.AddScoped<ILandlordService, LandlordService>();
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<HouseService>();
builder.Services.AddScoped<FlatService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ITokenBlacklistService, TokenBlacklistService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IPreAuthCacheService, PreAuthCacheService>();
builder.Services.AddScoped<IFrontendUrlService, FrontendUrlService>();

// Register HttpClient factory and EmailService
builder.Services.AddHttpClient();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// M-Pesa
builder.Services.Configure<MpesaConfig>(builder.Configuration.GetSection("Mpesa"));
builder.Services.AddHttpClient("Mpesa");
builder.Services.AddSingleton<IMpesaService, MpesaService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddHostedService<PaymentSchedulerService>();

// Add JWT Authentication
var jwtOptions = builder.Configuration.GetSection("JwtOptions").Get<JwtOptions>();

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
        }
    };
});

// Add Authorization with Policies
builder.Services.AddAuthorization(options =>
{
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

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "http://localhost:4201")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Add Controllers
builder.Services.AddControllers();

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

// Custom Middleware
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<LoggingMiddleware>();
app.UseMiddleware<TokenValidationMiddleware>();

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Map SignalR Hub
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();
