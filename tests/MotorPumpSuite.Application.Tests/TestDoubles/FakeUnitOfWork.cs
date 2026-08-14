using MotorPumpSuite.Domain.Abstractions;
using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Application.Tests.TestDoubles;

/// <summary>
/// In-memory stand-in for <see cref="IUnitOfWork"/>.
/// </summary>
/// <remarks>
/// Proves the seam the whole Application layer is built on: a service that only ever talks to
/// <see cref="IUnitOfWork"/> can be fully unit tested with no database and no EF Core.
/// </remarks>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    private readonly Dictionary<Type, object> _repositories = [];

    /// <summary>How many times <see cref="SaveChangesAsync"/> was called — services must call it
    /// exactly once per logical write.</summary>
    public int SaveChangesCallCount { get; private set; }

    /// <summary>Every transaction handed out, in order, so a test can inspect commit/rollback state.</summary>
    public List<FakeUnitOfWorkTransaction> Transactions { get; } = [];

    public bool Disposed { get; private set; }

    public IRepository<TEntity> Repository<TEntity>()
        where TEntity : Entity
    {
        if (_repositories.TryGetValue(typeof(TEntity), out var existing))
        {
            return (IRepository<TEntity>)existing;
        }

        var repository = new FakeRepository<TEntity>();
        _repositories[typeof(TEntity)] = repository;
        return repository;
    }

    /// <summary>Typed accessor for assertions against the concrete fake, without an unsafe cast at
    /// every call site.</summary>
    public FakeRepository<TEntity> FakeRepositoryFor<TEntity>()
        where TEntity : Entity => (FakeRepository<TEntity>)Repository<TEntity>();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(SaveChangesCallCount);
    }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        var transaction = new FakeUnitOfWorkTransaction();
        Transactions.Add(transaction);
        return Task.FromResult<IUnitOfWorkTransaction>(transaction);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
