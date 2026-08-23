using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.Application.Customers;

/// <summary>
/// The customer-side seed rows: the four placeholder categories and the sales settings row
/// (phase-4-customer-pricing.md §2 as closed on approval, §4a).
/// </summary>
/// <remarks>
/// Written at runtime through <see cref="ICompanyProvisioningService.EnsureCustomerSeedAsync"/> —
/// the same no-<c>HasData</c> rule as every other seed table, because migration seeding bypasses
/// <c>SaveChangesAsync</c> and therefore the audit trail (spec §5). The four categories are master
/// data rows, freely renamable and extendable; nothing downstream may treat them as an enum.
/// </remarks>
internal static class CustomerSeedData
{
    /// <summary>The group a customer's account ledger hangs under (seeded by Phase 1).</summary>
    public const string SundryDebtorsGroupName = "Sundry Debtors";

    internal sealed record CategorySeed(string Name, string Description);

    public static IReadOnlyList<CategorySeed> Categories { get; } =
    [
        new("Retailer", "Counter and walk-in customers at retail prices."),
        new("Dealer", "Authorised dealers buying for resale at dealer terms."),
        new("Wholesaler", "Bulk buyers on wholesale rates."),
        new("Institutional", "Hospitals, apartments and institutions on quoted terms."),
    ];
}
