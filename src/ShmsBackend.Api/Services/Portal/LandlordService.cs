using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Landlord;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Api.Services.Portal;

public class LandlordService : ILandlordService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LandlordService> _logger;
    private readonly IEmailService _emailService;

    public LandlordService(IUnitOfWork unitOfWork, ILogger<LandlordService> logger, IEmailService emailService)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _emailService = emailService;
    }

    public async Task<Landlord> CreateAsync(CreateLandlordDto dto)
    {
        var existing = await _unitOfWork.Landlords.GetByEmailAsync(dto.Email);
        if (existing != null)
            throw new InvalidOperationException($"Landlord with email {dto.Email} already exists");

        var landlord = new Landlord
        {
            Id = Guid.NewGuid(),
            Email = dto.Email.ToLower().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, 12),
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            PhoneNumber = dto.PhoneNumber,
            NationalId = dto.NationalId,
            AgencyName = dto.AgencyName,
            IsActive = true,
            IsEmailVerified = true,  // Admin-created accounts are email-trusted
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Landlords.AddAsync(landlord);
        await _unitOfWork.SaveChangesAsync();

        var emailSent = await _emailService.SendWelcomeEmailAsync(landlord.Email, landlord.FirstName, dto.Password);
        if (!emailSent)
            _logger.LogError("Failed to send welcome email to Landlord: {Email}", landlord.Email);

        _logger.LogInformation("Landlord created: {Email}", landlord.Email);
        return landlord;
    }

    public async Task<Landlord?> GetByIdAsync(Guid id)
    {
        return await _unitOfWork.Landlords.GetByIdAsync(id);
    }

    public async Task<IEnumerable<Landlord>> GetAllAsync()
    {
        return await _unitOfWork.Landlords.GetAllAsync();
    }

    public async Task<Landlord> UpdateAsync(Guid id, UpdateLandlordDto dto)
    {
        var landlord = await _unitOfWork.Landlords.GetByIdAsync(id);
        if (landlord == null)
            throw new InvalidOperationException("Landlord not found");

        if (!string.IsNullOrEmpty(dto.Email) && dto.Email.ToLower() != landlord.Email)
        {
            var duplicate = await _unitOfWork.Landlords.GetByEmailAsync(dto.Email);
            if (duplicate != null)
                throw new InvalidOperationException($"Email {dto.Email} is already in use");
            landlord.Email = dto.Email.ToLower().Trim();
        }

        if (!string.IsNullOrEmpty(dto.FirstName)) landlord.FirstName = dto.FirstName;
        if (!string.IsNullOrEmpty(dto.LastName)) landlord.LastName = dto.LastName;
        if (!string.IsNullOrEmpty(dto.PhoneNumber)) landlord.PhoneNumber = dto.PhoneNumber;
        if (dto.IsActive.HasValue) landlord.IsActive = dto.IsActive.Value;
        if (!string.IsNullOrEmpty(dto.NationalId)) landlord.NationalId = dto.NationalId;
        if (!string.IsNullOrEmpty(dto.AgencyName)) landlord.AgencyName = dto.AgencyName;

        landlord.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Landlords.UpdateAsync(landlord);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Landlord updated: {Id}", id);
        return landlord;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var landlord = await _unitOfWork.Landlords.GetByIdAsync(id);
        if (landlord == null) return false;

        await _unitOfWork.Landlords.DeleteAsync(landlord);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Landlord deleted: {Id}", id);
        return true;
    }

    public async Task<bool> ToggleStatusAsync(Guid id)
    {
        var landlord = await _unitOfWork.Landlords.GetByIdAsync(id);
        if (landlord == null) return false;

        landlord.IsActive = !landlord.IsActive;
        landlord.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Landlords.UpdateAsync(landlord);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Landlord status toggled: {Id}, IsActive: {IsActive}", id, landlord.IsActive);
        return true;
    }
}
