namespace VinayakaAgencies.Application.Maintenance;

/// <summary>
/// Computes the <see cref="BookFingerprint"/> checkpoint C10.6 compares across a restore.
/// </summary>
public interface IBookFingerprintService
{
    /// <summary>
    /// Renders the trial balance and the stock summary over the whole book and hashes both.
    /// </summary>
    Task<BookFingerprint> ComputeAsync(CancellationToken cancellationToken = default);
}
