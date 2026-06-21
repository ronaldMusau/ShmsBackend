using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Data.Repositories;

public class HouseRepository : Repository<House>, IHouseRepository
{
    public HouseRepository(ShmsDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<House>> GetByLandlordIdAsync(Guid landlordId)
    {
        return await _dbSet
            .Where(h => h.LandlordId == landlordId)
            .ToListAsync();
    }

    public async Task<IEnumerable<House>> GetAvailableAsync()
    {
        return await _dbSet
            .Where(h => h.IsAvailable)
            .ToListAsync();
    }

    public async Task<IEnumerable<House>> GetByCityAsync(string city)
    {
        return await _dbSet
            .Where(h => h.City.ToLower() == city.ToLower())
            .ToListAsync();
    }
}
