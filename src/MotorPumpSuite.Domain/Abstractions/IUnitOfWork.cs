using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Domain.Abstractions;

/// <summary>
/// One logical business transaction. Hands out repositories and owns the commit boundary.
/// </summary>
/// <remarks>
/// This exists for one specific reason: from Phase 3, posting a tax invoice writes journal lines
/// <i>and</i> stock ledger lines, and a failure midway must leave neither. The checkpoint criterion
/// C3.3 is met by this type or it is not met at all.
/// </remarks>
public interface IUnitOfWork : IAsyncDisposable
{
    IRepository<TEntity> Repository<TEntity>()
        where TEntity : Entity;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// An explicit transaction spanning several <see cref="IUnitOfWork.SaveChangesAsync"/> calls.
/// Disposing without committing rolls back.
/// </summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
