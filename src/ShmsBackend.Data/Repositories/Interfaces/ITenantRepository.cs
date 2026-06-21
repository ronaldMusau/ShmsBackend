using System.Threading.Tasks;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Data.Repositories.Interfaces;

public interface ITenantRepository : IRepository<Tenant>
{
    Task<Tenant?> GetByEmailAsync(string email);
}
