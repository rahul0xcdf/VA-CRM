using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Accounting;

/// <summary>
/// One version of a voucher's content, with the journal lines it posted.
/// </summary>
/// <remarks>
/// ADR-0002. An alteration writes a new revision and stamps
/// <see cref="SupersededByRevisionId"/> on the old one; nothing is mutated in place, so the books as
/// they stood at any past moment remain readable. The current revision of a voucher is the one whose
/// <see cref="SupersededByRevisionId"/> is null — and the uniqueness of a voucher number is enforced
/// by a filtered index over exactly that condition.
/// <para>
/// <see cref="CompanyId"/>, <see cref="VoucherTypeId"/>, <see cref="SeriesId"/> and
/// <see cref="FinancialYearId"/> are denormalised copies of the parent <see cref="Voucher"/>'s
/// (spec §5). Neither SQLite nor SQL Server can build a unique index across a join, so without them
/// the filtered index on
/// <c>(CompanyId, VoucherTypeId, SeriesId, FinancialYearId, NumberNormalised)</c> is unbuildable.
/// They are taken from the parent in the constructor and never mutated; all four are immutable on
/// <see cref="Voucher"/>, so the copies cannot drift by construction.
/// </para>
/// </remarks>
public sealed class VoucherRevision : Entity, ICreationStamped
{
    private readonly List<JournalLine> _lines = [];

    private VoucherRevision()
    {
        // EF Core materialisation.
    }

    public VoucherRevision(
        Voucher voucher,
        int revisionNumber,
        DateOnly voucherDate,
        string number,
        bool isManualNumber = false,
        string? narration = null,
        string? referenceNumber = null,
        DateOnly? referenceDate = null,
        string? changeReason = null,
        VoucherStatus status = VoucherStatus.Posted)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        ArgumentException.ThrowIfNullOrWhiteSpace(number);

        if (voucher.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(voucher));
        }

        if (revisionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revisionNumber), revisionNumber, "Revisions start at 1.");
        }

        // Taken from the parent rather than from separate arguments: a caller cannot then pass a
        // voucher type or financial year that disagrees with the voucher the revision belongs to.
        CompanyId = voucher.CompanyId;
        VoucherId = voucher.Id;
        VoucherTypeId = voucher.VoucherTypeId;
        SeriesId = voucher.SeriesId;
        FinancialYearId = voucher.FinancialYearId;
        RevisionNumber = revisionNumber;
        VoucherDate = voucherDate;
        Number = number.Trim();
        NumberNormalised = Normalise(Number);
        IsManualNumber = isManualNumber;
        Narration = string.IsNullOrWhiteSpace(narration) ? null : narration.Trim();
        ReferenceNumber = string.IsNullOrWhiteSpace(referenceNumber) ? null : referenceNumber.Trim();
        ReferenceDate = referenceDate;
        ChangeReason = string.IsNullOrWhiteSpace(changeReason) ? null : changeReason.Trim();
        Status = status;
    }

    public Guid CompanyId { get; private set; }

    public Guid VoucherId { get; private set; }

    /// <summary>Copy of <see cref="Voucher.VoucherTypeId"/>, carried here so the number-uniqueness
    /// index can be built without a join.</summary>
    public Guid VoucherTypeId { get; private set; }

    /// <summary>Copy of <see cref="Voucher.SeriesId"/>. Kept in the uniqueness key alongside
    /// <see cref="VoucherTypeId"/> — which it functionally determines — because ADR-0002 §7 states
    /// the key that way and the redundancy costs nothing.</summary>
    public Guid SeriesId { get; private set; }

    /// <summary>
    /// Copy of <see cref="Voucher.FinancialYearId"/>, and mandatory in the uniqueness key.
    /// </summary>
    /// <remarks>
    /// Decision D2 resets the series counter each financial year, so <c>PMT/0001</c> legitimately
    /// recurs in FY 2026-27 and FY 2027-28. A key without the financial year would reject the second
    /// year's first payment.
    /// </remarks>
    public Guid FinancialYearId { get; private set; }

    public int RevisionNumber { get; private set; }

    public VoucherStatus Status { get; private set; }

    /// <summary>The business date. <see cref="DateOnly"/> because financial-year logic follows the
    /// local calendar, not UTC.</summary>
    public DateOnly VoucherDate { get; private set; }

    public string Number { get; private set; } = string.Empty;

    /// <summary>
    /// Upper-cased, whitespace-stripped form of <see cref="Number"/>, used by the uniqueness index
    /// so that "pmt/0001" and "PMT/0001 " cannot both exist.
    /// </summary>
    public string NumberNormalised { get; private set; } = string.Empty;

    public bool IsManualNumber { get; private set; }

    public string? Narration { get; private set; }

    public string? ReferenceNumber { get; private set; }

    public DateOnly? ReferenceDate { get; private set; }

    /// <summary>Why this revision was created. Required on any revision after the first.</summary>
    public string? ChangeReason { get; private set; }

    /// <summary>Null on the current revision; set once a later revision replaces it.</summary>
    public Guid? SupersededByRevisionId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public IReadOnlyList<JournalLine> Lines => _lines;

    public bool IsCurrent => SupersededByRevisionId is null;

    /// <summary>Sum of every line in minor units. Must be zero for the revision to be postable.</summary>
    public long NetMinor => _lines.Sum(l => l.AmountMinor);

    /// <summary>
    /// Whether debits equal credits — an exact integer comparison, no tolerance (ADR-0003).
    /// </summary>
    /// <remarks>
    /// A cancelled revision carries no lines and is trivially balanced, which is why cancellation is
    /// modelled as an empty revision rather than as a deletion.
    /// </remarks>
    public bool IsBalanced => NetMinor == 0;

    public decimal TotalDebit => MoneyScale.FromMinor(_lines.Where(l => l.IsDebit).Sum(l => l.AmountMinor));

    public decimal TotalCredit => MoneyScale.FromMinor(-_lines.Where(l => l.IsCredit).Sum(l => l.AmountMinor));

    public void AddLine(JournalLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (SupersededByRevisionId is not null)
        {
            throw new InvalidOperationException(
                "A superseded revision is history and cannot take new lines.");
        }

        _lines.Add(line);
    }

    /// <summary>Marks this revision as replaced by a later one.</summary>
    public void SupersedeBy(Guid revisionId)
    {
        if (revisionId == Guid.Empty)
        {
            throw new ArgumentException("RevisionId must not be empty.", nameof(revisionId));
        }

        if (SupersededByRevisionId is not null)
        {
            throw new InvalidOperationException(
                "This revision has already been superseded; history is not rewritten.");
        }

        SupersededByRevisionId = revisionId;
    }

    /// <summary>Upper-cases and strips whitespace so trivially different spellings cannot collide.</summary>
    public static string Normalise(string number) =>
        new(number.Where(c => !char.IsWhiteSpace(c)).Select(char.ToUpperInvariant).ToArray());
}
