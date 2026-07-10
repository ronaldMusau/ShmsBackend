using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Flat;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Portal;

public class FlatService
{
    private readonly ShmsDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly ILogger<FlatService> _logger;

    public FlatService(ShmsDbContext context, INotificationService notificationService, ILogger<FlatService> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<object> CreateAsync(CreateFlatDto dto)
    {
        var exists = await _context.Flats
            .AnyAsync(f => f.FlatName == dto.FlatName);
        if (exists)
            throw new InvalidOperationException($"A flat named '{dto.FlatName}' already exists.");

        var landlord = await _context.Landlords
            .FirstOrDefaultAsync(u => u.Id == dto.LandlordId);
        if (landlord == null)
            throw new InvalidOperationException("Landlord not found.");

        var flat = new Flat
        {
            Id = Guid.NewGuid(),
            FlatName = dto.FlatName,
            County = dto.County,
            Constituency = dto.Constituency,
            Ward = dto.Ward,
            LandlordId = dto.LandlordId,
            RentDueDay = dto.RentDueDay,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var houses = new List<House>();
        if (dto.Houses != null && dto.Houses.Count > 0)
        {
            foreach (var group in dto.Houses)
            {
                if (!Enum.TryParse<HouseType>(group.HouseType, true, out var houseType))
                    throw new InvalidOperationException($"Invalid house type: {group.HouseType}");

                for (int i = 1; i <= group.Count; i++)
                {
                    houses.Add(new House
                    {
                        Id = Guid.NewGuid(),
                        HouseNumber = $"{group.HouseNumberPrefix}{i}",
                        HouseType = houseType,
                        RentFee = group.RentFee,
                        DepositFee = group.DepositFee,
                        OccupancyStatus = OccupancyStatus.Vacant,
                        PaymentStatus = PaymentStatus.NotPaid,
                        FlatId = flat.Id,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }

            var numbers = houses.Select(h => h.HouseNumber).ToList();
            if (numbers.Count != numbers.Distinct().Count())
                throw new InvalidOperationException("Duplicate house numbers detected in the submitted house groups.");
        }

        // Build per-line grouping from the in-memory list before SaveChangesAsync (IDs already assigned)
        var houseGroups = new List<object>();
        if (dto.Houses != null && houses.Count > 0)
        {
            int index = 0;
            foreach (var line in dto.Houses)
            {
                var groupHouseIds = houses.Skip(index).Take(line.Count).Select(h => h.Id).ToList();
                houseGroups.Add(new
                {
                    line.HouseType,
                    line.HouseNumberPrefix,
                    HouseIds = groupHouseIds
                });
                index += line.Count;
            }
        }

        _context.Flats.Add(flat);
        if (houses.Count > 0)
            _context.Houses.AddRange(houses);

        _logger.LogInformation("Creating flat {FlatName} with AgentId: {AgentId}",
            flat.FlatName, dto.AgentId?.ToString() ?? "none");

        if (dto.AgentId.HasValue)
        {
            var agentExists = await _context.Agents
                .AnyAsync(a => a.Id == dto.AgentId.Value && !a.IsDeleted);
            if (!agentExists)
                throw new InvalidOperationException($"Agent with ID {dto.AgentId.Value} not found.");

            var agentFlat = new AgentFlat
            {
                AgentId = dto.AgentId.Value,
                FlatId = flat.Id,
                AssignedAt = DateTime.UtcNow
            };
            await _context.AgentFlats.AddAsync(agentFlat);
        }

        await _context.SaveChangesAsync();

        try
        {
            var houseCount = houses.Count;
            var location = !string.IsNullOrEmpty(flat.Ward) ? flat.Ward : "an unspecified area";
            var houseText = houseCount > 0 ? $" with {houseCount} house{(houseCount == 1 ? "" : "s")}" : "";

            await _notificationService.SendToRolesAsync(
                new[]
                {
                    NotificationAudience.SuperAdmin,
                    NotificationAudience.Admin,
                    NotificationAudience.Secretary,
                    NotificationAudience.Manager,
                    NotificationAudience.Accountant
                },
                $"New flat '{flat.FlatName}' created in {location}{houseText}.",
                "property"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification for flat creation {FlatName}", flat.FlatName);
        }

        try
        {
            await _notificationService.SendToUserAsync(
                dto.LandlordId.ToString(),
                $"A new flat '{flat.FlatName}' has been created and assigned to you.",
                "property");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send flat creation notification to landlord");
        }

        var flatResult = (await GetByIdAsync(flat.Id))!;
        return new { flat = flatResult, houseGroups };
    }

    public async Task<IEnumerable<object>> GetAllAsync()
    {
        return await _context.Flats
            .Include(f => f.Landlord)
            .Include(f => f.Houses)
            .Include(f => f.AgentFlats)
                .ThenInclude(af => af.Agent)
            .Select(f => new
            {
                f.Id,
                f.FlatName,
                f.County,
                f.Constituency,
                f.Ward,
                f.LandlordId,
                Landlord = f.Landlord == null ? null : new
                {
                    f.Landlord.Id,
                    f.Landlord.FirstName,
                    f.Landlord.LastName,
                    f.Landlord.Email,
                    f.Landlord.PhoneNumber
                },
                TotalHouses = f.Houses.Count,
                VacantHouses = f.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Vacant),
                OccupiedHouses = f.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Occupied),
                AgentName = f.AgentFlats
                    .Select(af => af.Agent.FirstName + " " + af.Agent.LastName)
                    .FirstOrDefault(),
                f.CreatedAt,
                f.UpdatedAt
            })
            .ToListAsync<object>();
    }

    public async Task<object?> GetByIdAsync(Guid id)
    {
        var flat = await _context.Flats
            .Include(f => f.Landlord)
            .Include(f => f.Houses)
            .Include(f => f.AgentFlats)
                .ThenInclude(af => af.Agent)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (flat == null) return null;

        return new
        {
            flat.Id,
            flat.FlatName,
            flat.County,
            flat.Constituency,
            flat.Ward,
            flat.LandlordId,
            Landlord = flat.Landlord == null ? null : new
            {
                flat.Landlord.Id,
                flat.Landlord.FirstName,
                flat.Landlord.LastName,
                flat.Landlord.Email,
                flat.Landlord.PhoneNumber
            },
            Houses = flat.Houses.Select(h => new
            {
                h.Id,
                h.HouseNumber,
                HouseType = h.HouseType.ToString(),
                h.RentFee,
                h.DepositFee,
                OccupancyStatus = h.OccupancyStatus.ToString(),
                PaymentStatus = h.PaymentStatus.ToString(),
                h.CreatedAt
            }),
            TotalHouses = flat.Houses.Count,
            VacantHouses = flat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Vacant),
            OccupiedHouses = flat.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Occupied),
            Agents = flat.AgentFlats.Select(af => new
            {
                af.Agent.Id,
                af.Agent.FirstName,
                af.Agent.LastName,
                af.Agent.Email,
                af.Agent.PhoneNumber,
                af.AssignedAt
            }).ToList(),
            flat.CreatedAt,
            flat.UpdatedAt
        };
    }

    public async Task<object?> UpdateAsync(Guid id, UpdateFlatDto dto)
    {
        var flat = await _context.Flats.FindAsync(id);
        if (flat == null) return null;

        if (dto.FlatName != null)
        {
            var duplicate = await _context.Flats
                .AnyAsync(f => f.FlatName == dto.FlatName && f.Id != id);
            if (duplicate)
                throw new InvalidOperationException($"A flat named '{dto.FlatName}' already exists.");
            flat.FlatName = dto.FlatName;
        }
        if (dto.County != null) flat.County = dto.County;
        if (dto.Constituency != null) flat.Constituency = dto.Constituency;
        if (dto.Ward != null) flat.Ward = dto.Ward;

        flat.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        if (dto.AgentId.HasValue)
        {
            var agentExists = await _context.Agents
                .AnyAsync(a => a.Id == dto.AgentId.Value && !a.IsDeleted);
            if (!agentExists)
                throw new InvalidOperationException($"Agent with ID {dto.AgentId.Value} not found.");

            var existing = await _context.AgentFlats
                .Where(af => af.FlatId == flat.Id)
                .ToListAsync();
            _context.AgentFlats.RemoveRange(existing);

            var agentFlat = new AgentFlat
            {
                AgentId = dto.AgentId.Value,
                FlatId = flat.Id,
                AssignedAt = DateTime.UtcNow
            };
            await _context.AgentFlats.AddAsync(agentFlat);
            await _context.SaveChangesAsync();
        }
        else if (dto.AgentId == null)
        {
            var existing = await _context.AgentFlats
                .Where(af => af.FlatId == flat.Id)
                .ToListAsync();
            if (existing.Any())
            {
                _context.AgentFlats.RemoveRange(existing);
                await _context.SaveChangesAsync();
            }
        }

        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var flat = await _context.Flats.FindAsync(id);
        if (flat == null) return false;

        flat.IsDeleted = true;
        flat.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<object>> GetByLandlordAsync(Guid landlordId)
    {
        return await _context.Flats
            .Include(f => f.Houses)
            .Where(f => f.LandlordId == landlordId)
            .Select(f => new
            {
                f.Id,
                f.FlatName,
                f.County,
                f.Constituency,
                f.Ward,
                TotalHouses = f.Houses.Count,
                VacantHouses = f.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Vacant),
                OccupiedHouses = f.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Occupied),
                f.CreatedAt
            })
            .ToListAsync<object>();
    }

    public async Task<IEnumerable<object>> GetByLocationAsync(string county, string constituency, string ward)
    {
        return await _context.Flats
            .Include(f => f.Houses)
            .Where(f => f.County == county
                     && f.Constituency == constituency
                     && f.Ward == ward)
            .Select(f => new
            {
                f.Id,
                f.FlatName,
                f.County,
                f.Constituency,
                f.Ward,
                TotalHouses = f.Houses.Count,
                VacantHouses = f.Houses.Count(h => h.OccupancyStatus == OccupancyStatus.Vacant),
                f.CreatedAt
            })
            .ToListAsync<object>();
    }
}
