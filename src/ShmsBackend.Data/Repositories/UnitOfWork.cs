using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Repositories.Interfaces;

namespace ShmsBackend.Data.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly ShmsDbContext _context;
    private IDbContextTransaction? _transaction;

    private IAdminRepository? _adminRepository;
    private IPortalUserRepository? _portalUserRepository;
    private ILandlordRepository? _landlordRepository;
    private IAgentRepository? _agentRepository;
    private ITenantRepository? _tenantRepository;
    private IExplorerRepository? _explorerRepository;
    private IHouseRepository? _houseRepository;
    private IFlatRepository? _flatRepository;

    public UnitOfWork(ShmsDbContext context)
    {
        _context = context;
    }

    public IAdminRepository Admins
    {
        get
        {
            _adminRepository ??= new AdminRepository(_context);
            return _adminRepository;
        }
    }

    public IPortalUserRepository PortalUsers
    {
        get
        {
            _portalUserRepository ??= new PortalUserRepository(_context);
            return _portalUserRepository;
        }
    }

    public ILandlordRepository Landlords
    {
        get
        {
            _landlordRepository ??= new LandlordRepository(_context);
            return _landlordRepository;
        }
    }

    public IAgentRepository Agents
    {
        get
        {
            _agentRepository ??= new AgentRepository(_context);
            return _agentRepository;
        }
    }

    public ITenantRepository Tenants
    {
        get
        {
            _tenantRepository ??= new TenantRepository(_context);
            return _tenantRepository;
        }
    }

    public IExplorerRepository Explorers
    {
        get
        {
            _explorerRepository ??= new ExplorerRepository(_context);
            return _explorerRepository;
        }
    }

    public IHouseRepository Houses
    {
        get
        {
            _houseRepository ??= new HouseRepository(_context);
            return _houseRepository;
        }
    }

    public IFlatRepository Flats
    {
        get
        {
            _flatRepository ??= new FlatRepository(_context);
            return _flatRepository;
        }
    }

    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }

    public async Task BeginTransactionAsync()
    {
        _transaction = await _context.Database.BeginTransactionAsync();
    }

    public async Task CommitTransactionAsync()
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync()
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _context.Dispose();
    }
}
