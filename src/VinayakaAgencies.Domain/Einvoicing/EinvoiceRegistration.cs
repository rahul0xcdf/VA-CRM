using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Einvoicing;

/// <summary>
/// One invoice's registration with the IRP: its IRN, the portal's acknowledgement, and the signed
/// artefacts it returned (phase-6-einvoice-eway.md §2).
/// </summary>
/// <remarks>
/// <para>
/// The row is created <em>before</em> the portal is called, with the IRN already computed — the IRN
/// is a pure function of four values we hold, so there is nothing to wait for. That ordering is what
/// makes the phase's idempotency requirement work: the unique index on <c>(CompanyId, Irn)</c> means
/// a retry after a timeout finds this row instead of creating a second one, so a duplicate IRN is
/// impossible at the database level rather than by convention (checkpoint C6.5).
/// </para>
/// <para>
/// The portal's own <c>AckDt</c> is kept verbatim in <see cref="PortalAckDateText"/> as well as
/// parsed into <see cref="AcknowledgedAtUtc"/>. Both are needed, for different reasons: the signed
/// QR must carry the timestamp exactly as NIC wrote it, while the 24-hour cancellation window is
/// arithmetic and wants a real instant. Re-rendering either from the other would risk drift in a
/// value that sits inside a signature.
/// </para>
/// <para>
/// Nothing here stores a total that could drift from the books — the invoice stays the source of
/// truth for money, and this row only records what the portal was told and what it said back.
/// </para>
/// </remarks>
public sealed class EinvoiceRegistration : Entity, IAuditStamped
{
    /// <summary>How long after acknowledgement an e-invoice may still be cancelled.</summary>
    public static readonly TimeSpan CancellationWindow = TimeSpan.FromHours(24);

    private EinvoiceRegistration()
    {
        // EF Core materialisation.
    }

    public EinvoiceRegistration(
        Guid companyId,
        Guid tradeDocumentId,
        string supplierGstin,
        EinvoiceDocumentType documentType,
        string documentNumber,
        DateOnly documentDate)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (tradeDocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "A registration must name the document it registers.", nameof(tradeDocumentId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(supplierGstin);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);

        CompanyId = companyId;
        TradeDocumentId = tradeDocumentId;
        SupplierGstin = supplierGstin.Trim().ToUpperInvariant();
        DocumentType = documentType;
        DocumentNumber = documentNumber.Trim();
        DocumentDate = documentDate;

        // Computed here, once, so the IRN and the row carrying it can never disagree.
        Irn = IrnFormula.Compute(SupplierGstin, documentDate, documentType, DocumentNumber);
        Status = EinvoiceStatus.Pending;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>The posted tax invoice, credit note or debit note this registers.</summary>
    public Guid TradeDocumentId { get; private set; }

    public string SupplierGstin { get; private set; } = string.Empty;

    public EinvoiceDocumentType DocumentType { get; private set; }

    public string DocumentNumber { get; private set; } = string.Empty;

    public DateOnly DocumentDate { get; private set; }

    /// <summary>The 64-character IRN — computed locally, and the idempotency key for every call.</summary>
    public string Irn { get; private set; } = string.Empty;

    public EinvoiceStatus Status { get; private set; }

    /// <summary>The portal's acknowledgement number, once it has answered.</summary>
    public long? AcknowledgementNumber { get; private set; }

    /// <summary>The portal's <c>AckDt</c> parsed to an instant, for window arithmetic.</summary>
    public DateTimeOffset? AcknowledgedAtUtc { get; private set; }

    /// <summary>The portal's <c>AckDt</c> exactly as sent — this is what the signature covers.</summary>
    public string? PortalAckDateText { get; private set; }

    /// <summary>The signed invoice JWS returned by the portal.</summary>
    public string? SignedInvoiceJws { get; private set; }

    /// <summary>The signed QR JWS. This, not anything we build, is what gets printed.</summary>
    public string? SignedQrCodeJws { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public string? CancellationReasonCode { get; private set; }

    public string? CancellationRemarks { get; private set; }

    /// <summary>The portal's last error text, kept so a rejection stays diagnosable afterwards.</summary>
    public string? LastErrorText { get; private set; }

    /// <summary>How many times the portal has been called for this IRN. Diagnostic, not a gate.</summary>
    public int AttemptCount { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    /// <summary>True once the portal has registered this document.</summary>
    public bool IsRegistered => Status is EinvoiceStatus.Generated;

    /// <summary>True when another attempt at the portal is worth making.</summary>
    public bool IsRetryable => Status is EinvoiceStatus.Pending;

    /// <summary>The instant the 24-hour cancellation window closes, once acknowledged.</summary>
    public DateTimeOffset? CancellableUntilUtc =>
        AcknowledgedAtUtc is { } acknowledged ? acknowledged + CancellationWindow : null;

    /// <summary>
    /// True when this registration may still be cancelled at the portal. Once the window closes a
    /// credit note is the only route, and that is a bookkeeping act rather than an IRP call.
    /// </summary>
    public bool CanCancelAt(DateTimeOffset nowUtc) =>
        Status is EinvoiceStatus.Generated
        && CancellableUntilUtc is { } deadline
        && nowUtc <= deadline;

    /// <summary>Records one more attempt against the portal.</summary>
    public void RecordAttempt() => AttemptCount++;

    /// <summary>Settles the row from a successful portal response.</summary>
    public void MarkGenerated(
        long acknowledgementNumber,
        DateTimeOffset acknowledgedAtUtc,
        string portalAckDateText,
        string signedInvoiceJws,
        string signedQrCodeJws)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portalAckDateText);
        ArgumentException.ThrowIfNullOrWhiteSpace(signedInvoiceJws);
        ArgumentException.ThrowIfNullOrWhiteSpace(signedQrCodeJws);

        if (Status is EinvoiceStatus.Cancelled)
        {
            throw new InvalidOperationException(
                "A cancelled e-invoice cannot be re-registered under the same IRN.");
        }

        AcknowledgementNumber = acknowledgementNumber;
        AcknowledgedAtUtc = acknowledgedAtUtc;
        PortalAckDateText = portalAckDateText.Trim();
        SignedInvoiceJws = signedInvoiceJws;
        SignedQrCodeJws = signedQrCodeJws;
        LastErrorText = null;
        Status = EinvoiceStatus.Generated;
    }

    /// <summary>Records a portal rejection. The document must change before a retry is worthwhile.</summary>
    public void MarkRejected(string errorText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorText);

        LastErrorText = errorText.Trim();
        Status = EinvoiceStatus.Rejected;
    }

    /// <summary>
    /// Records a call that never came back — a timeout or a transport failure. The row stays
    /// <see cref="EinvoiceStatus.Pending"/> and retryable, which is the entire point of that state.
    /// </summary>
    public void MarkUnresolved(string errorText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorText);

        LastErrorText = errorText.Trim();
        Status = EinvoiceStatus.Pending;
    }

    /// <summary>Records a cancellation the portal accepted.</summary>
    public void MarkCancelled(
        DateTimeOffset cancelledAtUtc,
        string reasonCode,
        string? remarks = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        if (Status is not EinvoiceStatus.Generated)
        {
            throw new InvalidOperationException(
                "Only a registered e-invoice can be cancelled at the portal.");
        }

        if (!CanCancelAt(cancelledAtUtc))
        {
            throw new InvalidOperationException(
                "The 24-hour cancellation window has closed; issue a credit note instead.");
        }

        CancelledAtUtc = cancelledAtUtc;
        CancellationReasonCode = reasonCode.Trim();
        CancellationRemarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
        Status = EinvoiceStatus.Cancelled;
    }
}
