using Microsoft.EntityFrameworkCore.Storage;
using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Infrastructure.Persistence;

/// <summary>
/// Wraps an EF Core transaction so callers never see a provider type.
/// </summary>
/// <remarks>
/// Disposing without committing rolls back. That default is deliberate: an exception thrown
/// between posting a voucher's ledger lines and its stock lines must abandon both, and relying on
/// a caller to remember an explicit rollback in a <c>catch</c> block is how half-posted invoices
/// happen.
/// </remarks>
internal sealed class UnitOfWorkTransaction(IDbContextTransaction transaction)
    : IUnitOfWorkTransaction
{
    private readonly IDbContextTransaction _transaction = transaction
        ?? throw new ArgumentNullException(nameof(transaction));

    private bool _completed;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            await _transaction.RollbackAsync().ConfigureAwait(false);
            _completed = true;
        }

        await _transaction.DisposeAsync().ConfigureAwait(false);
    }
}
