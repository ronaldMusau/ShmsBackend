using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShmsBackend.Api.Models.DTOs.Landlord;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Portal;

public interface ILandlordService
{
    Task<Landlord> CreateAsync(CreateLandlordDto dto);
    Task<Landlord?> GetByIdAsync(Guid id);
    Task<IEnumerable<Landlord>> GetAllAsync();
    Task<Landlord> UpdateAsync(Guid id, UpdateLandlordDto dto);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> ToggleStatusAsync(Guid id);
}
