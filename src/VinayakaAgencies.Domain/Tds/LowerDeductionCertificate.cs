using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Tds;

/// <summary>
/// A Section 197 lower- or nil-deduction certificate: the assessing officer's authority to deduct a
/// payee at less than the scheduled rate (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// <para>
/// Per payee <em>and</em> per section, with its own validity dates, because that is how the
/// certificate is issued — one covers contractor payments and says nothing about rent. The
/// certificate rate wins over the schedule for payments inside its window and for nothing outside
/// it, so a payment dated the day after expiry deducts at the full rate without anyone remembering
/// to switch something off.
/// </para>
/// <para>
/// <b>A nil certificate is a zero rate, not an absence.</b> Both are expressed the same way here, and
/// a deduction of zero under a certificate is still recorded as a deduction — it has to appear in the
/// return, and "nothing was deducted because a certificate said so" is a different fact from "nothing
/// was deducted because nobody configured the section".
/// </para>
/// <para>
/// <b>The ceiling is optional because certificates differ.</b> Some authorise a lower rate up to a
/// stated amount and the full rate beyond it; others carry no cap. Null means uncapped rather than
/// zero, because zero would silently mean "the certificate never applies".
/// </para>
/// </remarks>
public sealed class LowerDeductionCertificate : Entity, IAuditStamped
{
    private LowerDeductionCertificate()
    {
        // EF Core materialisation.
    }

    public LowerDeductionCertificate(
        Guid companyId,
        Guid ledgerId,
        Guid taxSectionId,
        string certificateNumber,
        long ratePercentMinor,
        DateOnly validFrom,
        DateOnly validTo,
        long? ceilingAmountMinor = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException(
                "A certificate must name the party ledger it was issued for.", nameof(ledgerId));
        }

        if (taxSectionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A certificate must name the section it covers.", nameof(taxSectionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(certificateNumber);
        ArgumentOutOfRangeException.ThrowIfNegative(ratePercentMinor);

        if (validTo < validFrom)
        {
            throw new ArgumentOutOfRangeException(
                nameof(validTo),
                validTo,
                "A certificate cannot expire before it takes effect.");
        }

        if (ceilingAmountMinor is < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ceilingAmountMinor),
                ceilingAmountMinor,
                "A certificate ceiling cannot be negative.");
        }

        CompanyId = companyId;
        LedgerId = ledgerId;
        TaxSectionId = taxSectionId;
        CertificateNumber = certificateNumber.Trim();
        RatePercentMinor = ratePercentMinor;
        ValidFrom = validFrom;
        ValidTo = validTo;
        CeilingAmountMinor = ceilingAmountMinor;
        IsActive = true;
    }

    public Guid CompanyId { get; private set; }

    public Guid LedgerId { get; private set; }

    public Guid TaxSectionId { get; private set; }

    /// <summary>The certificate number as issued, which the return has to quote.</summary>
    public string CertificateNumber { get; private set; } = string.Empty;

    /// <summary>The authorised rate in minor units of 1/10,000 of a percent; zero for a nil certificate.</summary>
    public long RatePercentMinor { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public DateOnly ValidTo { get; private set; }

    /// <summary>
    /// The amount the lower rate is authorised up to, or null where the certificate carries no cap.
    /// </summary>
    public long? CeilingAmountMinor { get; private set; }

    /// <summary>
    /// False once withdrawn. A certificate is deactivated rather than deleted, because deductions
    /// already made under it quote its number on a filed return.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    /// <summary>Whether this certificate authorises the rate for a payment on the given date.</summary>
    public bool Covers(DateOnly date) => IsActive && date >= ValidFrom && date <= ValidTo;

    /// <summary>
    /// Whether the certificate still has room for a payment, given what it has already absorbed this
    /// year. An uncapped certificate always has room.
    /// </summary>
    public bool HasRoomFor(long cumulativeUnderCertificateMinor, long baseAmountMinor) =>
        !CeilingAmountMinor.HasValue
        || cumulativeUnderCertificateMinor + baseAmountMinor <= CeilingAmountMinor.Value;

    public void Withdraw() => IsActive = false;

    /// <summary>
    /// Brings back a certificate withdrawn in error.
    /// </summary>
    /// <remarks>
    /// Exists because withdrawal is a keystroke and the alternative to undoing it is re-keying a
    /// certificate whose number a filed return already quotes — which would leave two rows for one
    /// document. The edit log records both the withdrawal and the reinstatement, so the correction
    /// is visible rather than erased.
    /// </remarks>
    public void Reinstate() => IsActive = true;
}
