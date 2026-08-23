using System.Linq.Expressions;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Abstractions;

/// <summary>
/// A provider-agnostic description of a query: filter, includes, ordering and paging.
/// </summary>
/// <remarks>
/// Callers describe <i>what</i> they want; Infrastructure decides how to execute it. This is what
/// keeps LINQ-over-EF out of the Application layer and keeps the Phase 9 provider swap cheap.
/// </remarks>
public interface ISpecification<TEntity>
    where TEntity : Entity
{
    Expression<Func<TEntity, bool>>? Criteria { get; }

    IReadOnlyList<Expression<Func<TEntity, object?>>> Includes { get; }

    Expression<Func<TEntity, object?>>? OrderBy { get; }

    Expression<Func<TEntity, object?>>? OrderByDescending { get; }

    int? Skip { get; }

    int? Take { get; }

    /// <summary>
    /// When true the query is executed without change tracking. Use for read-only reads.
    /// </summary>
    bool AsNoTracking { get; }
}
