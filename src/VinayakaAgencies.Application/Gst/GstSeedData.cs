namespace VinayakaAgencies.Application.Gst;

/// <summary>
/// The GST seed rows provisioning writes for one company (phase-5-gst-returns.md §2).
/// </summary>
/// <remarks>
/// Deliberately no HSN codes and no tax rates here — nothing tax-related may be hardcoded
/// (CLAUDE.md non-negotiable #6), and which codes and rates apply is the owner's notification-driven
/// master data, entered through <see cref="IHsnSacService"/> and <see cref="IGstRateService"/>.
/// What a company does need before its first invoice posts is the tax ledgers the computation posts
/// through, and the settings row naming the round-off ledger; both are idempotent data rows.
/// </remarks>
internal static class GstSeedData
{
    /// <summary>The output-tax ledgers a sale credits, under "Duties & Taxes".</summary>
    public static IReadOnlyList<string> OutputLedgerNames { get; } =
    [
        "Output CGST",
        "Output SGST",
        "Output IGST",
        "Output Cess",
    ];

    /// <summary>The input-tax ledgers a purchase debits, as current assets.</summary>
    public static IReadOnlyList<string> InputLedgerNames { get; } =
    [
        "Input CGST",
        "Input SGST",
        "Input IGST",
        "Input Cess",
    ];

    /// <summary>Where output ledgers hang; seeded by Phase 1, so normally present already.</summary>
    public const string DutiesAndTaxesGroupName = "Duties & Taxes";

    /// <summary>Where input-credit ledgers hang.</summary>
    public const string CurrentAssetsGroupName = "Current Assets";
}
