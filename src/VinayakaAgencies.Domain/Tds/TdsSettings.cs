using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Tds;

/// <summary>
/// Per-company TDS/TCS configuration: which liability ledgers the tax posts to, and whether the two
/// turnover-linked sections are in force (phase-7-tds-payroll-banking.md §2, §4a D2).
/// </summary>
/// <remarks>
/// <para>
/// <b>194Q and 206C(1H) are flags, never inferred.</b> Both apply only above a turnover limit, and
/// the owner declares whether they apply rather than the software computing turnover and deciding
/// for itself. This follows the Phase 6 precedent for e-invoicing applicability and exists for the
/// same reason: turnover as the books compute it is not necessarily turnover as the statute defines
/// it, and a compliance obligation that switches itself on silently is worse than one that is
/// declared. With both off the two sections simply never resolve, so a business below the limit
/// carries two booleans and no behaviour.
/// </para>
/// <para>
/// <b>The section codes live in the master, not here.</b> This row points at the two sections by id
/// — chosen by the owner from the section master — precisely so that "194Q" and "206C(1H)" appear
/// nowhere in <c>src/</c> (checkpoint C7.5). When the 2025 Act renumbers them, the owner repoints
/// these two ids at the new rows and nothing is rebuilt.
/// </para>
/// </remarks>
public sealed class TdsSettings : Entity, IAuditStamped
{
    private TdsSettings()
    {
        // EF Core materialisation.
    }

    public TdsSettings(Guid companyId, string tdsPayableLedgerName, string tcsPayableLedgerName)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        CompanyId = companyId;
        SetTdsPayableLedgerName(tdsPayableLedgerName);
        SetTcsPayableLedgerName(tcsPayableLedgerName);
    }

    public Guid CompanyId { get; private set; }

    /// <summary>The liability ledger tax withheld from suppliers is credited to.</summary>
    public string TdsPayableLedgerName { get; private set; } = string.Empty;

    /// <summary>The liability ledger tax collected from buyers is credited to.</summary>
    public string TcsPayableLedgerName { get; private set; } = string.Empty;

    /// <summary>
    /// True once the owner declares the business liable to deduct on purchases of goods. Off by
    /// default: a business that has never crossed the limit should not have to turn anything off.
    /// </summary>
    public bool IsPurchaseOfGoodsDeductionEnabled { get; private set; }

    /// <summary>The section purchase-of-goods deduction resolves through, once enabled.</summary>
    public Guid? PurchaseOfGoodsSectionId { get; private set; }

    /// <summary>True once the owner declares the business liable to collect on sales of goods.</summary>
    public bool IsSaleOfGoodsCollectionEnabled { get; private set; }

    /// <summary>The section sale-of-goods collection resolves through, once enabled.</summary>
    public Guid? SaleOfGoodsSectionId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public void SetTdsPayableLedgerName(string ledgerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ledgerName);

        TdsPayableLedgerName = ledgerName.Trim();
    }

    public void SetTcsPayableLedgerName(string ledgerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ledgerName);

        TcsPayableLedgerName = ledgerName.Trim();
    }

    /// <summary>
    /// Turns purchase-of-goods deduction on against a named section, or off.
    /// </summary>
    /// <remarks>
    /// Enabling without naming a section is refused rather than accepted and ignored. A flag that is
    /// on but resolves to nothing is the worst of both: the owner believes they are compliant and no
    /// deduction is ever made.
    /// </remarks>
    public void ConfigurePurchaseOfGoods(bool isEnabled, Guid? sectionId)
    {
        if (isEnabled && (sectionId is null || sectionId == Guid.Empty))
        {
            throw new ArgumentException(
                "Enabling purchase-of-goods deduction needs the section it deducts under.",
                nameof(sectionId));
        }

        IsPurchaseOfGoodsDeductionEnabled = isEnabled;
        PurchaseOfGoodsSectionId = isEnabled ? sectionId : null;
    }

    /// <summary>Turns sale-of-goods collection on against a named section, or off.</summary>
    public void ConfigureSaleOfGoods(bool isEnabled, Guid? sectionId)
    {
        if (isEnabled && (sectionId is null || sectionId == Guid.Empty))
        {
            throw new ArgumentException(
                "Enabling sale-of-goods collection needs the section it collects under.",
                nameof(sectionId));
        }

        IsSaleOfGoodsCollectionEnabled = isEnabled;
        SaleOfGoodsSectionId = isEnabled ? sectionId : null;
    }
}
