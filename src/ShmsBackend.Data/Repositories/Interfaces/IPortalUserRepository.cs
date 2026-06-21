using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Data.Repositories.Interfaces;

public interface IPortalUserRepository : IRepository<PortalUser>
{
    Task<PortalUser?> GetByEmailAsync(string email);
    Task<PortalUser?> GetByEmailAndTypeAsync(string email, PortalUserType type);
    Task<List<PortalUserType>> GetUserTypesAsync(string email);
    Task<T?> GetSpecificPortalUserAsync<T>(Guid id) where T : PortalUser;
}
