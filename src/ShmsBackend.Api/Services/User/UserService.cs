using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BCrypt.Net;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.User;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Repositories.Interfaces;
using AdminEntity = ShmsBackend.Data.Models.Entities.Admin;

namespace ShmsBackend.Api.Services.User;

public class UserService : IUserService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _emailService;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUnitOfWork unitOfWork,
        IEmailService emailService,
        ILogger<UserService> logger)
    {
        _unitOfWork = unitOfWork;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<AdminEntity> CreateUserAsync(CreateUserDto createUserDto, Guid createdBy)
    {
        // Check if this email + userType combination already exists
        var existingUser = await _unitOfWork.Admins.GetByEmailAndTypeAsync(
            createUserDto.Email,
            createUserDto.UserType);

        if (existingUser != null)
        {
            throw new InvalidOperationException($"User with email {createUserDto.Email} and role {createUserDto.UserType} already exists");
        }

        // Generate a temporary password (not sent via email)
        var temporaryPassword = GenerateTemporaryPassword();
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword);

        // Generate email verification token
        var verificationToken = GenerateVerificationToken();

        // Create the appropriate admin type based on UserType
        AdminEntity admin = createUserDto.UserType switch
        {
            UserType.SuperAdmin => new SuperAdmin
            {
                Id = Guid.NewGuid(),
                Email = createUserDto.Email.ToLower(),
                PasswordHash = passwordHash,
                FirstName = createUserDto.FirstName,
                LastName = createUserDto.LastName,
                PhoneNumber = createUserDto.PhoneNumber,
                IsActive = true,
                IsEmailVerified = false,
                EmailVerificationToken = verificationToken,
                EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24),
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                UserType = UserType.SuperAdmin,
                SuperAdminPermissions = "full_access"
            },

            UserType.Admin => new AdminUser
            {
                Id = Guid.NewGuid(),
                Email = createUserDto.Email.ToLower(),
                PasswordHash = passwordHash,
                FirstName = createUserDto.FirstName,
                LastName = createUserDto.LastName,
                PhoneNumber = createUserDto.PhoneNumber,
                IsActive = true,
                IsEmailVerified = false,
                EmailVerificationToken = verificationToken,
                EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24),
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                UserType = UserType.Admin,
                Department = createUserDto.Department
            },

            UserType.Manager => new Manager
            {
                Id = Guid.NewGuid(),
                Email = createUserDto.Email.ToLower(),
                PasswordHash = passwordHash,
                FirstName = createUserDto.FirstName,
                LastName = createUserDto.LastName,
                PhoneNumber = createUserDto.PhoneNumber,
                IsActive = true,
                IsEmailVerified = false,
                EmailVerificationToken = verificationToken,
                EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24),
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                UserType = UserType.Manager,
                ManagedDepartment = createUserDto.ManagedDepartment,
                TeamSize = createUserDto.TeamSize ?? 0
            },

            UserType.Accountant => new Accountant
            {
                Id = Guid.NewGuid(),
                Email = createUserDto.Email.ToLower(),
                PasswordHash = passwordHash,
                FirstName = createUserDto.FirstName,
                LastName = createUserDto.LastName,
                PhoneNumber = createUserDto.PhoneNumber,
                IsActive = true,
                IsEmailVerified = false,
                EmailVerificationToken = verificationToken,
                EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24),
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                UserType = UserType.Accountant,
                LicenseNumber = createUserDto.LicenseNumber
            },

            UserType.Secretary => new Secretary
            {
                Id = Guid.NewGuid(),
                Email = createUserDto.Email.ToLower(),
                PasswordHash = passwordHash,
                FirstName = createUserDto.FirstName,
                LastName = createUserDto.LastName,
                PhoneNumber = createUserDto.PhoneNumber,
                IsActive = true,
                IsEmailVerified = false,
                EmailVerificationToken = verificationToken,
                EmailVerificationTokenExpiry = DateTime.UtcNow.AddHours(24),
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                UserType = UserType.Secretary,
                OfficeNumber = createUserDto.OfficeNumber
            },

            _ => throw new ArgumentException("Invalid user type")
        };

        await _unitOfWork.Admins.AddAsync(admin);
        await _unitOfWork.SaveChangesAsync();

        // Send verification email (not the password!)
        var verificationLink = $"https://your-frontend.com/verify-email?token={verificationToken}&email={admin.Email}";
        await _emailService.SendEmailVerificationEmailAsync(
            admin.Email,
            admin.FirstName,
            verificationLink
        );

        _logger.LogInformation("User created successfully: {Email} as {UserType}. Verification email sent.",
            admin.Email, admin.UserType);

        return admin;
    }

    public async Task<AdminEntity?> GetUserByIdAsync(Guid id)
    {
        return await _unitOfWork.Admins.GetByIdAsync(id);
    }

    public async Task<AdminEntity?> GetUserByEmailAsync(string email)
    {
        return await _unitOfWork.Admins.GetByEmailAsync(email);
    }

    public async Task<IEnumerable<AdminEntity>> GetAllUsersAsync()
    {
        return await _unitOfWork.Admins.GetAllAsync();
    }

    public async Task<AdminEntity> UpdateUserAsync(Guid id, UpdateUserDto updateUserDto)
    {
        var user = await _unitOfWork.Admins.GetByIdAsync(id);
        if (user == null)
        {
            throw new InvalidOperationException("User not found");
        }

        // Email uniqueness check - only check if email is being updated
        if (!string.IsNullOrEmpty(updateUserDto.Email) && updateUserDto.Email.ToLower() != user.Email.ToLower())
        {
            // Check if this email + userType combination exists for another user
            var existingUser = await _unitOfWork.Admins.ExistsAsync(
                a => a.Email.ToLower() == updateUserDto.Email.ToLower()
                     && a.UserType == user.UserType
                     && a.Id != id);

            if (existingUser)
            {
                throw new InvalidOperationException(
                    $"Email {updateUserDto.Email} is already in use for role {user.UserType}");
            }
            user.Email = updateUserDto.Email.ToLower();
        }

        // Basic info updates
        if (!string.IsNullOrEmpty(updateUserDto.FirstName))
            user.FirstName = updateUserDto.FirstName;

        if (!string.IsNullOrEmpty(updateUserDto.LastName))
            user.LastName = updateUserDto.LastName;

        if (!string.IsNullOrEmpty(updateUserDto.PhoneNumber))
            user.PhoneNumber = updateUserDto.PhoneNumber;

        if (updateUserDto.IsActive.HasValue)
            user.IsActive = updateUserDto.IsActive.Value;

        // Update UserType if provided (only SuperAdmin can do this)
        if (updateUserDto.UserType.HasValue && updateUserDto.UserType.Value != user.UserType)
        {
            _logger.LogWarning("User type changing from {OldType} to {NewType} for user {UserId}",
                user.UserType, updateUserDto.UserType.Value, id);
            user.UserType = updateUserDto.UserType.Value;
        }

        // Role-specific updates based on user type
        switch (user.UserType)
        {
            case UserType.SuperAdmin:
                await UpdateSuperAdminSpecificFields(id, updateUserDto);
                break;

            case UserType.Admin:
                await UpdateAdminSpecificFields(id, updateUserDto);
                break;

            case UserType.Manager:
                await UpdateManagerSpecificFields(id, updateUserDto);
                break;

            case UserType.Accountant:
                await UpdateAccountantSpecificFields(id, updateUserDto);
                break;

            case UserType.Secretary:
                await UpdateSecretarySpecificFields(id, updateUserDto);
                break;
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Admins.UpdateAsync(user);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("User updated successfully: {Id}", id);
        return user;
    }

    #region Role-Specific Update Methods

    private async Task UpdateSuperAdminSpecificFields(Guid id, UpdateUserDto updateUserDto)
    {
        var superAdmin = await _unitOfWork.Admins.GetSpecificAdminAsync<SuperAdmin>(id);
        if (superAdmin != null)
        {
            // SuperAdmin specific fields can be updated here
            // For now, no specific fields
            await _unitOfWork.Admins.UpdateAsync(superAdmin);
            _logger.LogDebug("SuperAdmin specific fields updated for {Id}", id);
        }
    }

    private async Task UpdateAdminSpecificFields(Guid id, UpdateUserDto updateUserDto)
    {
        var admin = await _unitOfWork.Admins.GetSpecificAdminAsync<AdminUser>(id);
        if (admin != null)
        {
            if (!string.IsNullOrEmpty(updateUserDto.Department))
            {
                admin.Department = updateUserDto.Department;
            }
            await _unitOfWork.Admins.UpdateAsync(admin);
            _logger.LogDebug("Admin specific fields updated for {Id}", id);
        }
    }

    private async Task UpdateManagerSpecificFields(Guid id, UpdateUserDto updateUserDto)
    {
        var manager = await _unitOfWork.Admins.GetSpecificAdminAsync<Manager>(id);
        if (manager != null)
        {
            bool updated = false;

            if (!string.IsNullOrEmpty(updateUserDto.ManagedDepartment))
            {
                manager.ManagedDepartment = updateUserDto.ManagedDepartment;
                updated = true;
            }

            if (updateUserDto.TeamSize.HasValue)
            {
                manager.TeamSize = updateUserDto.TeamSize.Value;
                updated = true;
            }

            if (updated)
            {
                await _unitOfWork.Admins.UpdateAsync(manager);
                _logger.LogDebug("Manager specific fields updated for {Id}", id);
            }
        }
    }

    private async Task UpdateAccountantSpecificFields(Guid id, UpdateUserDto updateUserDto)
    {
        var accountant = await _unitOfWork.Admins.GetSpecificAdminAsync<Accountant>(id);
        if (accountant != null)
        {
            if (!string.IsNullOrEmpty(updateUserDto.LicenseNumber))
            {
                accountant.LicenseNumber = updateUserDto.LicenseNumber;
                await _unitOfWork.Admins.UpdateAsync(accountant);
                _logger.LogDebug("Accountant specific fields updated for {Id}", id);
            }
        }
    }

    private async Task UpdateSecretarySpecificFields(Guid id, UpdateUserDto updateUserDto)
    {
        var secretary = await _unitOfWork.Admins.GetSpecificAdminAsync<Secretary>(id);
        if (secretary != null)
        {
            if (!string.IsNullOrEmpty(updateUserDto.OfficeNumber))
            {
                secretary.OfficeNumber = updateUserDto.OfficeNumber;
                await _unitOfWork.Admins.UpdateAsync(secretary);
                _logger.LogDebug("Secretary specific fields updated for {Id}", id);
            }
        }
    }

    #endregion

    public async Task<bool> DeleteUserAsync(Guid id)
    {
        var user = await _unitOfWork.Admins.GetByIdAsync(id);
        if (user == null)
        {
            return false;
        }

        // Prevent deletion of SuperAdmin
        if (user.UserType == UserType.SuperAdmin)
        {
            throw new InvalidOperationException("Cannot delete super admin");
        }

        // Delete from child table first (cascade should handle this, but being explicit)
        switch (user.UserType)
        {
            case UserType.Admin:
                var admin = await _unitOfWork.Admins.GetSpecificAdminAsync<AdminUser>(id);
                if (admin != null)
                    await _unitOfWork.Admins.DeleteAsync(admin);
                break;

            case UserType.Manager:
                var manager = await _unitOfWork.Admins.GetSpecificAdminAsync<Manager>(id);
                if (manager != null)
                    await _unitOfWork.Admins.DeleteAsync(manager);
                break;

            case UserType.Accountant:
                var accountant = await _unitOfWork.Admins.GetSpecificAdminAsync<Accountant>(id);
                if (accountant != null)
                    await _unitOfWork.Admins.DeleteAsync(accountant);
                break;

            case UserType.Secretary:
                var secretary = await _unitOfWork.Admins.GetSpecificAdminAsync<Secretary>(id);
                if (secretary != null)
                    await _unitOfWork.Admins.DeleteAsync(secretary);
                break;
        }

        // Delete from base table
        await _unitOfWork.Admins.DeleteAsync(user);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("User deleted successfully: {Id}", id);
        return true;
    }

    public async Task<bool> ToggleUserStatusAsync(Guid id)
    {
        var user = await _unitOfWork.Admins.GetByIdAsync(id);
        if (user == null)
        {
            return false;
        }

        // Prevent disabling SuperAdmin
        if (user.UserType == UserType.SuperAdmin)
        {
            throw new InvalidOperationException("Cannot disable super admin");
        }

        user.IsActive = !user.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Admins.UpdateAsync(user);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("User status toggled: {Id}, IsActive: {IsActive}", id, user.IsActive);
        return true;
    }

    public async Task<UserType> GetUserTypeAsync(Guid userId)
    {
        var user = await _unitOfWork.Admins.GetByIdAsync(userId);
        if (user == null)
        {
            throw new InvalidOperationException("User not found");
        }
        return user.UserType;
    }

    private string GenerateTemporaryPassword()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, 12)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private string GenerateVerificationToken()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("/", "_")
            .Replace("+", "-")
            .TrimEnd('=');
    }
}