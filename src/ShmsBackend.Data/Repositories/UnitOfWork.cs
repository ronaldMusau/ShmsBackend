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
    private IAdminRepository? _adminRepository;  // Changed from _userRepository

    public UnitOfWork(ShmsDbContext context)
    {
        _context = context;
    }

    public IAdminRepository Admins  // Changed from Users
    {
        get
        {
            _adminRepository ??= new AdminRepository(_context);
            return _adminRepository;
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