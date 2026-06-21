using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShmsBackend.Api.Models.DTOs.Flat;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Portal;

public interface IFlatService
{
    Task<Flat> CreateAsync(CreateFlatDto dto);
    Task<Flat?> GetByIdAsync(Guid id);
    Task<IEnumerable<Flat>> GetAllAsync();
    Task<IEnumerable<Flat>> GetByLandlordAsync(Guid landlordId);
    Task<IEnumerable<Flat>> GetByHouseAsync(Guid houseId);
    Task<IEnumerable<Flat>> GetAvailableAsync();
    Task<IEnumerable<Flat>> GetByCityAsync(string city);
    Task<Flat> UpdateAsync(Guid id, UpdateFlatDto dto);
    Task<bool> DeleteAsync(Guid id);
}
