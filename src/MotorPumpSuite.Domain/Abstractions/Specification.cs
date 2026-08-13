using System.Linq.Expressions;
using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Domain.Abstractions;

/// <summary>
/// Convenience base class for <see cref="ISpecification{TEntity}"/> implementations.
/// </summary>
public abstract class Specification<TEntity> : ISpecification<TEntity>
    where TEntity : Entity
{
    private readonly List<Expression<Func<TEntity, object?>>> _includes = [];

    protected Specification()
    {
    }

    protected Specification(Expression<Func<TEntity, bool>> criteria)
    {
        Criteria = criteria;
    }

    public Expression<Func<TEntity, bool>>? Criteria { get; private set; }

    public IReadOnlyList<Expression<Func<TEntity, object?>>> Includes => _includes;

    public Expression<Func<TEntity, object?>>? OrderBy { get; private set; }

    public Expression<Func<TEntity, object?>>? OrderByDescending { get; private set; }

    public int? Skip { get; private set; }

    public int? Take { get; private set; }

    public bool AsNoTracking { get; private set; }

    protected void Where(Expression<Func<TEntity, bool>> criteria) => Criteria = criteria;

    protected void AddInclude(Expression<Func<TEntity, object?>> include) => _includes.Add(include);

    protected void ApplyOrderBy(Expression<Func<TEntity, object?>> orderBy) => OrderBy = orderBy;

    protected void ApplyOrderByDescending(Expression<Func<TEntity, object?>> orderBy) =>
        OrderByDescending = orderBy;

    protected void ApplyPaging(int skip, int take)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);

        Skip = skip;
        Take = take;
    }

    protected void ApplyNoTracking() => AsNoTracking = true;
}
