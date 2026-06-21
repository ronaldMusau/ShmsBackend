using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Data.Repositories;

public class AgentRepository : Repository<Agent>, IAgentRepository
{
    public AgentRepository(ShmsDbContext context) : base(context)
    {
    }

    public async Task<Agent?> GetByEmailAsync(string email)
    {
        return await _dbSet
            .FirstOrDefaultAsync(a => a.Email.ToLower() == email.ToLower());
    }
}
