using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Flat;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Portal;

public class FlatService
{
    private readonly ShmsDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly IEmailService _emailService;
    private readonly ILogger<FlatService> _logger;

    public FlatService(ShmsDbContext context, INotificationService notificationService, IEmailService emailService, ILogger<FlatService> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _emailService = emailService;
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
            BillableGracePeriodMonths = dto.BillableGracePeriodMonths,
            VacateNoticeDeadlineDay = dto.VacateNoticeDeadlineDay,
            SitDeposit = dto.SitDeposit,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var houses = new List<House>();
        if (dto.Houses != null && dto.Houses.Count > 0)
        {
            var houseTypeIds = dto.Houses.Select(g => g.HouseTypeId).Distinct().ToList();
            var validHouseTypeIds = await _context.HouseTypes
                .Where(t => houseTypeIds.Contains(t.Id) && t.IsActive)
                .Select(t => t.Id)
                .ToListAsync();

            foreach (var group in dto.Houses)
            {
                if (!validHouseTypeIds.Contains(group.HouseTypeId))
                    throw new InvalidOperationException($"Invalid house type.");

                for (int i = 1; i <= group.Count; i++)
                {
                    houses.Add(new House
                    {
                        Id = Guid.NewGuid(),
                        HouseNumber = $"{group.HouseNumberPrefix}{i}",
                        HouseTypeId = group.HouseTypeId,
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
                    HouseTypeId = line.HouseTypeId,
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
                "property", "Flat", flat.Id.ToString()
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
                "property", "Flat", flat.Id.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send flat creation notification to landlord");
        }

        try
        {
            await _emailService.SendFlatCreatedLandlordEmailAsync(
                landlord.Email, landlord.FirstName, flat.FlatName, houses.Count, landlord.Id.ToString(), true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send flat creation email to landlord");
        }

        if (dto.AgentId.HasValue)
        {
            try
            {
                var agent = await _context.Agents.FindAsync(dto.AgentId.Value);
                if (agent != null)
                {
                    await _emailService.SendFlatAssignedAgentEmailAsync(agent.Email, agent.FirstName, flat.FlatName, agent.Id.ToString(), true);
                    await _notificationService.SendToUserAsync(
                        agent.Id.ToString(),
                        $"A new flat '{flat.FlatName}' has been added for you to manage.",
                        "property", "Flat", flat.Id.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send flat assignment notification to agent");
            }
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
                HasPhotos = f.Houses.Any(h => h.Images.Any()),
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
                .ThenInclude(h => h.Images)
            .Include(f => f.Houses)
                .ThenInclude(h => h.HouseTypeRef)
            .Include(f => f.AgentFlats)
                .ThenInclude(af => af.Agent)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (flat == null) return null;

        var houseIds = flat.Houses.Select(h => h.Id).ToList();
        var everOccupiedSet = new HashSet<Guid>(
            await _context.TenantHouseHistories
                .Where(th => houseIds.Contains(th.HouseId))
                .Select(th => th.HouseId)
                .Distinct()
                .ToListAsync()
        );

        var pendingRentChangesByHouse = (await _context.PendingRentChanges
                .Where(pc => houseIds.Contains(pc.HouseId) && pc.AppliedAt == null)
                .ToListAsync())
            .GroupBy(pc => pc.HouseId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(pc => pc.CreatedAt).First());

        return new
        {
            flat.Id,
            flat.FlatName,
            flat.County,
            flat.Constituency,
            flat.Ward,
            flat.GoogleMapsLink,
            flat.RentDueDay,
            flat.BillableGracePeriodMonths,
            flat.VacateNoticeDeadlineDay,
            flat.SitDeposit,
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
                h.HouseTypeId,
                HouseTypeName = h.HouseTypeRef != null ? h.HouseTypeRef.Name : null,
                h.RentFee,
                h.DepositFee,
                OccupancyStatus = h.OccupancyStatus.ToString(),
                PaymentStatus = h.PaymentStatus.ToString(),
                h.CreatedAt,
                Images = h.Images.OrderBy(hi => hi.SortOrder).Select(hi => new { hi.Id, hi.ImagePath }).ToList(),
                EverOccupied = everOccupiedSet.Contains(h.Id),
                ScheduledRentChange = pendingRentChangesByHouse.TryGetValue(h.Id, out var prc) ? new
                {
                    prc.NewRentFee,
                    prc.NewDepositFee,
                    prc.EffectiveMonth,
                    prc.EffectiveYear
                } : null
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
            HasPendingEditRequest = _context.FlatEditRequests.Any(r => r.FlatId == flat.Id && r.Status == "Pending"),
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
        flat.RentDueDay = dto.RentDueDay;
        flat.BillableGracePeriodMonths = dto.BillableGracePeriodMonths;
        flat.VacateNoticeDeadlineDay = dto.VacateNoticeDeadlineDay;
        flat.SitDeposit = dto.SitDeposit;

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
        else if (dto.ClearAgent)
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

    public async Task<object?> AddHouseLinesAsync(Guid flatId, List<HouseGroupDto> houseLines)
    {
        var flat = await _context.Flats.FindAsync(flatId);
        if (flat == null) return null;

        var houseTypeIds = houseLines.Select(g => g.HouseTypeId).Distinct().ToList();
        var validHouseTypeIds = await _context.HouseTypes
            .Where(t => houseTypeIds.Contains(t.Id) && t.IsActive)
            .Select(t => t.Id)
            .ToListAsync();

        var existingHouseTypeIds = await _context.Houses
            .Where(h => h.FlatId == flatId)
            .Select(h => h.HouseTypeId)
            .Distinct()
            .ToListAsync();

        foreach (var group in houseLines)
        {
            if (existingHouseTypeIds.Contains(group.HouseTypeId))
            {
                var typeName = await _context.HouseTypes
                    .Where(t => t.Id == group.HouseTypeId)
                    .Select(t => t.Name)
                    .FirstOrDefaultAsync();
                throw new InvalidOperationException($"House type '{typeName ?? group.HouseTypeId.ToString()}' already exists on this flat. Use increase-count instead.");
            }
        }

        var houses = new List<House>();
        foreach (var group in houseLines)
        {
            if (!validHouseTypeIds.Contains(group.HouseTypeId))
                throw new InvalidOperationException($"Invalid house type.");

            var startIndex = await GetNextIndexForPrefix(flatId, group.HouseNumberPrefix);
            for (int i = startIndex; i < startIndex + group.Count; i++)
            {
                houses.Add(new House
                {
                    Id = Guid.NewGuid(),
                    HouseNumber = $"{group.HouseNumberPrefix}{i}",
                    HouseTypeId = group.HouseTypeId,
                    RentFee = group.RentFee,
                    DepositFee = group.DepositFee,
                    OccupancyStatus = OccupancyStatus.Vacant,
                    PaymentStatus = PaymentStatus.NotPaid,
                    FlatId = flatId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        var numbers = houses.Select(h => h.HouseNumber).ToList();
        if (numbers.Count != numbers.Distinct().Count())
            throw new InvalidOperationException("Duplicate house numbers detected in the submitted house groups.");

        _context.Houses.AddRange(houses);
        await _context.SaveChangesAsync();

        var houseGroups = new List<object>();
        int index = 0;
        foreach (var line in houseLines)
        {
            var groupHouseIds = houses.Skip(index).Take(line.Count).Select(h => h.Id).ToList();
            houseGroups.Add(new
            {
                HouseTypeId = line.HouseTypeId,
                line.HouseNumberPrefix,
                HouseIds = groupHouseIds
            });
            index += line.Count;
        }

        return houseGroups;
    }

    public async Task<object?> IncreaseHouseCountAsync(Guid flatId, Guid houseTypeId, int additionalCount)
    {
        var flat = await _context.Flats.FindAsync(flatId);
        if (flat == null) return null;

        var existingHouses = await _context.Houses
            .Where(h => h.FlatId == flatId && h.HouseTypeId == houseTypeId)
            .ToListAsync();

        if (existingHouses.Count == 0)
            throw new InvalidOperationException("No houses of this type exist on this flat to increase.");

        var sample = existingHouses.First();
        var prefix = SplitHouseNumber(sample.HouseNumber).Prefix;
        var startIndex = await GetNextIndexForPrefix(flatId, prefix);

        var newHouses = new List<House>();
        for (int i = startIndex; i < startIndex + additionalCount; i++)
        {
            newHouses.Add(new House
            {
                Id = Guid.NewGuid(),
                HouseNumber = $"{prefix}{i}",
                HouseTypeId = houseTypeId,
                RentFee = sample.RentFee,
                DepositFee = sample.DepositFee,
                OccupancyStatus = OccupancyStatus.Vacant,
                PaymentStatus = PaymentStatus.NotPaid,
                FlatId = flatId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        _context.Houses.AddRange(newHouses);
        await _context.SaveChangesAsync();

        return newHouses.Select(h => new { h.Id, h.HouseNumber }).ToList();
    }

    public async Task<object?> EditHouseGroupAsync(Guid flatId, Guid houseTypeId, EditHouseGroupDto dto)
    {
        var flat = await _context.Flats.FindAsync(flatId);
        if (flat == null) return null;

        var existingHouses = await _context.Houses
            .Where(h => h.FlatId == flatId && h.HouseTypeId == houseTypeId)
            .ToListAsync();

        if (existingHouses.Count == 0)
            throw new InvalidOperationException("No houses of this type exist on this flat.");

        var houseIds = existingHouses.Select(h => h.Id).ToList();
        var everOccupied = await _context.TenantHouseHistories.AnyAsync(h => houseIds.Contains(h.HouseId));
        if (everOccupied)
            throw new InvalidOperationException("This house type has occupied units and cannot be fully edited — only count can be increased.");

        var currentCount = existingHouses.Count;

        if (dto.NewCount < currentCount)
        {
            var toRemove = currentCount - dto.NewCount;
            var removals = existingHouses
                .OrderByDescending(h => SplitHouseNumber(h.HouseNumber).Suffix)
                .Take(toRemove)
                .ToList();
            _context.Houses.RemoveRange(removals);
            foreach (var h in removals)
                existingHouses.Remove(h);
        }

        foreach (var house in existingHouses)
        {
            var suffix = SplitHouseNumber(house.HouseNumber).Suffix;
            house.HouseNumber = $"{dto.NewPrefix}{suffix}";
            house.RentFee = dto.NewRentFee;
            house.DepositFee = dto.NewDepositFee;
            house.UpdatedAt = DateTime.UtcNow;
        }

        // Save removals + re-prefixed houses first so the next-index lookup below reflects
        // the final state and doesn't hand out a number that collides with a just-renamed house.
        await _context.SaveChangesAsync();

        var newHouses = new List<House>();
        if (dto.NewCount > currentCount)
        {
            var toAdd = dto.NewCount - currentCount;
            var startIndex = await GetNextIndexForPrefix(flatId, dto.NewPrefix);
            for (int i = startIndex; i < startIndex + toAdd; i++)
            {
                newHouses.Add(new House
                {
                    Id = Guid.NewGuid(),
                    HouseNumber = $"{dto.NewPrefix}{i}",
                    HouseTypeId = houseTypeId,
                    RentFee = dto.NewRentFee,
                    DepositFee = dto.NewDepositFee,
                    OccupancyStatus = OccupancyStatus.Vacant,
                    PaymentStatus = PaymentStatus.NotPaid,
                    FlatId = flatId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            _context.Houses.AddRange(newHouses);
            await _context.SaveChangesAsync();
        }

        return existingHouses.Concat(newHouses)
            .Select(h => new { h.Id, h.HouseNumber })
            .ToList();
    }

    public async Task<bool?> DeleteHouseGroupAsync(Guid flatId, Guid houseTypeId)
    {
        var flat = await _context.Flats.FindAsync(flatId);
        if (flat == null) return null;

        var existingHouses = await _context.Houses
            .Where(h => h.FlatId == flatId && h.HouseTypeId == houseTypeId)
            .ToListAsync();

        if (existingHouses.Count == 0)
            throw new InvalidOperationException("No houses of this type exist on this flat.");

        var houseIds = existingHouses.Select(h => h.Id).ToList();
        var everOccupied = await _context.TenantHouseHistories.AnyAsync(h => houseIds.Contains(h.HouseId));
        if (everOccupied)
            throw new InvalidOperationException("This house type has occupied units and cannot be deleted — only unoccupied groups can be removed.");

        _context.Houses.RemoveRange(existingHouses);
        await _context.SaveChangesAsync();
        return true;
    }

    private async Task<int> GetNextIndexForPrefix(Guid flatId, string prefix)
    {
        var existingNumbers = await _context.Houses
            .Where(h => h.FlatId == flatId && h.HouseNumber.StartsWith(prefix))
            .Select(h => h.HouseNumber)
            .ToListAsync();

        int maxIndex = 0;
        foreach (var number in existingNumbers)
        {
            var suffix = number.Substring(prefix.Length);
            if (int.TryParse(suffix, out var idx) && idx > maxIndex)
                maxIndex = idx;
        }
        return maxIndex + 1;
    }

    private static (string Prefix, int Suffix) SplitHouseNumber(string houseNumber)
    {
        int i = houseNumber.Length;
        while (i > 0 && char.IsDigit(houseNumber[i - 1])) i--;
        var prefix = houseNumber.Substring(0, i);
        int.TryParse(houseNumber.Substring(i), out var suffix);
        return (prefix, suffix);
    }
}
