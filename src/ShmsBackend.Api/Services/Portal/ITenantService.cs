using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShmsBackend.Api.Models.DTOs.Tenant;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Portal;

public interface ITenantService
{
    Task<Tenant> CreateAsync(CreateTenantDto dto);
    Task<Tenant?> GetByIdAsync(Guid id);
    Task<IEnumerable<Tenant>> GetAllAsync();
    Task<Tenant> UpdateAsync(Guid id, UpdateTenantDto dto);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> ToggleStatusAsync(Guid id);
}
