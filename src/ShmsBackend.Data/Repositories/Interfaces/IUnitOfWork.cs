using System;
using System.Threading.Tasks;

namespace ShmsBackend.Data.Repositories.Interfaces;

public interface IUnitOfWork : IDisposable
{
    IAdminRepository Admins { get; }  // Changed from Users
    Task<int> SaveChangesAsync();
    Task BeginTransactionAsync();
    Task CommitTransactionAsync();
    Task RollbackTransactionAsync();
}