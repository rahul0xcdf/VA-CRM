namespace VinayakaAgencies.Application.Companies;

/// <summary>
/// What one call to <see cref="ICompanyProvisioningService.ProvisionAsync"/> wrote.
/// </summary>
/// <param name="CompanyId">The company whose books were provisioned.</param>
/// <param name="AlreadyProvisioned">
/// True when the company already had a chart of accounts and this call wrote nothing. Provisioning
/// is a no-op in that case rather than a second pass: the chart of accounts is unique per company by
/// name, so a second pass would either duplicate the tree or fail halfway on a unique index.
/// </param>
/// <param name="AccountGroupsCreated">15 primary groups + 13 sub-groups on a fresh company.</param>
/// <param name="LedgersCreated">Ledgers L1–L9 of spec §4.5.</param>
/// <param name="VoucherTypesCreated">The eight system types of spec §7.1.</param>
/// <param name="NumberSeriesCreated">One default series per voucher type (spec §7.2).</param>
public sealed record CompanyProvisioningResult(
    Guid CompanyId,
    bool AlreadyProvisioned,
    int AccountGroupsCreated,
    int LedgersCreated,
    int VoucherTypesCreated,
    int NumberSeriesCreated)
{
    /// <summary>Total rows written, which is also the number of edit-log rows produced (§11.1).</summary>
    public int TotalRowsCreated =>
        AccountGroupsCreated + LedgersCreated + VoucherTypesCreated + NumberSeriesCreated;

    /// <summary>The result of a call that found the company already provisioned.</summary>
    public static CompanyProvisioningResult NoOp(Guid companyId) =>
        new(companyId, AlreadyProvisioned: true, 0, 0, 0, 0);
}
