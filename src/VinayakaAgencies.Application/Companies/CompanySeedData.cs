using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Application.Companies;

/// <summary>
/// The chart of accounts and voucher setup a new company starts life with, transcribed from
/// <c>specs/phase-1-accounting-core.md</c> §4.3, §4.4, §4.5, §7.1 and §7.2.
/// </summary>
/// <remarks>
/// This is data, not schema: it is written at runtime through <c>IUnitOfWork</c> by
/// <see cref="CompanyProvisioningService"/> and deliberately <b>not</b> through EF's <c>HasData</c>
/// (spec §5, "Seed data does not use EF's <c>HasData</c>"). Migration seeding would bypass
/// <c>SaveChangesAsync</c> and therefore the audit trail, and §11.1 requires every insert of every
/// mapped entity to be logged — the creation of a system-locked row is exactly what an auditor asks
/// about. It also would not work: every row here is <c>CompanyId</c>-scoped, and companies are
/// created at runtime.
/// <para>
/// Kept as one table per spec table so a reviewer can read this file against the spec line by line.
/// </para>
/// </remarks>
internal static class CompanySeedData
{
    /// <summary>Group that carries the derived net-profit figure onto the balance sheet.</summary>
    public const string ProfitAndLossLedgerName = "Profit & Loss A/c";

    /// <summary>
    /// The fifteen primary groups of §4.3, with the exact <c>Nature</c> and
    /// <c>AffectsGrossProfit</c> that table gives. All are seeded <c>IsPrimary</c> and
    /// <c>IsSystem</c>: they match TallyPrime one-to-one so the Phase 8 export maps directly, which
    /// only holds if they cannot be renamed out of shape or re-parented.
    /// </summary>
    public static IReadOnlyList<PrimaryGroupSeed> PrimaryGroups { get; } =
    [
        new("Branch / Divisions", AccountNature.Liability, AffectsGrossProfit: false),
        new("Capital Account", AccountNature.Liability, AffectsGrossProfit: false),
        new("Current Assets", AccountNature.Asset, AffectsGrossProfit: false),
        new("Current Liabilities", AccountNature.Liability, AffectsGrossProfit: false),
        new("Direct Expenses", AccountNature.Expense, AffectsGrossProfit: true),
        new("Direct Incomes", AccountNature.Income, AffectsGrossProfit: true),
        new("Fixed Assets", AccountNature.Asset, AffectsGrossProfit: false),
        new("Indirect Expenses", AccountNature.Expense, AffectsGrossProfit: false),
        new("Indirect Incomes", AccountNature.Income, AffectsGrossProfit: false),
        new("Investments", AccountNature.Asset, AffectsGrossProfit: false),
        new("Loans (Liability)", AccountNature.Liability, AffectsGrossProfit: false),
        new("Misc. Expenses (ASSET)", AccountNature.Asset, AffectsGrossProfit: false),
        new("Purchase Accounts", AccountNature.Expense, AffectsGrossProfit: true),
        new("Sales Accounts", AccountNature.Income, AffectsGrossProfit: true),
        new("Suspense A/c", AccountNature.Liability, AffectsGrossProfit: false),
    ];

    /// <summary>
    /// The thirteen sub-groups of §4.4. <c>Nature</c> and <c>AffectsGrossProfit</c> are not listed
    /// there and are not choices: a sub-group inherits both from its parent, because a sub-group of
    /// Current Assets that claimed to be Income would put balances on the wrong statement.
    /// </summary>
    public static IReadOnlyList<SubGroupSeed> SubGroups { get; } =
    [
        new("Bank Accounts", "Current Assets"),
        new("Bank OD A/c", "Loans (Liability)"),
        new("Cash-in-Hand", "Current Assets"),
        new("Deposits (Asset)", "Current Assets"),
        new("Duties & Taxes", "Current Liabilities"),
        new("Loans & Advances (Asset)", "Current Assets"),
        new("Provisions", "Current Liabilities"),
        new("Reserves & Surplus", "Capital Account"),
        new("Secured Loans", "Loans (Liability)"),
        new("Stock-in-Hand", "Current Assets"),
        new("Sundry Creditors", "Current Liabilities"),
        new("Sundry Debtors", "Current Assets"),
        new("Unsecured Loans", "Loans (Liability)"),
    ];

