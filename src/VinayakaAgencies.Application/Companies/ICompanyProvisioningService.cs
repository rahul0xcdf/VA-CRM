namespace VinayakaAgencies.Application.Companies;

/// <summary>
/// Gives a newly created company the books it needs before anything can be posted: the
/// chart of accounts (spec §4.3, §4.4, §4.5) and the voucher types with their numbering series
/// (§7.1, §7.2).
/// </summary>
/// <remarks>
/// This is the runtime replacement for migration seeding. Spec §5 rules out <c>HasData</c> outright:
/// it writes rows through the migration, bypassing <c>SaveChangesAsync</c> and therefore the audit
/// trail that §11.1 requires for every insert of every mapped entity. Provisioning a company
/// consequently produces some fifty edit-log rows, which is the correct outcome and not a cost.
/// </remarks>
public interface ICompanyProvisioningService
{
    /// <summary>
    /// Whether this company already has a chart of accounts or voucher setup.
    /// </summary>
    Task<bool> IsProvisionedAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the seeded groups, ledgers, voucher types and numbering series for one company, all
    /// inside a single unit-of-work transaction: either the whole chart of accounts exists
    /// afterwards or none of it does.
    /// </summary>
    /// <remarks>
    /// Idempotent by no-op: calling it again on a provisioned company writes nothing and returns a
    /// result with <see cref="CompanyProvisioningResult.AlreadyProvisioned"/> set.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="companyId"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">No such company exists.</exception>
    Task<CompanyProvisioningResult> ProvisionAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the inventory setup a company needs before stock can move: the system
    /// "Stock Journal" voucher type with its numbering series, the seeded "Main Location" godown,
    /// the base unit of measure and the inventory settings row (phase-2-inventory.md §1).
    /// </summary>
    /// <remarks>
    /// Idempotent and row-granular, unlike <see cref="ProvisionAsync"/>'s all-or-nothing contract:
    /// it exists so databases provisioned before Phase 2 — which already have a chart of accounts
    /// and are therefore refused by <c>IsProvisionedAsync</c>'s logic — still gain the stock seed,
    /// each missing piece independently. Safe to call at every startup.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="companyId"/> is empty.</exception>
    Task EnsureInventorySeedAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the sales/purchase pipeline setup a company needs: one system voucher type with its
    /// numbering series per document kind, the cost-of-goods-sold ledger, and the default
    /// tax-invoice print templates (phase-3-sales-purchase-pipeline.md §4a).
    /// </summary>
    /// <remarks>
    /// Idempotent and row-granular exactly like <see cref="EnsureInventorySeedAsync"/>: databases
    /// provisioned under earlier phases gain the missing rows at the next startup, and the call
    /// writes nothing once everything is present. Safe to call at every startup.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="companyId"/> is empty.</exception>
    Task EnsurePipelineSeedAsync(Guid companyId, CancellationToken cancellationToken = default);
}
