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

    /// <summary>
    /// Ensures the customer-side setup a company needs: the four placeholder customer categories
    /// (Retailer / Dealer / Wholesaler / Institutional) and the sales settings row with its
    /// defaults (override allowed, credit breaches warn) — phase-4-customer-pricing.md §2, §4a.
    /// </summary>
    /// <remarks>
    /// Idempotent and row-granular exactly like its Phase 2/3 siblings: safe to call at every
    /// startup, fills only what is genuinely missing.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="companyId"/> is empty.</exception>
    Task EnsureCustomerSeedAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the GST setup a company needs before its first invoice posts: the output/input
    /// tax ledgers the computation posts through, and the settings row naming the round-off
    /// ledger — phase-5-gst-returns.md §2.
    /// </summary>
    /// <remarks>
    /// Idempotent and row-granular exactly like its Phase 2/3/4 siblings. Deliberately seeds no
    /// HSN codes and no rates: nothing tax-related may be hardcoded, so codes and rates are the
    /// owner's master data entered through <see cref="VinayakaAgencies.Application.Gst.IHsnSacService"/>
    /// and <see cref="VinayakaAgencies.Application.Gst.IGstRateService"/>.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="companyId"/> is empty.</exception>
    Task EnsureGstSeedAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the TDS/TCS setup a company needs before its first deducting voucher: the TDS and
    /// TCS payable ledgers, and the settings row naming them —
    /// phase-7-tds-payroll-banking.md §2.
    /// </summary>
    /// <remarks>
    /// Idempotent and row-granular like its siblings, and it seeds <b>no section, rate or
    /// threshold</b> — not even 194C, the one section §4a D1 names as in use today. Those are the
    /// owner's master data entered through
    /// <see cref="VinayakaAgencies.Application.Tds.ITaxSectionService"/>, which is what makes
    /// checkpoints C7.2 and C7.5 hold. Both applicability flags start off (§4a D2).
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="companyId"/> is empty.</exception>
    Task EnsureTdsSeedAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the payroll setup: the salary and statutory ledgers, and the settings row carrying
    /// the three applicability flags — phase-7-tds-payroll-banking.md §4a D3.
    /// </summary>
    /// <remarks>
    /// Idempotent and row-granular like its siblings, and it seeds <b>no contribution rate, wage
    /// ceiling, professional tax slab or pay head</b>. §2 extends non-negotiable #6 to the first
    /// three by name; pay heads are the owner's own salary structure, entered through
    /// <see cref="VinayakaAgencies.Application.Payroll.IPayrollMasterService"/>. All three
    /// statutory flags start off, so the module is inert until the owner declares otherwise
    /// (checkpoint C7.7).
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="companyId"/> is empty.</exception>
    Task EnsurePayrollSeedAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the two roles `phase-9-multiuser.md` §4a D4 settles on — Owner and Operator —
    /// exist for the company.
    /// </summary>
    /// <remarks>
    /// Idempotent and row-granular like its Phase 2–7 siblings, so an install provisioned under an
    /// earlier phase gains the roles at the next startup.
    /// <para>
    /// Seeds <b>roles only, never a user</b>. A seeded account would ship every install with a
    /// known user name and a password somebody has to remember to change, which is how products
    /// end up with an <c>admin</c>/<c>admin</c> nobody touched. The first account is created at
    /// first launch through
    /// <see cref="VinayakaAgencies.Application.Security.ISignInService.CreateFirstAccountAsync"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="companyId"/> is empty.</exception>
    Task EnsureSecuritySeedAsync(Guid companyId, CancellationToken cancellationToken = default);
}
