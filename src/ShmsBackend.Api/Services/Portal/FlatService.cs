using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Models.DTOs.Flat;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Api.Services.Portal;

public class FlatService : IFlatService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<FlatService> _logger;

    public FlatService(IUnitOfWork unitOfWork, ILogger<FlatService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Flat> CreateAsync(CreateFlatDto dto)
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

        if (dto.HouseId.HasValue)
        {
            var houseExists = await _unitOfWork.Houses.GetByIdAsync(dto.HouseId.Value);
            if (houseExists == null)
                throw new InvalidOperationException($"House with id {dto.HouseId} not found");
        }

        var flat = new Flat
        {
            Id = Guid.NewGuid(),
            Title = dto.Title,
            Description = dto.Description,
            Address = dto.Address,
            City = dto.City,
            County = dto.County,
            Constituency = dto.Constituency,
            Ward = dto.Ward,
            State = dto.State,
            ZipCode = dto.ZipCode,
            Price = dto.Price,
            FloorNumber = dto.FloorNumber,
            Bedrooms = dto.Bedrooms,
            Bathrooms = dto.Bathrooms,
            IsAvailable = true,
            HouseId = dto.HouseId,
            LandlordId = dto.LandlordId,
            AgentId = dto.AgentId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Flats.AddAsync(flat);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Flat created: {Id}", flat.Id);
        return flat;
    }

    public async Task<Flat?> GetByIdAsync(Guid id)
    {
        return await _unitOfWork.Flats.GetByIdAsync(id);
    }

    public async Task<IEnumerable<Flat>> GetAllAsync()
    {
        return await _unitOfWork.Flats.GetAllAsync();
    }

    public async Task<IEnumerable<Flat>> GetByLandlordAsync(Guid landlordId)
    {
        return await _unitOfWork.Flats.GetByLandlordIdAsync(landlordId);
    }

    public async Task<IEnumerable<Flat>> GetByHouseAsync(Guid houseId)
    {
        return await _unitOfWork.Flats.GetByHouseIdAsync(houseId);
    }

    public async Task<IEnumerable<Flat>> GetAvailableAsync()
    {
        return await _unitOfWork.Flats.GetAvailableAsync();
    }

    public async Task<IEnumerable<Flat>> GetByCityAsync(string city)
    {
        return await _unitOfWork.Flats.GetByCityAsync(city);
    }

    public async Task<Flat> UpdateAsync(Guid id, UpdateFlatDto dto)
    {
        var flat = await _unitOfWork.Flats.GetByIdAsync(id);
        if (flat == null)
            throw new InvalidOperationException("Flat not found");

        if (dto.AgentId.HasValue)
        {
            var agentExists = await _unitOfWork.Agents.GetByIdAsync(dto.AgentId.Value);
            if (agentExists == null)
                throw new InvalidOperationException($"Agent with id {dto.AgentId} not found");
        }

        if (dto.HouseId.HasValue)
        {
            var houseExists = await _unitOfWork.Houses.GetByIdAsync(dto.HouseId.Value);
            if (houseExists == null)
                throw new InvalidOperationException($"House with id {dto.HouseId} not found");
        }

        if (!string.IsNullOrEmpty(dto.Title)) flat.Title = dto.Title;
        if (dto.Description != null) flat.Description = dto.Description;
        if (!string.IsNullOrEmpty(dto.Address)) flat.Address = dto.Address;
        if (!string.IsNullOrEmpty(dto.City)) flat.City = dto.City;
        if (dto.County != null) flat.County = dto.County;
        if (dto.Constituency != null) flat.Constituency = dto.Constituency;
        if (dto.Ward != null) flat.Ward = dto.Ward;
        if (dto.State != null) flat.State = dto.State;
        if (dto.ZipCode != null) flat.ZipCode = dto.ZipCode;
        if (dto.Price.HasValue) flat.Price = dto.Price.Value;
        if (dto.FloorNumber.HasValue) flat.FloorNumber = dto.FloorNumber.Value;
        if (dto.Bedrooms.HasValue) flat.Bedrooms = dto.Bedrooms.Value;
        if (dto.Bathrooms.HasValue) flat.Bathrooms = dto.Bathrooms.Value;
        if (dto.IsAvailable.HasValue) flat.IsAvailable = dto.IsAvailable.Value;
        if (dto.HouseId.HasValue) flat.HouseId = dto.HouseId;
        if (dto.AgentId.HasValue) flat.AgentId = dto.AgentId;

        flat.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Flats.UpdateAsync(flat);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Flat updated: {Id}", id);
        return flat;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var flat = await _unitOfWork.Flats.GetByIdAsync(id);
        if (flat == null) return false;

        await _unitOfWork.Flats.DeleteAsync(flat);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Flat deleted: {Id}", id);
        return true;
    }
}
