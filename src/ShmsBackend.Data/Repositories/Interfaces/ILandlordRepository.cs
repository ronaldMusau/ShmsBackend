using System.Threading.Tasks;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Data.Repositories.Interfaces;

public interface ILandlordRepository : IRepository<Landlord>
{
    Task<Landlord?> GetByEmailAsync(string email);
}
