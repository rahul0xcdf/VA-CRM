using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Application.Einvoicing;

/// <summary>
/// The port onto NIC's Invoice Registration Portal and e-way bill API
/// (phase-6-einvoice-eway.md §2, §4a).
/// </summary>
/// <remarks>
/// <para>
/// Declared here and implemented in Infrastructure, for the ordinary layering reason and one
/// specific one: <c>open-questions.md</c> #10 is still open, so no implementation of this interface
/// can be exercised against the real sandbox yet. Everything above this seam is therefore verified
/// against a recorded double that speaks the same contract, which is what makes checkpoints C6.4,
/// C6.5 and C6.6 reachable today. It also means a later move to a GSP is a second implementation
/// rather than a rewrite (§4a).
/// </para>
/// <para>
/// No method here throws for a portal refusal or a timeout — both are ordinary, expected answers and
/// come back as an <see cref="IrpOutcome"/> on the result. Exceptions are reserved for programmer
/// error. A client that threw on timeout would push every caller into a catch block to do the one
/// thing that actually matters, which is to record the attempt and leave the row retryable.
/// </para>
/// <para>
/// Credentials are passed per call rather than configured once, so plaintext never outlives the call
/// that needed it (checkpoint C6.6).
/// </para>
/// </remarks>
public interface IIrpClient
{
    /// <summary>
    /// Registers a document and returns the portal's IRN, acknowledgement and signed artefacts.
    /// </summary>
    Task<IrpIrnResponse> GenerateIrnAsync(
        IrpCredentials credentials,
        EinvoiceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the portal what it knows about an IRN. This is how an unresolved attempt is settled: if
    /// the portal already has it, the earlier call did land and no second registration is attempted
    /// (checkpoint C6.5).
    /// </summary>
    Task<IrpIrnResponse> GetByIrnAsync(
        IrpCredentials credentials,
        string irn,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels a registered IRN within its 24-hour window.</summary>
    Task<IrpCancelResponse> CancelIrnAsync(
        IrpCredentials credentials,
        string irn,
        string reasonCode,
        string? remarks,
        CancellationToken cancellationToken = default);

    /// <summary>Generates an e-way bill against an already-registered IRN.</summary>
    Task<IrpEwayBillResponse> GenerateEwayBillByIrnAsync(
        IrpCredentials credentials,
        string irn,
        EwayBillPartB partB,
        int distanceKilometres,
        CancellationToken cancellationToken = default);

    /// <summary>Updates Part-B on an issued bill — the vehicle changed en route.</summary>
    Task<IrpEwayBillResponse> UpdateEwayBillPartBAsync(
        IrpCredentials credentials,
        long ewayBillNumber,
        EwayBillPartB partB,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels an e-way bill within its 24-hour window.</summary>
    Task<IrpCancelResponse> CancelEwayBillAsync(
        IrpCredentials credentials,
        long ewayBillNumber,
        string reasonCode,
        string? remarks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the portal to extend a bill's validity. The portal grants the new expiry, so the caller
    /// records what it is told rather than computing it.
    /// </summary>
    Task<IrpEwayBillResponse> ExtendEwayBillValidityAsync(
        IrpCredentials credentials,
        long ewayBillNumber,
        int remainingDistanceKilometres,
        string reasonCode,
        string? remarks,
        CancellationToken cancellationToken = default);
}

/// <summary>The Part-B transport details one e-way bill call carries (phase-6-einvoice-eway.md §4a).</summary>
/// <param name="TransportMode">Road, rail, air or ship — this decides which fields are required.</param>
/// <param name="VehicleType">Regular or over-dimensional cargo, which fixes the validity slab.</param>
/// <param name="VehicleNumber">Required by road.</param>
/// <param name="TransporterGstin">Transporter's GSTIN, where there is one.</param>
/// <param name="TransporterName">Transporter's name.</param>
/// <param name="TransportDocumentNumber">Required for rail, air and ship.</param>
/// <param name="TransportDocumentDate">Required for rail, air and ship.</param>
public sealed record EwayBillPartB(
    TransportMode TransportMode,
    VehicleType VehicleType,
    string? VehicleNumber = null,
    string? TransporterGstin = null,
    string? TransporterName = null,
    string? TransportDocumentNumber = null,
    DateOnly? TransportDocumentDate = null);
