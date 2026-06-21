using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Data.Repositories.Interfaces;

public interface IFlatRepository : IRepository<Flat>
{
    Task<IEnumerable<Flat>> GetByLandlordIdAsync(Guid landlordId);
    Task<IEnumerable<Flat>> GetByHouseIdAsync(Guid houseId);
    Task<IEnumerable<Flat>> GetAvailableAsync();
    Task<IEnumerable<Flat>> GetByCityAsync(string city);
}
