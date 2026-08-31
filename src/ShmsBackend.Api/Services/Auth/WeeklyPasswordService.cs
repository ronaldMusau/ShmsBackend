using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Services.Auth;

public class WeeklyPasswordService : IWeeklyPasswordService
{
    private readonly ShmsDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IWeeklyPasswordPlaintextCache _plaintextCache;
    private readonly ILogger<WeeklyPasswordService> _logger;

    public WeeklyPasswordService(
        ShmsDbContext context,
        IEmailService emailService,
        IWeeklyPasswordPlaintextCache plaintextCache,
        ILogger<WeeklyPasswordService> logger)
    {
        _context = context;
        _emailService = emailService;
        _plaintextCache = plaintextCache;
        _logger = logger;
    }

    public async Task GenerateAndRotateAsync()
    {
        var plaintext = GenerateStrongPassword();
        var now = DateTime.UtcNow;

        var currentActive = await _context.WeeklyDefaultPasswords
            .Where(w => w.IsActive)
            .ToListAsync();
        foreach (var w in currentActive)
            w.IsActive = false;

        _context.WeeklyDefaultPasswords.Add(new WeeklyDefaultPassword
        {
            Id = Guid.NewGuid(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(plaintext),
            GeneratedAt = now,
            ExpiresAt = now.AddDays(7),
            IsActive = true
        });

        await _context.SaveChangesAsync();

        // Keep the plaintext in memory for this cycle so newly-opted-in admins can be emailed immediately.
        _plaintextCache.Set(plaintext);

        var subscribers = await (
            from s in _context.WeeklyPasswordSubscribers
            join a in _context.Admins on s.AdminId equals a.Id
            select new { a.Email, a.FirstName }
        ).ToListAsync();

        var sent = 0;
        foreach (var sub in subscribers)
        {
            try
            {
                await _emailService.SendWeeklyPasswordEmailAsync(sub.Email, sub.FirstName, plaintext);
                sent++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send rotated weekly password email to {Email}", sub.Email);
            }
        }

        _logger.LogInformation("Weekly default password rotated; emailed {Sent}/{Total} subscriber(s)",
            sent, subscribers.Count);
    }

    public async Task<string?> GetCurrentPasswordHashAsync()
    {
        var current = await _context.WeeklyDefaultPasswords
            .Where(w => w.IsActive)
            .OrderByDescending(w => w.GeneratedAt)
            .FirstOrDefaultAsync();
        return current?.PasswordHash;
    }

    public Task<bool> IsSubscribedAsync(Guid adminId) =>
        _context.WeeklyPasswordSubscribers.AnyAsync(s => s.AdminId == adminId);

    public async Task<IReadOnlyList<WeeklyPasswordSubscriberDto>> GetSubscribersAsync()
    {
        return await (
            from s in _context.WeeklyPasswordSubscribers
            join a in _context.Admins on s.AdminId equals a.Id
            orderby a.FirstName, a.LastName
            select new WeeklyPasswordSubscriberDto
            {
                AdminId = a.Id,
                FirstName = a.FirstName,
                LastName = a.LastName,
                Email = a.Email,
                Role = a.UserType.ToString(),
                SubscribedAt = s.CreatedAt
            }
        ).ToListAsync();
    }

    public async Task<IReadOnlyList<EligibleAdminDto>> GetAllEligibleAdminsAsync()
    {
        var subscriberIds = await _context.WeeklyPasswordSubscribers
            .Select(s => s.AdminId)
            .ToListAsync();
        var subscriberIdSet = subscriberIds.ToHashSet();

        var admins = await _context.Admins
            .OrderBy(a => a.FirstName).ThenBy(a => a.LastName)
            .Select(a => new { a.Id, a.FirstName, a.LastName, a.Email, a.UserType })
            .ToListAsync();

        return admins.Select(a => new EligibleAdminDto
        {
            AdminId = a.Id,
            FirstName = a.FirstName,
            LastName = a.LastName,
            Email = a.Email,
            Role = a.UserType.ToString(),
            IsSubscribed = subscriberIdSet.Contains(a.Id)
        }).ToList();
    }

    public async Task SetSubscriptionAsync(Guid adminId, bool subscribe)
    {
        var existing = await _context.WeeklyPasswordSubscribers
            .FirstOrDefaultAsync(s => s.AdminId == adminId);

        if (subscribe)
        {
            if (existing != null)
                return; // already opted in — no-op

            _context.WeeklyPasswordSubscribers.Add(new WeeklyPasswordSubscriber
            {
                Id = Guid.NewGuid(),
                AdminId = adminId,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            var plaintext = _plaintextCache.Get();
            if (!string.IsNullOrEmpty(plaintext))
            {
                var admin = await _context.Admins.FirstOrDefaultAsync(a => a.Id == adminId);
                if (admin != null)
                {
                    try
                    {
                        await _emailService.SendWeeklyPasswordEmailAsync(admin.Email, admin.FirstName, plaintext);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send current weekly password to newly subscribed admin {AdminId}", adminId);
                    }
                }
            }
            else
            {
                _logger.LogInformation(
                    "Admin {AdminId} subscribed to weekly password but no plaintext is cached this cycle; they will receive it at the next rotation",
                    adminId);
            }
        }
        else
        {
            if (existing == null)
                return;

            _context.WeeklyPasswordSubscribers.Remove(existing);
            await _context.SaveChangesAsync();
        }
    }

    // ── Password generation ──────────────────────────────────────────────────

    // Ambiguous-looking characters (0/O, 1/l/I) are excluded for readability of a shared credential.
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*-_=+?";

    private static string GenerateStrongPassword(int length = 20)
    {
        const string all = Upper + Lower + Digits + Symbols;
        var chars = new char[length];

        // Guarantee at least one character from each class.
        chars[0] = Upper[RandomNumberGenerator.GetInt32(Upper.Length)];
        chars[1] = Lower[RandomNumberGenerator.GetInt32(Lower.Length)];
        chars[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        chars[3] = Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)];

        for (var i = 4; i < length; i++)
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];

        // Fisher–Yates shuffle so the guaranteed characters aren't always at the front.
        for (var i = length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
