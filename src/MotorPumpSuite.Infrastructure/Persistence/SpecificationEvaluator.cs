using Microsoft.EntityFrameworkCore;
using MotorPumpSuite.Domain.Abstractions;
using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Infrastructure.Persistence;

/// <summary>
/// Translates an <see cref="ISpecification{TEntity}"/> into an EF Core query.
/// </summary>
/// <remarks>
/// This is the single place where a specification meets LINQ-over-EF. Callers describe what they
/// want; only this file knows how it is executed, which is what keeps <c>Microsoft.EntityFrameworkCore</c>
/// out of the Application layer (ADR-0001).
/// </remarks>
internal static class SpecificationEvaluator
{
    public static IQueryable<TEntity> Apply<TEntity>(
        IQueryable<TEntity> source,
        ISpecification<TEntity>? specification)
        where TEntity : Entity
    {
        ArgumentNullException.ThrowIfNull(source);

        if (specification is null)
        {
            return source;
        }

        var query = source;

        if (specification.AsNoTracking)
        {
            query = query.AsNoTracking();
        }

        if (specification.Criteria is not null)
        {
            query = query.Where(specification.Criteria);
        }

        foreach (var include in specification.Includes)
        {
            query = query.Include(include);
        }

        if (specification.OrderBy is not null)
        {
            query = query.OrderBy(specification.OrderBy);
        }
        else if (specification.OrderByDescending is not null)
        {
            query = query.OrderByDescending(specification.OrderByDescending);
        }

        // Skip and Take are only meaningful over a defined order; an unordered page is not
        // reproducible. Specification.ApplyPaging validates the values themselves.
        if (specification.Skip is { } skip)
        {
            query = query.Skip(skip);
        }

        if (specification.Take is { } take)
        {
            query = query.Take(take);
        }

        return query;
    }
}
