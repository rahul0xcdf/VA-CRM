namespace VinayakaAgencies.Application.Gst;

/// <summary>
/// Maintains the HSN/SAC master (phase-5-gst-returns.md §2). Every change lands through the unit
/// of work, so the audit trail captures it like every other master.
/// </summary>
public interface IHsnSacService
{
    Task<IReadOnlyList<HsnSacSummary>> ListAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<HsnSacSummary?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates or revises one code. Codes are unique among a company's active rows.</summary>
    Task<HsnSacSummary> SaveAsync(SaveHsnSacRequest request, CancellationToken cancellationToken = default);

    Task DeactivateAsync(Guid id, CancellationToken cancellationToken = default);

    Task ActivateAsync(Guid id, CancellationToken cancellationToken = default);
}
