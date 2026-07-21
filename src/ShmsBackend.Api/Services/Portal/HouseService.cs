using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.House;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Portal;

public class HouseService
{
    private readonly ShmsDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly ILogger<HouseService> _logger;

    public HouseService(ShmsDbContext context, INotificationService notificationService, ILogger<HouseService> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<object> CreateAsync(CreateHouseDto dto)
    {
        var houseTypeExists = await _context.HouseTypes.AnyAsync(t => t.Id == dto.HouseTypeId && t.IsActive);
        if (!houseTypeExists)
            throw new InvalidOperationException("Invalid house type.");

        var flat = await _context.Flats.FindAsync(dto.FlatId);
        if (flat == null)
            throw new InvalidOperationException("Flat not found.");

        var duplicate = await _context.Houses
            .AnyAsync(h => h.FlatId == dto.FlatId && h.HouseNumber == dto.HouseNumber);
        if (duplicate)
            throw new InvalidOperationException($"House number '{dto.HouseNumber}' already exists in this flat.");

        var house = new House
        {
            Id = Guid.NewGuid(),
            HouseNumber = dto.HouseNumber,
            HouseTypeId = dto.HouseTypeId,
            RentFee = dto.RentFee,
            DepositFee = dto.DepositFee,
            OccupancyStatus = OccupancyStatus.Vacant,
            PaymentStatus = PaymentStatus.NotPaid,
            FlatId = dto.FlatId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Houses.Add(house);
        await _context.SaveChangesAsync();

        try
        {
            var flatName = flat.FlatName;
            await _notificationService.SendToRolesAsync(
                new[]
                {
                    NotificationAudience.SuperAdmin,
                    NotificationAudience.Admin,
                    NotificationAudience.Secretary,
                    NotificationAudience.Manager,
                    NotificationAudience.Accountant
                },
                $"House {house.HouseNumber} added to {flatName}.",
                "property"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification for house creation {HouseNumber}", house.HouseNumber);
        }

        try
        {
            await _notificationService.SendToUserAsync(
                flat.LandlordId.ToString(),
                $"A new house ({house.HouseNumber}) has been added to your flat '{flat.FlatName}'.",
                "property");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send house creation notification to landlord");
        }

        return (await GetByIdAsync(house.Id))!;
    }

    public async Task<IEnumerable<object>> GetAllAsync()
    {
        return await _context.Houses
            .Include(h => h.Flat)
            .Include(h => h.HouseTypeRef)
            .Select(h => new
            {
                h.Id,
                h.HouseNumber,
                HouseTypeName = h.HouseTypeRef != null ? h.HouseTypeRef.Name : null,
                h.RentFee,
                h.DepositFee,
                OccupancyStatus = h.OccupancyStatus.ToString(),
                PaymentStatus = h.PaymentStatus.ToString(),
                h.FlatId,
                Flat = h.Flat == null ? null : new { h.Flat.Id, h.Flat.FlatName },
                h.CreatedAt,
                h.UpdatedAt,
                ImagePaths = h.Images.OrderBy(i => i.SortOrder).Select(i => i.ImagePath).ToList()
            })
            .ToListAsync<object>();
    }

    public async Task<object?> GetByIdAsync(Guid id)
    {
        var house = await _context.Houses
            .Include(h => h.Flat)
            .Include(h => h.Images)
            .Include(h => h.HouseTypeRef)
            .FirstOrDefaultAsync(h => h.Id == id);

        if (house == null) return null;
        return MapToDto(house);
    }

    public async Task<IEnumerable<object>> GetByFlatAsync(Guid flatId)
    {
        return await _context.Houses
            .Where(h => h.FlatId == flatId)
            .Include(h => h.HouseTypeRef)
            .Select(h => new
            {
                h.Id,
                h.HouseNumber,
                HouseTypeName = h.HouseTypeRef != null ? h.HouseTypeRef.Name : null,
                h.RentFee,
                h.DepositFee,
                OccupancyStatus = h.OccupancyStatus.ToString(),
                PaymentStatus = h.PaymentStatus.ToString(),
                h.FlatId,
                h.CreatedAt,
                h.UpdatedAt,
                ImagePaths = h.Images.OrderBy(i => i.SortOrder).Select(i => i.ImagePath).ToList()
            })
            .ToListAsync<object>();
    }

    public async Task<object?> UpdateAsync(Guid id, UpdateHouseDto dto)
    {
        var house = await _context.Houses
            .Include(h => h.Flat)
            .FirstOrDefaultAsync(h => h.Id == id);
        if (house == null) return null;

        if (dto.HouseNumber != null)
        {
            var duplicate = await _context.Houses
                .AnyAsync(h => h.FlatId == house.FlatId && h.HouseNumber == dto.HouseNumber && h.Id != id);
            if (duplicate)
                throw new InvalidOperationException($"House number '{dto.HouseNumber}' already exists in this flat.");
            house.HouseNumber = dto.HouseNumber;
        }

        if (dto.HouseTypeId.HasValue)
        {
            var houseTypeExists = await _context.HouseTypes.AnyAsync(t => t.Id == dto.HouseTypeId.Value && t.IsActive);
            if (!houseTypeExists)
                throw new InvalidOperationException("Invalid house type.");
            house.HouseTypeId = dto.HouseTypeId.Value;
        }

        if (dto.RentFee.HasValue) house.RentFee = dto.RentFee.Value;
        if (dto.DepositFee.HasValue) house.DepositFee = dto.DepositFee.Value;

        if (dto.OccupancyStatus != null)
        {
            if (!Enum.TryParse<OccupancyStatus>(dto.OccupancyStatus, true, out var occupancyStatus))
                throw new InvalidOperationException($"Invalid occupancy status: {dto.OccupancyStatus}");
            house.OccupancyStatus = occupancyStatus;
        }

        if (dto.PaymentStatus != null)
        {
            if (!Enum.TryParse<PaymentStatus>(dto.PaymentStatus, true, out var paymentStatus))
                throw new InvalidOperationException($"Invalid payment status: {dto.PaymentStatus}");
            house.PaymentStatus = paymentStatus;
        }

        house.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        try
        {
            if (dto.OccupancyStatus != null)
            {
                var flatName = house.Flat?.FlatName ?? "a flat";
                await _notificationService.SendToRolesAsync(
                    new[]
                    {
                        NotificationAudience.SuperAdmin,
                        NotificationAudience.Admin,
                        NotificationAudience.Secretary,
                        NotificationAudience.Accountant
                    },
                    $"House {house.HouseNumber} at {flatName} is now {house.OccupancyStatus}.",
                    "property"
                );
            }

            if (dto.PaymentStatus != null)
            {
                var flatName = house.Flat?.FlatName ?? "a flat";
                await _notificationService.SendToRolesAsync(
                    new[]
                    {
                        NotificationAudience.SuperAdmin,
                        NotificationAudience.Admin,
                        NotificationAudience.Accountant
                    },
                    $"Payment status for House {house.HouseNumber} at {flatName} updated to {house.PaymentStatus}.",
                    "payment"
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification for house update {HouseNumber}", house.HouseNumber);
        }

        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var house = await _context.Houses.FindAsync(id);
        if (house == null) return false;

        house.IsDeleted = true;
        house.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<object>> GetHistoryAsync(Guid houseId)
    {
        return await _context.TenantHouseHistories
            .Where(h => h.HouseId == houseId)
            .OrderByDescending(h => h.AssignedAt)
            .Select(h => (object)new
            {
                h.Id,
                h.TenantId,
                h.TenantFirstName,
                h.TenantLastName,
                h.TenantEmail,
                h.TenantPhone,
                h.HouseNumber,
                h.FlatName,
                h.AssignedAt,
                h.RemovedAt
            })
            .ToListAsync();
    }

    private static object MapToDto(House h) => new
    {
        h.Id,
        h.HouseNumber,
        HouseTypeName = h.HouseTypeRef != null ? h.HouseTypeRef.Name : null,
        h.RentFee,
        h.DepositFee,
        OccupancyStatus = h.OccupancyStatus.ToString(),
        PaymentStatus = h.PaymentStatus.ToString(),
        h.FlatId,
        Flat = h.Flat == null ? null : new { h.Flat.Id, h.Flat.FlatName },
        h.CreatedAt,
        h.UpdatedAt,
        ImagePaths = h.Images.OrderBy(i => i.SortOrder).Select(i => i.ImagePath).ToList()
    };
}
