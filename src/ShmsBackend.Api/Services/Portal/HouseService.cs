using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.House;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Api.Services.Portal;

public class HouseService : IHouseService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<HouseService> _logger;

    public HouseService(IUnitOfWork unitOfWork, ILogger<HouseService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<House> CreateAsync(CreateHouseDto dto)
    {
        var landlordExists = await _unitOfWork.Landlords.GetByIdAsync(dto.LandlordId);
        if (landlordExists == null)
            throw new InvalidOperationException($"Landlord with id {dto.LandlordId} not found");

        if (dto.AgentId.HasValue)
        {
            var agentExists = await _unitOfWork.Agents.GetByIdAsync(dto.AgentId.Value);
            if (agentExists == null)
                throw new InvalidOperationException($"Agent with id {dto.AgentId} not found");
        }

        var house = new House
        {
            Id = Guid.NewGuid(),
            Title = dto.Title,
            Description = dto.Description,
            Address = dto.Address,
            City = dto.City,
            State = dto.State,
            ZipCode = dto.ZipCode,
            Price = dto.Price,
            Bedrooms = dto.Bedrooms,
            Bathrooms = dto.Bathrooms,
            IsAvailable = true,
            LandlordId = dto.LandlordId,
            AgentId = dto.AgentId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Houses.AddAsync(house);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("House created: {Id}", house.Id);
        return house;
    }

    public async Task<House?> GetByIdAsync(Guid id)
    {
        return await _unitOfWork.Houses.GetByIdAsync(id);
    }

    public async Task<IEnumerable<House>> GetAllAsync()
    {
        return await _unitOfWork.Houses.GetAllAsync();
    }

    public async Task<IEnumerable<House>> GetByLandlordAsync(Guid landlordId)
    {
        return await _unitOfWork.Houses.GetByLandlordIdAsync(landlordId);
    }

    public async Task<IEnumerable<House>> GetAvailableAsync()
    {
        return await _unitOfWork.Houses.GetAvailableAsync();
    }

    public async Task<IEnumerable<House>> GetByCityAsync(string city)
    {
        return await _unitOfWork.Houses.GetByCityAsync(city);
    }

    public async Task<House> UpdateAsync(Guid id, UpdateHouseDto dto)
    {
        var house = await _unitOfWork.Houses.GetByIdAsync(id);
        if (house == null)
            throw new InvalidOperationException("House not found");

        if (dto.AgentId.HasValue)
        {
            var agentExists = await _unitOfWork.Agents.GetByIdAsync(dto.AgentId.Value);
            if (agentExists == null)
                throw new InvalidOperationException($"Agent with id {dto.AgentId} not found");
        }

        if (!string.IsNullOrEmpty(dto.Title)) house.Title = dto.Title;
        if (dto.Description != null) house.Description = dto.Description;
        if (!string.IsNullOrEmpty(dto.Address)) house.Address = dto.Address;
        if (!string.IsNullOrEmpty(dto.City)) house.City = dto.City;
        if (dto.State != null) house.State = dto.State;
        if (dto.ZipCode != null) house.ZipCode = dto.ZipCode;
        if (dto.Price.HasValue) house.Price = dto.Price.Value;
        if (dto.Bedrooms.HasValue) house.Bedrooms = dto.Bedrooms.Value;
        if (dto.Bathrooms.HasValue) house.Bathrooms = dto.Bathrooms.Value;
        if (dto.IsAvailable.HasValue) house.IsAvailable = dto.IsAvailable.Value;
        if (dto.AgentId.HasValue) house.AgentId = dto.AgentId;

        house.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Houses.UpdateAsync(house);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("House updated: {Id}", id);
        return house;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var house = await _unitOfWork.Houses.GetByIdAsync(id);
        if (house == null) return false;

        await _unitOfWork.Houses.DeleteAsync(house);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("House deleted: {Id}", id);
        return true;
    }
}
