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
        // This now checks if ANY record with this email exists across ALL types
        return await _dbSet.AnyAsync(a => a.Email.ToLower() == email.ToLower());
    }

    public async Task<bool> EmailExistsForDifferentTypeAsync(string email, UserType userType)
    {
        // Check if email exists for a DIFFERENT user type
        return await _dbSet.AnyAsync(a => a.Email.ToLower() == email.ToLower()
                                        && a.UserType != userType);
    }

    public async Task<List<UserType>> GetUserTypesAsync(string email)
    {
        // Get all user types associated with this email
        return await _dbSet
            .Where(a => a.Email.ToLower() == email.ToLower())
            .Select(a => a.UserType)
            .ToListAsync();
    }

    public async Task<T?> GetSpecificAdminAsync<T>(Guid id) where T : Admin
    {
        return await _context.Set<T>().FindAsync(id);
    }
}