    /// <summary>
    /// Ledgers L1–L9 of §4.5 — every row the owner approved (open-questions.md #15, Resolved).
    /// </summary>
    /// <remarks>
    /// L10–L12 are deliberately absent and must stay absent. A bank ledger needs a real account
    /// name, number and IFSC; party ledgers arrive with the customer master in Phase 4; and a seeded
    /// GST ledger would invite someone to hardcode a rate against it in Phase 5, when rates must be
    /// data-driven config rows with effective dates.
    /// <para>
    /// Two rows read differently here from how §4.5's <c>Locked</c> column words them, because the
    /// domain model has one lock flag and the column describes three:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>Profit &amp; Loss A/c</c> is the only ledger with
    /// <c>AllowsManualPosting = false</c>. Its balance-sheet figure is computed from income and
    /// expense (§8.4), so a manual posting to it would be a stored balance in all but name.</description></item>
    /// <item><description><c>Opening Balance Difference</c> is <c>IsSystem</c> (no delete, no
    /// re-parent) but stays postable, because §8.5 posts the residual of the opening-balance voucher
    /// to it. <see cref="Ledger.Rename"/> only refuses on a ledger that is also barred from posting,
    /// so §4.5's "no rename" cannot be expressed for it without breaking §8.5 — posting wins, and
    /// any rename is edit-logged like every other master change.</description></item>
    /// </list>
    /// </remarks>
    public static IReadOnlyList<LedgerSeed> Ledgers { get; } =
    [
        new("Cash", "Cash-in-Hand", IsSystem: true, AllowsManualPosting: true),
        new(ProfitAndLossLedgerName, "Capital Account", IsSystem: true, AllowsManualPosting: false),
        new("Opening Balance Difference", "Suspense A/c", IsSystem: true, AllowsManualPosting: true),
        new("Stock-in-Hand", "Stock-in-Hand", IsSystem: true, AllowsManualPosting: true),
        new("Sales", "Sales Accounts", IsSystem: false, AllowsManualPosting: true),
        new("Purchases", "Purchase Accounts", IsSystem: false, AllowsManualPosting: true),
        new("Round Off", "Indirect Expenses", IsSystem: false, AllowsManualPosting: true),
        new("Discount Allowed", "Indirect Expenses", IsSystem: false, AllowsManualPosting: true),
        new("Discount Received", "Indirect Incomes", IsSystem: false, AllowsManualPosting: true),
    ];

    /// <summary>
    /// The eight system voucher types of §7.1, one per <see cref="VoucherKind"/>, each with the
    /// §7.2 default numbering series: three-letter prefix + <c>/</c>, width 4, starting at 1,
    /// resetting each financial year, manual override permitted.
    /// </summary>
    /// <remarks>
    /// The type is a data row and the kind is an enum, so the owner may later add "Cash Sales" and
    /// "Credit Sales" as two types under <see cref="VoucherKind.Sales"/> with their own series.
    /// These eight are the closed set that must exist for the product to post at all.
    /// </remarks>
    public static IReadOnlyList<VoucherTypeSeed> VoucherTypes { get; } =
    [
        new("Payment", VoucherKind.Payment, "PMT/"),
        new("Receipt", VoucherKind.Receipt, "RCT/"),
        new("Contra", VoucherKind.Contra, "CTR/"),
        new("Journal", VoucherKind.Journal, "JRN/"),
        new("Sales", VoucherKind.Sales, "SAL/"),
        new("Purchase", VoucherKind.Purchase, "PUR/"),
        new("Credit Note", VoucherKind.CreditNote, "CRN/"),
        new("Debit Note", VoucherKind.DebitNote, "DBN/"),
    ];

    /// <summary>A §4.3 primary group.</summary>
    internal sealed record PrimaryGroupSeed(
        string Name,
        AccountNature Nature,
        bool AffectsGrossProfit);

    /// <summary>A §4.4 sub-group, named against the parent that table gives.</summary>
    internal sealed record SubGroupSeed(string Name, string ParentName);

    /// <summary>A §4.5 ledger and the group it hangs off.</summary>
    internal sealed record LedgerSeed(
        string Name,
        string GroupName,
        bool IsSystem,
        bool AllowsManualPosting);

    /// <summary>A §7.1 voucher type and its §7.2 default series prefix.</summary>
    internal sealed record VoucherTypeSeed(string Name, VoucherKind Kind, string Prefix);
}
