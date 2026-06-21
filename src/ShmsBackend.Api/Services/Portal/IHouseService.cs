using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShmsBackend.Api.Models.DTOs.House;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Portal;

public interface IHouseService
{
    Task<House> CreateAsync(CreateHouseDto dto);
    Task<House?> GetByIdAsync(Guid id);
    Task<IEnumerable<House>> GetAllAsync();
    Task<IEnumerable<House>> GetByLandlordAsync(Guid landlordId);
    Task<IEnumerable<House>> GetAvailableAsync();
    Task<IEnumerable<House>> GetByCityAsync(string city);
    Task<House> UpdateAsync(Guid id, UpdateHouseDto dto);
    Task<bool> DeleteAsync(Guid id);
}
