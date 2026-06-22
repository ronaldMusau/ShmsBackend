using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Data.Repositories;

public class ExplorerRepository : Repository<Explorer>, IExplorerRepository
{
    public ExplorerRepository(ShmsDbContext context) : base(context)
    {
    }

    public async Task<Explorer?> GetByEmailAsync(string email)
    {
        return await _dbSet
            .FirstOrDefaultAsync(e => e.Email.ToLower() == email.ToLower());
    }
}
