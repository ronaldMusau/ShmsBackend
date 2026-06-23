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

    public async Task<IEnumerable<House>> GetByFlatIdAsync(Guid flatId)
    {
        return await _dbSet
            .Where(h => h.FlatId == flatId)
            .ToListAsync();
    }
}
