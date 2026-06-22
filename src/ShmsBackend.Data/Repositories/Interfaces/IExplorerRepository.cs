using System.Threading.Tasks;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Data.Repositories.Interfaces;

public interface IExplorerRepository : IRepository<Explorer>
{
    Task<Explorer?> GetByEmailAsync(string email);
}
