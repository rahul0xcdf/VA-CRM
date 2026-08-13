using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Domain.Abstractions;

/// <summary>
/// The only sanctioned way to read and write entities. No <c>DbContext</c> type ever leaves the
/// Infrastructure assembly — see ADR-0001.
/// </summary>
/// <remarks>
/// Writes are staged; nothing is persisted until <see cref="IUnitOfWork.SaveChangesAsync"/> is
/// called. That is deliberate: Phase 3 requires ledger and stock effects to commit atomically or
/// not at all.
/// </remarks>
public interface IRepository<TEntity>
    where TEntity : Entity
{
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> ListAsync(
        ISpecification<TEntity>? specification = null,
        CancellationToken cancellationToken = default);

    Task<TEntity?> SingleOrDefaultAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(
        ISpecification<TEntity>? specification = null,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        ISpecification<TEntity>? specification = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    void Update(TEntity entity);

    void Remove(TEntity entity);
}
