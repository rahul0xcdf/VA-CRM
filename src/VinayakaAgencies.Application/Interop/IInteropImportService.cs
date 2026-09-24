namespace VinayakaAgencies.Application.Interop;

/// <summary>
/// Brings a file into the books: validate everything, then write it or write nothing.
/// </summary>
/// <remarks>
/// The two calls are the two passes §2 requires and §4a D2 sizes. <see cref="ValidateAsync"/> reads
/// the whole source and reports every problem in it without touching the database;
/// <see cref="CommitAsync"/> re-reads it, refuses if anything is still wrong, and writes what it
/// finds inside a single transaction.
/// <para>
/// Committing re-validates rather than trusting the preview it was handed. A preview is a snapshot
/// of a file at a moment, and between looking at it and pressing the key the owner may well have
/// gone and corrected the source — which is exactly the workflow a migration has (§4a D2). Writing
/// against a stale verdict is how a file with a known error gets committed anyway.
/// </para>
/// </remarks>
public interface IInteropImportService
{
    /// <summary>Reads the whole source and reports what is wrong with it. Writes nothing.</summary>
    Task<InteropImportPreview> ValidateAsync(
        IInteropSource source,
        InteropImportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates, then writes everything in one transaction — or nothing at all.
    /// </summary>
    /// <remarks>
    /// Reconciles afterwards (C8.6): the trial balance and closing stock are read before and after,
    /// and the movement is compared against what the file carried.
    /// </remarks>
    Task<InteropImportResult> CommitAsync(
        IInteropSource source,
        InteropImportOptions options,
        IProgress<InteropImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
