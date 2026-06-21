using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Data.Repositories.Interfaces;

public interface IHouseRepository : IRepository<House>
{
    Task<IEnumerable<House>> GetByLandlordIdAsync(Guid landlordId);
    Task<IEnumerable<House>> GetAvailableAsync();
    Task<IEnumerable<House>> GetByCityAsync(string city);
}
