using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Accounting;

/// <summary>
/// One posting line: a signed amount against a ledger.
/// </summary>
/// <remarks>
/// The amount is **signed** — positive is a debit, negative a credit — rather than held as separate
/// Dr and Cr columns. With one column the balance invariant is a single exact comparison
/// (<c>Σ = 0</c>), and it is structurally impossible for a row to carry both a debit and a credit,
/// which is a state a two-column design has to validate against forever.
/// <para>
/// Insert-only (ADR-0002). A correction is a new revision carrying new lines; these are never
/// updated or deleted, which is what lets the day book show what was actually posted at the time.
/// </para>
/// </remarks>
public sealed class JournalLine : Entity, IBelongsToAggregate
{
    private JournalLine()
    {
        // EF Core materialisation.
    }

    public JournalLine(
        Guid companyId,
        Guid voucherId,
        Guid voucherRevisionId,
        int lineNumber,
        Guid ledgerId,
        decimal amount,
        Guid? costCentreId = null,
        string? narration = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (voucherId == Guid.Empty)
        {
            throw new ArgumentException(
                "A journal line must name the voucher it belongs to.", nameof(voucherId));
        }

        if (voucherRevisionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A journal line must name its revision.", nameof(voucherRevisionId));
        }

        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException("A journal line must name a ledger.", nameof(ledgerId));
        }

        if (lineNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber), lineNumber, "Must be at least 1.");
        }

        if (amount == 0m)
        {
            throw new ArgumentException(
                "A journal line must carry a non-zero amount; a zero line records nothing.",
                nameof(amount));
        }

        // Throws if the value carries more than four decimal places (ADR-0003), so an inexact
        // amount is refused here rather than silently rounded on its way to the database.
        AmountMinor = MoneyScale.ToMinor(amount);

        CompanyId = companyId;
        VoucherId = voucherId;
        VoucherRevisionId = voucherRevisionId;
        LineNumber = lineNumber;
        LedgerId = ledgerId;
        CostCentreId = costCentreId;
        Narration = string.IsNullOrWhiteSpace(narration) ? null : narration.Trim();
    }

    public Guid CompanyId { get; private set; }

    /// <summary>
    /// Copy of the parent revision's <see cref="VoucherRevision.VoucherId"/>, carried for the same
    /// reason the revision carries it: the edit log files every line directly under its voucher,
    /// without a join (spec §8.2, §11.1).
    /// </summary>
    public Guid VoucherId { get; private set; }

    public Guid VoucherRevisionId { get; private set; }

    public int LineNumber { get; private set; }

    public Guid LedgerId { get; private set; }

    /// <summary>
    /// The amount in minor units of 1/10,000 (ADR-0003). Positive is a debit, negative a credit.
    /// </summary>
    public long AmountMinor { get; private set; }

    /// <summary>
    /// Optional cost-centre dimension. Nullable and unused in Phase 1 by design (decision D3):
    /// adding this column to a populated journal table later is far more expensive than shipping it
    /// empty now.
    /// </summary>
    public Guid? CostCentreId { get; private set; }

    public string? Narration { get; private set; }

    /// <summary>The signed amount in rupees.</summary>
    public decimal Amount => MoneyScale.FromMinor(AmountMinor);

    public bool IsDebit => AmountMinor > 0;

    public bool IsCredit => AmountMinor < 0;

    /// <summary>The debit column of a two-column presentation; zero on a credit line.</summary>
    public decimal DebitAmount => AmountMinor > 0 ? MoneyScale.FromMinor(AmountMinor) : 0m;

    /// <summary>The credit column of a two-column presentation, shown positive; zero on a debit line.</summary>
    public decimal CreditAmount => AmountMinor < 0 ? MoneyScale.FromMinor(-AmountMinor) : 0m;

    /// <summary>The voucher this line belongs under in the edit log (§11.1), reached through its
    /// revision. Explicit rather than inferred: a journal line carries several foreign keys and a
    /// guessed aggregate would file the change under the wrong voucher.</summary>
    string IBelongsToAggregate.AggregateType => nameof(Voucher);

    string IBelongsToAggregate.AggregateId => VoucherId.ToString("D");
}
