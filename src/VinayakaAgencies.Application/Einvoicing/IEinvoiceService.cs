namespace VinayakaAgencies.Application.Einvoicing;

/// <summary>
/// Registers posted documents with the IRP and cancels them (phase-6-einvoice-eway.md §2).
/// </summary>
public interface IEinvoiceService
{
    /// <summary>Registrations in a date range, newest document first.</summary>
    Task<IReadOnlyList<EinvoiceRegistrationSummary>> ListAsync(
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>The registration for one document, if it has one.</summary>
    Task<EinvoiceRegistrationSummary?> FindByDocumentAsync(
        Guid tradeDocumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Assembles what would be sent for a document, without sending it. Exposed so the screen can
    /// show it and so the payload can be tested without a portal — which, while
    /// <c>open-questions.md</c> #10 is open, is the only way it can be tested at all.
    /// </summary>
    /// <exception cref="EinvoiceNotSubmittableException">Something the portal would refuse is missing.</exception>
    Task<EinvoiceRequest> BuildRequestAsync(
        Guid tradeDocumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an IRN for a posted document, or returns the one it already has.
    /// </summary>
    /// <remarks>
    /// Idempotent by construction. The IRN is computed locally before the portal is called, a unique
    /// index keeps one registration per IRN, and an unresolved earlier attempt is settled by asking
    /// the portal about the IRN before ever submitting again — so a retry after a timeout cannot
    /// produce a second registration (checkpoint C6.5).
    /// </remarks>
    /// <exception cref="EinvoiceNotSubmittableException">The document cannot be submitted as it stands.</exception>
    Task<EinvoiceGenerationResult> GenerateAsync(
        Guid tradeDocumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a registration at the portal, within its 24-hour window.
    /// </summary>
    /// <exception cref="InvalidOperationException">The window has closed, so a credit note is the only route.</exception>
    Task<EinvoiceRegistrationSummary> CancelAsync(
        Guid registrationId,
        string reasonCode,
        string? remarks = null,
        CancellationToken cancellationToken = default);
}
