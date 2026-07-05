using System.Threading.Tasks;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Data.Repositories.Interfaces;

public interface IAgentRepository : IRepository<Agent>
{
    Task<Agent?> GetByEmailAsync(string email);
    Task<Agent?> GetDeletedByEmailAsync(string email);
}
