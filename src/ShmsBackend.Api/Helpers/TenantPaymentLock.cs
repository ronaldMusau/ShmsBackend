using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ShmsBackend.Api.Helpers;

/// <summary>
/// Serialises payment initiation per tenant (within this server process) so that the
/// "is a payment already processing?" check and the insert of the new checkout attempt
/// behave as one atomic step. Without it, two simultaneous requests both pass the check
/// before either has created its attempt row, and the tenant is charged twice.
/// </summary>
public static class TenantPaymentLock
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    public static async Task<IDisposable> AcquireAsync(Guid tenantId)
    {
        var sem = Locks.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        return new Releaser(sem);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _sem;
        public Releaser(SemaphoreSlim sem) => _sem = sem;
        public void Dispose() => Interlocked.Exchange(ref _sem, null)?.Release();
    }
}
