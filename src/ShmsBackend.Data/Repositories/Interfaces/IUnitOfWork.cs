using System;
using System.Threading.Tasks;

namespace ShmsBackend.Data.Repositories.Interfaces;

public interface IUnitOfWork : IDisposable
{
    IAdminRepository Admins { get; }
    IPortalUserRepository PortalUsers { get; }
    ILandlordRepository Landlords { get; }
    IAgentRepository Agents { get; }
    ITenantRepository Tenants { get; }
    IHouseRepository Houses { get; }
    IFlatRepository Flats { get; }
    Task<int> SaveChangesAsync();
    Task BeginTransactionAsync();
    Task CommitTransactionAsync();
    Task RollbackTransactionAsync();
}
