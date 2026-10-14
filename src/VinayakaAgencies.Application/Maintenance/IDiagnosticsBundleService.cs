namespace VinayakaAgencies.Application.Maintenance;

/// <summary>
/// Collects logs, crash reports and environment facts into one file the owner can send
/// (phase-10-hardening.md §2, final bullet).
/// </summary>
public interface IDiagnosticsBundleService
{
    /// <summary>Writes a bundle and returns where it landed.</summary>
    /// <param name="operatorNote">
    /// What the operator was doing when it went wrong, in their own words. Optional, and worth more
    /// than anything in the logs when it is supplied.
    /// </param>
    Task<DiagnosticsBundle> CreateAsync(
        string? operatorNote = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an unhandled fault as a crash report beside the logs, for the next bundle to carry.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CreateAsync"/> because the moment of a crash is the worst possible
    /// time to build a ZIP: the process may be seconds from death. This writes one small file and
    /// returns.
    /// </remarks>
    Task<string> RecordCrashAsync(
        string source,
        Exception exception,
        CancellationToken cancellationToken = default);
}

/// <summary>A written diagnostics bundle.</summary>
public sealed record DiagnosticsBundle(
    string FilePath,
    long SizeBytes,
    IReadOnlyList<string> Contents,
    int RedactedEntryCount)
{
    public string FileName => System.IO.Path.GetFileName(FilePath);
}
