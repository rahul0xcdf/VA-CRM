using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Application.Einvoicing;

/// <summary>One e-invoice registration as the screens and reports see it.</summary>
/// <param name="Id">Registration id.</param>
/// <param name="TradeDocumentId">The document registered.</param>
/// <param name="DocumentNumber">Invoice number.</param>
/// <param name="DocumentDate">Invoice date.</param>
/// <param name="DocumentTypeCode">NIC document-type code.</param>
/// <param name="Irn">The IRN.</param>
/// <param name="Status">Where it stands with the portal.</param>
/// <param name="AcknowledgementNumber">The portal's <c>AckNo</c>.</param>
/// <param name="PortalAckDateText">The portal's <c>AckDt</c>, verbatim.</param>
/// <param name="SignedQrCodeJws">The signed QR, for printing.</param>
/// <param name="CancellableUntilUtc">When the 24-hour cancellation window closes.</param>
/// <param name="AttemptCount">Calls made for this IRN.</param>
/// <param name="LastErrorText">The portal's last complaint, if any.</param>
public sealed record EinvoiceRegistrationSummary(
    Guid Id,
    Guid TradeDocumentId,
    string DocumentNumber,
    DateOnly DocumentDate,
    string DocumentTypeCode,
    string Irn,
    EinvoiceStatus Status,
    long? AcknowledgementNumber,
    string? PortalAckDateText,
    string? SignedQrCodeJws,
    DateTimeOffset? CancellableUntilUtc,
    int AttemptCount,
    string? LastErrorText);

/// <summary>
/// The outcome of asking for an IRN, including whether the portal was actually called.
/// </summary>
/// <remarks>
/// <see cref="WasAlreadyRegistered"/> is what a caller needs to distinguish a fresh registration from
/// a retry that found the document already done. Both are successes, and conflating them would hide
/// exactly the case checkpoint C6.5 is about.
/// </remarks>
/// <param name="Registration">The registration as it now stands.</param>
/// <param name="WasAlreadyRegistered">True when this call found an existing registration rather than creating one.</param>
/// <param name="PortalIrnMatchedOurs">
/// Whether the portal's IRN equalled the one we computed. Null when the portal did not return one.
/// This is the observation checkpoint C6.4 asserts on.
/// </param>
public sealed record EinvoiceGenerationResult(
    EinvoiceRegistrationSummary Registration,
    bool WasAlreadyRegistered,
    bool? PortalIrnMatchedOurs);

/// <summary>One e-way bill as the screens see it.</summary>
public sealed record EwayBillSummary(
    Guid Id,
    Guid TradeDocumentId,
    string? Irn,
    long? EwayBillNumber,
    EwayBillStatus Status,
    long ConsignmentValueMinor,
    int DistanceKilometres,
    TransportMode? TransportMode,
    VehicleType VehicleType,
    string? VehicleNumber,
    string? TransporterGstin,
    string? TransporterName,
    string? TransportDocumentNumber,
    DateOnly? TransportDocumentDate,
    DateTimeOffset? GeneratedAtUtc,
    DateTimeOffset? ValidUntilUtc,
    bool IsPartBComplete,
    string? LastErrorText);

/// <summary>E-invoicing configuration as the settings screen sees it.</summary>
/// <remarks>
/// Carries no credential, not even a masked one — only <see cref="HasCredentials"/>. A DTO with a
/// masked secret on it is a DTO that will eventually be serialised somewhere by someone, and the
/// screen has no reason to know more than whether credentials are present.
/// </remarks>
public sealed record EinvoiceSettingsSummary(
    bool IsEinvoicingApplicable,
    IrpEnvironment Environment,
    long EwayBillThresholdMinor,
    bool HasCredentials);

/// <summary>A request to store new NIC credentials.</summary>
/// <remarks>
/// Plaintext, necessarily — this is the one hop between the entry field and the protector. Nothing
/// holds an instance of this beyond the call that encrypts it.
/// </remarks>
public sealed record SaveIrpCredentialsRequest(
    string ClientId,
    string ClientSecret,
    string Username,
    string Password);

/// <summary>Raised when a document cannot be submitted to the portal as it stands.</summary>
/// <remarks>
/// Separate from a portal rejection: this fires before any call is made, because something the
/// portal would certainly refuse is missing — no GSTIN, an incomplete address, no lines, no HSN on a
/// line. Failing here keeps a pointless call, and a pointless <see cref="EinvoiceApiCall"/> row, off
/// the record, and gives the user a message naming the field rather than a portal error code.
/// </remarks>
public sealed class EinvoiceNotSubmittableException(string message) : InvalidOperationException(message);
