using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Data.Repositories;

public class FlatRepository : Repository<Flat>, IFlatRepository
{
    public FlatRepository(ShmsDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Flat>> GetByLandlordIdAsync(Guid landlordId)
    {
        return await _dbSet
            .Where(f => f.LandlordId == landlordId)
            .ToListAsync();
    }
}
