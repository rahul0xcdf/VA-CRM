using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/>.
/// </summary>
/// <remarks>
/// One instance owns one <see cref="AppDbContext"/> and therefore one change tracker, so every
/// repository it hands out participates in the same commit. Checkpoint C3.3 — a tax invoice that
/// fails midway leaves neither a stock nor a ledger row — is met by this type or it is not met at
/// all.
/// </remarks>
internal sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    private readonly AppDbContext _context = context
        ?? throw new ArgumentNullException(nameof(context));

    private readonly ConcurrentDictionary<Type, object> _repositories = new();

    private bool _disposed;

    public IRepository<TEntity> Repository<TEntity>()
        where TEntity : Entity
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Cached per entity type so that repeated calls share the one change tracker rather than
        // handing out independent views of the same pending work.
        return (IRepository<TEntity>)_repositories.GetOrAdd(
            typeof(TEntity),
            _ => new Repository<TEntity>(_context));
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            return await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Translated here rather than left to the caller, because the Application and App
            // layers must be able to handle a lost update and neither may reference EF Core
            // (ADR-0001). Letting the EF type escape would mean the only way to catch this in a
            // view model was to break the dependency rule.
            throw ConcurrentUpdateException.For(NameOfContestedEntity(ex), ex);
        }
    }

    /// <summary>Names the entity that lost the race, for a message saying what to reload.</summary>
    /// <remarks>
    /// EF reports every entry it could not save; the first is enough for a message, and a save that
    /// contests several rows at once has one cause anyway. Falls back to a generic word rather than
    /// throwing — a concurrency failure must not be replaced by a failure to describe it.
    /// </remarks>
    private static string NameOfContestedEntity(DbUpdateConcurrencyException exception)
    {
        var entry = exception.Entries.Count > 0 ? exception.Entries[0] : null;

        return entry?.Entity.GetType().Name ?? "record";
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // An ambient transaction already exists when a caller nests scopes. Returning a no-op
        // handle keeps the inner scope from committing work the outer one still owns.
        if (_context.Database.CurrentTransaction is not null)
        {
            return NestedUnitOfWorkTransaction.Instance;
        }

        var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        return new UnitOfWorkTransaction(transaction);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _repositories.Clear();
        await _context.DisposeAsync().ConfigureAwait(false);
    }
}
