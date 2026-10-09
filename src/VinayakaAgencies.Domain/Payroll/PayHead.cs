using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Payroll;

/// <summary>
/// One line a pay slip can carry, and how its amount is arrived at
/// (phase-7-tds-payroll-banking.md §4a D3 — "pay heads as configuration").
/// </summary>
/// <remarks>
/// <para>
/// A head names a <see cref="LedgerId"/> because a salary voucher has to post somewhere, and which
/// ledger is the owner's chart-of-accounts decision rather than the software's. Basic and HRA
/// usually go to one salary expense head; PF employee share goes to a PF payable liability; the
/// employer's share goes to its own expense and its own liability.
/// </para>
/// <para>
/// <see cref="StatuteKind"/> is set exactly on the heads whose figures the statutory engine
/// produces, and it is what lets a flag switch a head off wholesale. A head with a statute and a
/// flag that is off never computes and never posts — checkpoint C7.7 — and no arm of any
/// <c>switch</c> has to know which head is "the PF one" by name.
/// </para>
/// </remarks>
public sealed class PayHead : Entity, IAuditStamped
{
    private PayHead()
    {
        // EF Core materialisation.
    }

    public PayHead(
        Guid companyId,
        string name,
        PayHeadType type,
        PayComputationBasis basis,
        Guid ledgerId,
        StatuteKind? statute = null,
        int sortOrder = 0)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException(
                "A pay head must name the ledger its amount posts to.", nameof(ledgerId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // A statutory basis without a statute would be a head the engine is asked to compute and
        // has no rule for; a statute without the statutory basis would be a head the owner can type
        // a figure into that the engine then overwrites. Both are silent wrongness.
        if (basis == PayComputationBasis.Statutory && statute is null)
        {
            throw new ArgumentException(
                "A statutory pay head must name which statute produces its figure.", nameof(statute));
        }

        if (basis != PayComputationBasis.Statutory && statute is not null)
        {
            throw new ArgumentException(
                "Only a head on the statutory basis may name a statute — otherwise its amount "
                + "comes from the salary structure and the statute would never be consulted.",
                nameof(statute));
        }

        CompanyId = companyId;
        SetName(name);
        Type = type;
        Basis = basis;
        LedgerId = ledgerId;
        Statute = statute;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Comparison form of <see cref="Name"/> for the company-scoped unique index.</summary>
    public string NameNormalised { get; private set; } = string.Empty;

    public PayHeadType Type { get; private set; }

    public PayComputationBasis Basis { get; private set; }

    /// <summary>The ledger this head's amount posts to on the salary voucher.</summary>
    public Guid LedgerId { get; private set; }

    /// <summary>
    /// The statute that produces this head's figure, on a statutory head; null on every other.
    /// </summary>
    public StatuteKind? Statute { get; private set; }

    /// <summary>Where the head sits on a printed pay slip. Presentation, not arithmetic.</summary>
    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    /// <summary>True for a head whose amount the statutory engine produces rather than the structure.</summary>
    public bool IsStatutory => Basis == PayComputationBasis.Statutory;

    public void SetName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        NameNormalised = NameNormaliser.Normalise(name);
    }

    public void SetLedger(Guid ledgerId)
    {
        if (ledgerId == Guid.Empty)
        {
            throw new ArgumentException(
                "A pay head must name the ledger its amount posts to.", nameof(ledgerId));
        }

        LedgerId = ledgerId;
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
