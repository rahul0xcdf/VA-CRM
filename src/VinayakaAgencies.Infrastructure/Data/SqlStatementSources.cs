using System.Reflection;
using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Infrastructure.Data;

/// <summary>
/// Collects every <see cref="ISqlStatementSource"/> in this assembly for one provider.
/// </summary>
internal static class SqlStatementSources
{
    /// <summary>
    /// Builds the statement table for <paramref name="provider"/> by merging every source declared
    /// for it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Two sources claim the same <see cref="SqlQueryKey"/>. That is always a mistake and always
    /// worth failing startup over: the winner would otherwise depend on reflection order, so the
    /// same build could resolve a key to one statement on one machine and another elsewhere.
    /// </exception>
    internal static IReadOnlyDictionary<SqlQueryKey, string> For(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        var merged = new Dictionary<SqlQueryKey, string>();
        var owners = new Dictionary<SqlQueryKey, string>();

        foreach (var source in Discover(provider))
        {
            foreach (var (key, sql) in source.Statements)
            {
                if (owners.TryGetValue(key, out var firstOwner))
                {
                    throw new InvalidOperationException(
                        $"Query key '{key}' is declared for provider '{provider}' by both " +
                        $"{firstOwner} and {source.GetType().Name}. A key must have exactly one " +
                        "statement per provider — otherwise which one wins depends on reflection " +
                        "order. Remove one of the two declarations.");
                }

                merged.Add(key, sql);
                owners.Add(key, source.GetType().Name);
            }
        }

        return merged;
    }

    private static IEnumerable<ISqlStatementSource> Discover(string provider) =>
        typeof(SqlStatementSources).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .Where(typeof(ISqlStatementSource).IsAssignableFrom)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(Instantiate)
            .Where(source => string.Equals(source.Provider, provider, StringComparison.OrdinalIgnoreCase));

    private static ISqlStatementSource Instantiate(Type type)
    {
        var instance = Activator.CreateInstance(type) as ISqlStatementSource;

        return instance ?? throw new InvalidOperationException(
            $"{type.FullName} implements {nameof(ISqlStatementSource)} but could not be created. " +
            "A statement source needs a public parameterless constructor, because the dialect " +
            "discovers sources by reflection rather than through the container.");
    }
}
