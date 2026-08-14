using MotorPumpSuite.Domain.Abstractions;
using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Application.Tests.TestDoubles;

/// <summary>
/// In-memory stand-in for <see cref="IRepository{TEntity}"/>.
/// </summary>
/// <remarks>
/// Applies the specification's <c>Criteria</c>/<c>OrderBy</c>/paging so a test that hands the
/// service a <see cref="ISpecification{TEntity}"/> genuinely exercises that pathway instead of the
/// fake silently returning everything regardless of what was asked for.
/// </remarks>
public sealed class FakeRepository<TEntity> : IRepository<TEntity>
    where TEntity : Entity
{
    private readonly List<TEntity> _items = [];

    /// <summary>Direct access for test assertions — what the service actually staged.</summary>
    public IReadOnlyList<TEntity> Items => _items;

    public Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_items.SingleOrDefault(e => e.Id == id));

    public Task<IReadOnlyList<TEntity>> ListAsync(
        ISpecification<TEntity>? specification = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TEntity> result = Apply(specification).ToList();
        return Task.FromResult(result);
    }

    public Task<TEntity?> SingleOrDefaultAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return Task.FromResult(Apply(specification).SingleOrDefault());
    }

    public Task<bool> AnyAsync(
        ISpecification<TEntity>? specification = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Apply(specification).Any());

    public Task<int> CountAsync(
        ISpecification<TEntity>? specification = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Apply(specification).Count());

    public Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _items.Add(entity);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        _items.AddRange(entities);
        return Task.CompletedTask;
    }

    public void Update(TEntity entity)
    {
        // The fake holds live references in _items already; nothing to stage separately.
    }

    public void Remove(TEntity entity) => _items.Remove(entity);

    private IEnumerable<TEntity> Apply(ISpecification<TEntity>? specification)
    {
        IEnumerable<TEntity> query = _items;

        if (specification is null)
        {
            return query;
        }

        if (specification.Criteria is not null)
        {
            query = query.Where(specification.Criteria.Compile());
        }

        if (specification.OrderBy is not null)
        {
            query = query.OrderBy(specification.OrderBy.Compile());
        }
        else if (specification.OrderByDescending is not null)
        {
            query = query.OrderByDescending(specification.OrderByDescending.Compile());
        }

        if (specification.Skip is int skip)
        {
            query = query.Skip(skip);
        }

        if (specification.Take is int take)
        {
            query = query.Take(take);
        }

        return query;
    }
}
