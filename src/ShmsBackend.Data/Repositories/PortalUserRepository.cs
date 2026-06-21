using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Data.Repositories;

public class PortalUserRepository : Repository<PortalUser>, IPortalUserRepository
{
    public PortalUserRepository(ShmsDbContext context) : base(context)
    {
    }

    public async Task<PortalUser?> GetByEmailAsync(string email)
    {
        return await _dbSet
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
    }

    public async Task<PortalUser?> GetByEmailAndTypeAsync(string email, PortalUserType type)
    {
        return await _dbSet
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower()
                                   && u.PortalUserType == type);
    }

    public async Task<List<PortalUserType>> GetUserTypesAsync(string email)
    {
        return await _dbSet
            .Where(u => u.Email.ToLower() == email.ToLower())
            .Select(u => u.PortalUserType)
            .ToListAsync();
    }

    public async Task<T?> GetSpecificPortalUserAsync<T>(Guid id) where T : PortalUser
    {
        return await _context.Set<T>().FindAsync(id);
    }
}
