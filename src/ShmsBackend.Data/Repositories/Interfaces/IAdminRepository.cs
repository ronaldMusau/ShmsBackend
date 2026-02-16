using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Data.Repositories.Interfaces;

public interface IAdminRepository : IRepository<Admin>
{
    Task<Admin?> GetByEmailAsync(string email);
    Task<Admin?> GetByEmailAndTypeAsync(string email, UserType userType);
    Task<bool> EmailExistsAsync(string email);
    Task<bool> EmailExistsForDifferentTypeAsync(string email, UserType userType); // New method
    Task<List<UserType>> GetUserTypesAsync(string email); // Updated to return List
    Task<T?> GetSpecificAdminAsync<T>(Guid id) where T : Admin;
}