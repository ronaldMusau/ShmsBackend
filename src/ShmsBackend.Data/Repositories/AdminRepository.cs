using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Data.Repositories;

public class AdminRepository : Repository<Admin>, IAdminRepository
{
    public AdminRepository(ShmsDbContext context) : base(context)
    {
    }

    public async Task<Admin?> GetByEmailAsync(string email)
    {
        return await _dbSet
            .FirstOrDefaultAsync(a => a.Email.ToLower() == email.ToLower());
    }

    public async Task<Admin?> GetByEmailAndTypeAsync(string email, UserType userType)
    {
        return await _dbSet
            .FirstOrDefaultAsync(a => a.Email.ToLower() == email.ToLower()
                                    && a.UserType == userType);
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        return await _dbSet.AnyAsync(a => a.Email.ToLower() == email.ToLower());
    }

    public async Task<List<UserType>> GetUserTypesAsync(string email)
    {
        var admin = await GetByEmailAsync(email);
        return admin != null ? new List<UserType> { admin.UserType } : new List<UserType>();
    }

    public async Task<T?> GetSpecificAdminAsync<T>(Guid id) where T : Admin
    {
        return await _context.Set<T>().FindAsync(id);
    }
}