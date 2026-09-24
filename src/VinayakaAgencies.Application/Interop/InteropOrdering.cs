using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// Orders account groups so that no group is written before its parent.
/// </summary>
/// <remarks>
/// Both directions need this and for the same reason. A file that names a parent Tally has not read
/// yet imports a partial chart of accounts; the same file read by this application's own importer
/// would have to defer the row and re-resolve it later. Sorting once at the boundary removes the
/// problem from both sides instead of making each cope with it.
/// </remarks>
public static class InteropOrdering
{
    /// <summary>
    /// Returns the groups with every parent ahead of its children.
    /// </summary>
    /// <remarks>
    /// Groups whose parent is not in the collection — the usual case, since primary groups already
    /// exist in the target company and are not exported — count as roots. A cycle cannot be ordered,
    /// so its members are appended in their original order rather than dropped: losing rows silently
    /// would be worse than emitting a file whose import reports the problem.
    /// </remarks>
    public static IReadOnlyList<InteropAccountGroup> ParentsFirst(
        IReadOnlyCollection<InteropAccountGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        if (groups.Count <= 1)
        {
            return groups as IReadOnlyList<InteropAccountGroup> ?? [.. groups];
        }

        var byName = new Dictionary<string, InteropAccountGroup>(StringComparer.Ordinal);

        foreach (var group in groups)
        {
            byName[NameNormaliser.Normalise(group.Name)] = group;
        }

        var ordered = new List<InteropAccountGroup>(groups.Count);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var inProgress = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in groups)
        {
            Place(group, byName, ordered, placed, inProgress);
        }

        return ordered;
    }

    private static void Place(
        InteropAccountGroup group,
        Dictionary<string, InteropAccountGroup> byName,
        List<InteropAccountGroup> ordered,
        HashSet<string> placed,
        HashSet<string> inProgress)
    {
        var key = NameNormaliser.Normalise(group.Name);

        if (placed.Contains(key))
        {
            return;
        }

        // Already on the stack: this is a cycle. Stop descending and let the caller place it, so the
        // row survives into the file and is reported by whoever reads it.
        if (!inProgress.Add(key))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(group.ParentName)
            && byName.TryGetValue(NameNormaliser.Normalise(group.ParentName), out var parent))
        {
            Place(parent, byName, ordered, placed, inProgress);
        }

        inProgress.Remove(key);

        if (placed.Add(key))
        {
            ordered.Add(group);
        }
    }
}
