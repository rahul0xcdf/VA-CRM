namespace VinayakaAgencies.Application.Maintenance;

/// <summary>What one run of <see cref="LegacyDataRelocator"/> did, and why.</summary>
/// <remarks>
/// A record rather than a bool because every outcome here needs to appear in the log in a form
/// somebody can act on. "Relocation skipped" is useless to an owner whose books have apparently
/// vanished; "found no database at the old location" tells them where to look next.
/// </remarks>
public sealed record LegacyDataRelocation(
    LegacyDataRelocationOutcome Outcome,
    string? FromPath,
    string? ToPath,
    IReadOnlyList<string> MovedEntries,
    string Explanation)
{
    /// <summary>Nothing needed doing — the usual case on every run after the first.</summary>
    public static LegacyDataRelocation NotNeeded(string explanation) => new(
        LegacyDataRelocationOutcome.NotNeeded,
        FromPath: null,
        ToPath: null,
        MovedEntries: [],
        explanation);
}

/// <summary>The three things that can come of looking for a pre-Phase-10 data directory.</summary>
public enum LegacyDataRelocationOutcome
{
    /// <summary>No per-user database to move, or the machine-wide one already exists.</summary>
    NotNeeded = 0,

    /// <summary>A pre-Phase-10 install was found and moved. Happens exactly once per machine.</summary>
    Relocated = 1,

    /// <summary>
    /// A database was found at both locations. Deliberately not resolved automatically.
    /// </summary>
    AmbiguousLeftAlone = 2,
}
