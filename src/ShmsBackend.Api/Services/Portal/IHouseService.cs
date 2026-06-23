using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShmsBackend.Api.Models.DTOs.House;

namespace ShmsBackend.Api.Services.Portal;

public interface IHouseService
{
    Task<object> CreateAsync(CreateHouseDto dto);
    Task<object?> GetByIdAsync(Guid id);
    Task<IEnumerable<object>> GetAllAsync();
    Task<IEnumerable<object>> GetByFlatAsync(Guid flatId);
    Task<object?> UpdateAsync(Guid id, UpdateHouseDto dto);
    Task<bool> DeleteAsync(Guid id);
}
