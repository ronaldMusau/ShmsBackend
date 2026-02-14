using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShmsBackend.Api.Models.DTOs.User;
using ShmsBackend.Data.Enums;
using AdminEntity = ShmsBackend.Data.Models.Entities.Admin;

namespace ShmsBackend.Api.Services.User;

public interface IUserService
{
    // Create - Only SuperAdmin/Admin (controlled in controller)
    Task<AdminEntity> CreateUserAsync(CreateUserDto createUserDto, Guid createdBy);

    // Read
    Task<AdminEntity?> GetUserByIdAsync(Guid id);
    Task<AdminEntity?> GetUserByEmailAsync(string email);
    Task<IEnumerable<AdminEntity>> GetAllUsersAsync();

    // Update
    Task<AdminEntity> UpdateUserAsync(Guid id, UpdateUserDto updateUserDto);

    // Delete - Only SuperAdmin
    Task<bool> DeleteUserAsync(Guid id);

    // Status - Only SuperAdmin
    Task<bool> ToggleUserStatusAsync(Guid id);

    // Helper
    Task<UserType> GetUserTypeAsync(Guid userId);
}