using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShmsBackend.Api.Models.DTOs.Flat;

namespace ShmsBackend.Api.Services.Portal;

public interface IFlatService
{
    Task<object> CreateAsync(CreateFlatDto dto);
    Task<object?> GetByIdAsync(Guid id);
    Task<IEnumerable<object>> GetAllAsync();
    Task<IEnumerable<object>> GetByLandlordAsync(Guid landlordId);
    Task<IEnumerable<object>> GetByLocationAsync(string county, string constituency, string ward);
    Task<object?> UpdateAsync(Guid id, UpdateFlatDto dto);
    Task<bool> DeleteAsync(Guid id);
    Task<object?> AddHouseLinesAsync(Guid flatId, List<HouseGroupDto> houseLines);
}
