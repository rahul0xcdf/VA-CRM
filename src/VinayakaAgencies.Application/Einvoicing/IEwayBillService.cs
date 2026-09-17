using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Application.Einvoicing;

/// <summary>
/// Generates and maintains e-way bills (phase-6-einvoice-eway.md §2).
/// </summary>
public interface IEwayBillService
{
    Task<IReadOnlyList<EwayBillSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<EwayBillSummary?> FindByDocumentAsync(
        Guid tradeDocumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a document's value obliges an e-way bill, against the configured threshold.
    /// </summary>
    /// <remarks>
    /// Advisory, and returned rather than enforced: the threshold test is on consignment value, and a
    /// consignment can differ from an invoice — several invoices may travel together. So this answers
    /// the question and the user decides, instead of the software refusing to make a bill somebody
    /// has a reason to want.
    /// </remarks>
    Task<bool> IsRequiredForDocumentAsync(
        Guid tradeDocumentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a bill for a document. When the invoice already has an IRN the bill is generated
    /// <em>by IRN</em>, which is the route the spec asks for and the one that spares re-sending the
    /// invoice detail (checkpoint C6.2).
    /// </summary>
    Task<EwayBillSummary> GenerateAsync(
        Guid tradeDocumentId,
        int distanceKilometres,
        EwayBillPartB partB,
        CancellationToken cancellationToken = default);

    /// <summary>Updates Part-B on an issued bill — the vehicle changed en route.</summary>
    Task<EwayBillSummary> UpdatePartBAsync(
        Guid ewayBillId,
        EwayBillPartB partB,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels a bill within its 24-hour window.</summary>
    Task<EwayBillSummary> CancelAsync(
        Guid ewayBillId,
        string reasonCode,
        string? remarks = null,
        CancellationToken cancellationToken = default);

    /// <summary>Asks the portal to extend a bill's validity, and records the expiry it grants.</summary>
    Task<EwayBillSummary> ExtendValidityAsync(
        Guid ewayBillId,
        int remainingDistanceKilometres,
        string reasonCode,
        string? remarks = null,
        CancellationToken cancellationToken = default);
}
