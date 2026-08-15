using System.Linq.Expressions;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Tests.Common;

namespace VinayakaAgencies.Domain.Tests.Abstractions;

/// <summary>
/// Exposes the protected builder methods of <see cref="Specification{TEntity}"/> so the tests can
/// drive them directly, the same way a real specification's constructor would.
/// </summary>
internal sealed class ProbeSpecification : Specification<TestEntity>
{
    public ProbeSpecification()
    {
    }

    public ProbeSpecification(Expression<Func<TestEntity, bool>> criteria)
        : base(criteria)
    {
    }

    public void ExposeWhere(Expression<Func<TestEntity, bool>> criteria) => Where(criteria);

    public void ExposeAddInclude(Expression<Func<TestEntity, object?>> include) => AddInclude(include);

    public void ExposeApplyOrderBy(Expression<Func<TestEntity, object?>> orderBy) =>
        ApplyOrderBy(orderBy);

    public void ExposeApplyOrderByDescending(Expression<Func<TestEntity, object?>> orderBy) =>
        ApplyOrderByDescending(orderBy);

    public void ExposeApplyPaging(int skip, int take) => ApplyPaging(skip, take);

    public void ExposeApplyNoTracking() => ApplyNoTracking();
}
