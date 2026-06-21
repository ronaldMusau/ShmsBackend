using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Data.Repositories;

public class TenantRepository : Repository<Tenant>, ITenantRepository
{
    public TenantRepository(ShmsDbContext context) : base(context)
    {
    }

    public async Task<Tenant?> GetByEmailAsync(string email)
    {
        return await _dbSet
            .FirstOrDefaultAsync(t => t.Email.ToLower() == email.ToLower());
    }
}
