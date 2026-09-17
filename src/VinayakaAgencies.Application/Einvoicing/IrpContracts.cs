using VinayakaAgencies.Domain.Einvoicing;

namespace VinayakaAgencies.Application.Einvoicing;

/// <summary>
/// How one call to the IRP ended (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// Three outcomes, not two, and the third is the important one. A portal that says "no" and a portal
/// that says nothing at all demand opposite responses: a rejection means the document is wrong and
/// retrying it will fail identically, while silence means the document may well be registered and
/// the only safe move is to ask again. Collapsing them into a single failure would make the spec's
/// "a network outage never leaves an invoice in an ambiguous state" impossible to honour.
/// </remarks>
public enum IrpOutcome
{
    /// <summary>The portal accepted the request.</summary>
    Success = 1,

    /// <summary>The portal answered, and refused. Not retryable as-is.</summary>
    Rejected = 2,

    /// <summary>No answer arrived — timeout, dropped connection, unreachable host. Retryable.</summary>
    NoResponse = 3,
}

/// <summary>
/// The transport-level record of one IRP call, carried alongside every response.
/// </summary>
/// <remarks>
/// The bodies here are already redacted by the client. They are surfaced on the result rather than
/// logged inside the client so that the caller can persist them as an <see cref="EinvoiceApiCall"/>
/// in the same unit of work as the state change they justify — a request/response pair that commits
/// separately from its outcome is a pair that can go missing exactly when it is needed.
/// </remarks>
/// <param name="Outcome">Accepted, refused, or no answer.</param>
/// <param name="Endpoint">The endpoint called.</param>
/// <param name="RedactedRequestBody">Request body, credentials already removed.</param>
/// <param name="RedactedResponseBody">Response body, credentials already removed.</param>
/// <param name="HttpStatusCode">Transport status, when a response arrived.</param>
/// <param name="ErrorCode">The portal's own error code, when it gave one.</param>
/// <param name="ErrorMessage">The portal's error text, or the transport failure's message.</param>
public sealed record IrpCallRecord(
    IrpOutcome Outcome,
    string Endpoint,
    string RedactedRequestBody,
    string RedactedResponseBody,
    int? HttpStatusCode = null,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public bool Succeeded => Outcome is IrpOutcome.Success;

    /// <summary>True when asking again is the right move rather than a duplicate risk.</summary>
    public bool IsRetryable => Outcome is IrpOutcome.NoResponse;
}

/// <summary>The credentials and endpoint one IRP call needs, resolved and decrypted for that call.</summary>
/// <remarks>
/// Passed per call rather than held on the client so that plaintext credentials exist for as short a
/// window as possible, and so nothing long-lived holds a field a logger could reach (checkpoint
/// C6.6).
/// </remarks>
/// <param name="Environment">Sandbox or production.</param>
/// <param name="ClientId">NIC client id.</param>
/// <param name="ClientSecret">NIC client secret.</param>
/// <param name="Username">NIC API username.</param>
/// <param name="Password">NIC API password.</param>
/// <param name="Gstin">The supplier GSTIN the credentials belong to.</param>
public sealed record IrpCredentials(
    IrpEnvironment Environment,
    string ClientId,
    string ClientSecret,
    string Username,
    string Password,
    string Gstin);

/// <summary>What the portal returned for an IRN generation attempt.</summary>
/// <param name="Call">The transport record for this attempt.</param>
/// <param name="AcknowledgementNumber">The portal's <c>AckNo</c>.</param>
/// <param name="AcknowledgementDateText">The portal's <c>AckDt</c>, verbatim.</param>
/// <param name="Irn">The IRN the portal computed — compared against ours for checkpoint C6.4.</param>
/// <param name="SignedInvoiceJws">The signed invoice.</param>
/// <param name="SignedQrCodeJws">The signed QR, which is what gets printed.</param>
public sealed record IrpIrnResponse(
    IrpCallRecord Call,
    long? AcknowledgementNumber = null,
    string? AcknowledgementDateText = null,
    string? Irn = null,
    string? SignedInvoiceJws = null,
    string? SignedQrCodeJws = null);

/// <summary>What the portal returned for a cancellation.</summary>
/// <param name="Call">The transport record for this attempt.</param>
/// <param name="CancelledDateText">The portal's cancellation timestamp, verbatim.</param>
public sealed record IrpCancelResponse(IrpCallRecord Call, string? CancelledDateText = null);

/// <summary>What the portal returned for an e-way bill request.</summary>
/// <param name="Call">The transport record for this attempt.</param>
/// <param name="EwayBillNumber">The portal's <c>EwbNo</c>.</param>
/// <param name="GeneratedDateText">The portal's <c>EwbDt</c>, verbatim.</param>
/// <param name="ValidUntilText">The portal's <c>EwbValidTill</c>, verbatim.</param>
public sealed record IrpEwayBillResponse(
    IrpCallRecord Call,
    long? EwayBillNumber = null,
    string? GeneratedDateText = null,
    string? ValidUntilText = null);

/// <summary>One line of an e-invoice, already carrying its posted tax snapshot.</summary>
/// <remarks>
/// Every money figure arrives in minor units of 1/10,000 (ADR-0003) and is converted to decimal once,
/// at the JSON boundary. Nothing is recomputed here: the taxes are the snapshot Phase 5 posted with
/// the invoice, so what the portal is told always matches what the books say.
/// </remarks>
public sealed record EinvoiceLine(
    string HsnCode,
    string Description,
    string UnitCode,
    long QuantityMinor,
    long UnitPriceMinor,
    long TaxableValueMinor,
    long GstRatePercentMinor,
    long IgstMinor,
    long CgstMinor,
    long SgstMinor,
    long CessMinor)
{
    /// <summary>Line total as the portal wants it: taxable value plus every tax on the line.</summary>
    public long TotalValueMinor =>
        TaxableValueMinor + IgstMinor + CgstMinor + SgstMinor + CessMinor;
}

/// <summary>Everything the IRP needs about one document, assembled from the books.</summary>
/// <remarks>
/// This is the seam that keeps the payload builder testable without a network or a database: the
/// service gathers these facts, and turning them into NIC's JSON is a pure function of this record.
/// </remarks>
public sealed record EinvoiceRequest(
    EinvoiceDocumentType DocumentType,
    string DocumentNumber,
    DateOnly DocumentDate,
    bool IsReverseCharge,
    EinvoicePartyDetails Seller,
    EinvoicePartyDetails Buyer,
    string PlaceOfSupplyStateCode,
    IReadOnlyList<EinvoiceLine> Lines,
    long RoundOffMinor = 0L);

/// <summary>A party on an e-invoice.</summary>
/// <param name="Gstin">GSTIN, or <c>URP</c> for an unregistered buyer.</param>
/// <param name="LegalName">Legal name as registered.</param>
/// <param name="Address">Address line.</param>
/// <param name="City">City or town.</param>
/// <param name="StateCode">Two-digit state code.</param>
/// <param name="Pincode">Six-digit pincode.</param>
public sealed record EinvoicePartyDetails(
    string Gstin,
    string LegalName,
    string? Address = null,
    string? City = null,
    string? StateCode = null,
    int? Pincode = null);
