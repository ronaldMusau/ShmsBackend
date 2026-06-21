using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Data.Repositories;

public class LandlordRepository : Repository<Landlord>, ILandlordRepository
{
    public LandlordRepository(ShmsDbContext context) : base(context)
    {
    }

    public async Task<Landlord?> GetByEmailAsync(string email)
    {
        return await _dbSet
            .FirstOrDefaultAsync(l => l.Email.ToLower() == email.ToLower());
    }
}
