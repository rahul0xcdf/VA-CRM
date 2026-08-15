using Microsoft.EntityFrameworkCore;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IRepository{TEntity}"/>.
/// </summary>
/// <remarks>
/// Writes are staged on the change tracker and committed by
/// <see cref="IUnitOfWork.SaveChangesAsync"/>, never here. Phase 3 requires a tax invoice to post
/// its ledger and stock effects atomically, which is only possible if a repository cannot commit
/// on its own.
/// </remarks>
internal sealed class Repository<TEntity>(AppDbContext context) : IRepository<TEntity>
    where TEntity : Entity
{
    private readonly AppDbContext _context = context
        ?? throw new ArgumentNullException(nameof(context));

    private DbSet<TEntity> Set => _context.Set<TEntity>();

    public Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Set.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TEntity>> ListAsync(
        ISpecification<TEntity>? specification = null,
        CancellationToken cancellationToken = default) =>
        await SpecificationEvaluator.Apply(Set, specification)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<TEntity?> SingleOrDefaultAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return SpecificationEvaluator.Apply(Set, specification)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<bool> AnyAsync(
        ISpecification<TEntity>? specification = null,
        CancellationToken cancellationToken = default) =>
        SpecificationEvaluator.Apply(Set, specification).AnyAsync(cancellationToken);

    public Task<int> CountAsync(
        ISpecification<TEntity>? specification = null,
        CancellationToken cancellationToken = default) =>
        SpecificationEvaluator.Apply(Set, specification).CountAsync(cancellationToken);

    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await Set.AddAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddRangeAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        await Set.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);
    }

    public void Update(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Set.Update(entity);
    }

    public void Remove(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Set.Remove(entity);
    }
}
