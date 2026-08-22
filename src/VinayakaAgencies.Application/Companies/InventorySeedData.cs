namespace VinayakaAgencies.Application.Companies;

/// <summary>
/// The inventory rows a company must have before stock can move, transcribed from
/// <c>specs/phase-2-inventory.md</c> §1 as approved.
/// </summary>
/// <remarks>
/// Data, not schema — written at runtime by
/// <see cref="CompanyProvisioningService.EnsureInventorySeedAsync"/> through the unit of work so the
/// audit trail captures every seeded row (spec §5 rules out <c>HasData</c>), and row-granularly
/// idempotent so databases provisioned under Phase 1 gain them at their next startup.
/// </remarks>
public static class InventorySeedData
{
    /// <summary>
    /// The system voucher type every manual inventory document posts and numbers under. Kind is
    /// <c>Journal</c>, so all Phase 1 journal-engine invariants apply to it unchanged; its own
    /// series keeps stock movements out of the JRN/ numbers an operator reads as manual journals.
    /// </summary>
    public const string StockJournalVoucherTypeName = "Stock Journal";

    /// <summary>Prefix of the Stock Journal series: SJ/0001, resetting each financial year.</summary>
    public const string StockJournalPrefix = "SJ/";

    /// <summary>The default base unit, present on day one so item creation never dead-ends.</summary>
    public const string BaseUnitCode = "NOS";
}
