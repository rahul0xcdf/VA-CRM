namespace VinayakaAgencies.Application.Tds;

/// <summary>
/// The TDS/TCS seed rows provisioning writes for one company
/// (phase-7-tds-payroll-banking.md §2).
/// </summary>
/// <remarks>
/// <para>
/// <b>No sections. No rates. No thresholds.</b> Not even 194C, which §4a D1 names as the one section
/// the business deducts under today — because seeding it would put a section number in <c>src/</c>
/// and checkpoint C7.5 greps for exactly that. More to the point, a seeded rate is a rate somebody
/// has to remember to correct when the notification changes it, and the owner entering it once is
/// the act that makes them own it.
/// </para>
/// <para>
/// What a company does need before its first deducting voucher is the two liability ledgers the tax
/// posts to, and the settings row that names them. Both are idempotent data rows, and neither is a
/// tax figure.
/// </para>
/// </remarks>
internal static class TdsSeedData
{
    /// <summary>The liability ledger tax withheld from suppliers is credited to.</summary>
    public const string TdsPayableLedgerName = "TDS Payable";

    /// <summary>The liability ledger tax collected from buyers is credited to.</summary>
    public const string TcsPayableLedgerName = "TCS Payable";

    /// <summary>Where both hang; seeded by Phase 1, so normally present already.</summary>
    public const string DutiesAndTaxesGroupName = "Duties & Taxes";

    /// <summary>Both liability ledgers, in the order provisioning creates them.</summary>
    public static IReadOnlyList<string> PayableLedgerNames { get; } =
    [
        TdsPayableLedgerName,
        TcsPayableLedgerName,
    ];
}
