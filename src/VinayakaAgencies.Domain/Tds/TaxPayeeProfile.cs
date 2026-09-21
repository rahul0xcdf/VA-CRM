using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Tds;

/// <summary>
/// What a party ledger needs in order to be deducted from: its PAN and its class of person
/// (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// <para>
/// A separate row rather than columns on <c>Ledger</c>. Most ledgers are never a TDS payee — bank
/// accounts, sales, expenses — and widening the chart of accounts with three nullable tax columns to
/// serve the handful that are would put tax concepts into a Phase 1 aggregate that has none. The
/// profile exists only where it means something.
/// </para>
/// <para>
/// <b>PAN presence is the whole point.</b> Section 206AA charges a higher rate where the payee has
/// furnished no PAN, so "we hold no PAN for this party" is a fact the computation reads, not a blank
/// field it ignores. <see cref="HasPan"/> is derived from the stored value rather than kept as a
/// separate flag, because a flag and a value can disagree and the value is the evidence.
/// </para>
/// </remarks>
public sealed class TaxPayeeProfile : Entity, IAuditStamped
{
    /// <summary>A PAN is ten characters: five letters, four digits, one letter.</summary>
    public const int PanLength = 10;

    private TaxPayeeProfile()
    {
        // EF Core materialisation.
    }

    public TaxPayeeProfile(
        Guid companyId,
        Guid ledgerId,
        PayeeCategory payeeCategory,
        string? pan = null,
        bool isDeductionApplicable = true,
        bool declaresDeductingOnPurchases = false)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException(
                "A payee profile must name the party ledger it describes.", nameof(ledgerId));
        }

        CompanyId = companyId;
        LedgerId = ledgerId;
        PayeeCategory = payeeCategory;
        SetPan(pan);
        IsDeductionApplicable = isDeductionApplicable;
        DeclaresDeductingOnPurchases = declaresDeductingOnPurchases;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>The party ledger this profile describes, one profile per ledger.</summary>
    public Guid LedgerId { get; private set; }

    /// <summary>The class of person, which selects the rate row.</summary>
    public PayeeCategory PayeeCategory { get; private set; }

    /// <summary>The payee's PAN as furnished, or null where none has been.</summary>
    public string? Pan { get; private set; }

    /// <summary>
    /// False for a party the owner has decided is outside deduction altogether. Kept as an explicit
    /// switch rather than inferred from "has no section mapped", so that turning it off is a recorded
    /// decision rather than a gap in configuration.
    /// </summary>
    public bool IsDeductionApplicable { get; private set; } = true;

    /// <summary>
    /// True once this party has declared, in writing, that they deduct tax at source on their
    /// purchases from us — which is what switches off our collection on sales to them
    /// (checkpoint C7.6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A declaration, not a deduction.</b> The statute makes 194Q and 206C(1H) mutually
    /// exclusive on one transaction: where the buyer deducts, the seller does not collect. But the
    /// seller cannot see the buyer's books — the only thing the seller has is the buyer's written
    /// declaration, and that is precisely what this flag records. Inferring it from anything else
    /// would be inventing a fact about a third party's tax position.
    /// </para>
    /// <para>
    /// Default false, which collects. That is the safe default of the two: over-collecting is
    /// refundable through the buyer's return, while under-collecting is the seller's liability with
    /// interest.
    /// </para>
    /// </remarks>
    public bool DeclaresDeductingOnPurchases { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>True when a PAN is on file, which decides whether the 206AA uplift applies.</summary>
    public bool HasPan => !string.IsNullOrWhiteSpace(Pan);

    public void SetPayeeCategory(PayeeCategory payeeCategory) => PayeeCategory = payeeCategory;

    public void SetDeductionApplicable(bool isApplicable) => IsDeductionApplicable = isApplicable;

    /// <summary>Records — or withdraws — the buyer's declaration that they deduct on purchases.</summary>
    public void SetDeclaresDeductingOnPurchases(bool declares) =>
        DeclaresDeductingOnPurchases = declares;

    /// <summary>
    /// Records the PAN, normalised to upper case.
    /// </summary>
    /// <remarks>
    /// Length is checked; the character pattern deliberately is not. A PAN whose shape the software
    /// disagrees with is still the PAN the payee furnished, and refusing to record it would leave the
    /// operator unable to enter a real document — while the length check catches the typo that
    /// actually happens, which is a truncated paste.
    /// </remarks>
    public void SetPan(string? pan)
    {
        if (string.IsNullOrWhiteSpace(pan))
        {
            Pan = null;
            return;
        }

        var trimmed = pan.Trim().ToUpperInvariant();

        if (trimmed.Length != PanLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pan),
                pan,
                $"A PAN is exactly {PanLength} characters.");
        }

        Pan = trimmed;
    }
}
