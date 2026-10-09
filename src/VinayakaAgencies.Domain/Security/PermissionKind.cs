namespace VinayakaAgencies.Domain.Security;

/// <summary>
/// A discrete right the service layer can refuse.
/// </summary>
/// <remarks>
/// <b>Why this is an enum when roles are data.</b> `phase-9-multiuser.md` §4a D4 settles that roles
/// are data rows, so a third role is master data rather than a rebuild. Permissions are the
/// opposite kind of thing: each value here is a branch that some service actually takes, so the set
/// is closed by the code whether it is written down or not. Making it an enum means a role can only
/// ever grant a right the product can enforce — a permission row naming something no service checks
/// would read as protection and provide none.
/// <para>
/// Persisted by name (<c>HasConversion&lt;string&gt;()</c>), like every other enum in the schema, so
/// that reordering these members cannot silently change what a stored grant means.
/// </para>
/// <para>
/// <b>Why the awkward name.</b> CA1711 reserves the <c>Permission</c> suffix for types deriving from
/// the old code-access-security hierarchy, and this build treats warnings as errors — both
/// <c>Permission</c> and <c>AppPermission</c> are refused. So the type carries <c>Kind</c> and
/// everything around it keeps the spec's word, permissions. Not worth an ADR; worth a note, so that
/// nobody renames it back and rediscovers this.
/// </para>
/// </remarks>
public enum PermissionKind
{
    /// <summary>Create and alter users and roles, and reset passwords.</summary>
    ManageUsers,

    /// <summary>Change company, sales, GST, payroll or e-invoicing settings.</summary>
    /// <remarks>One of the three rights D4 refuses the Operator role.</remarks>
    ManageConfiguration,

    /// <summary>Create and alter masters — ledgers, groups, items, customers, employees.</summary>
    ManageMasters,

    /// <summary>Post a voucher, an invoice or a receipt.</summary>
    PostVoucher,

    /// <summary>Alter a posted voucher.</summary>
    AlterVoucher,

    /// <summary>Cancel a posted voucher.</summary>
    /// <remarks>One of the three rights D4 refuses the Operator role.</remarks>
    CancelVoucher,

    /// <summary>
    /// Save a sale line at a price other than the one the pricing engine applied.
    /// </summary>
    /// <remarks>
    /// The right Phase 4 recorded as the <c>Sales.AllowPriceOverride</c> setting "until Phase 9
    /// roles exist" (C4.5). It now has a holder rather than being on or off for everybody.
    /// </remarks>
    OverrideSalesPrice,

    /// <summary>Read reports and statements.</summary>
    ViewReports,

    /// <summary>Generate GST returns, TDS statements, IRNs and e-way bills.</summary>
    RunStatutoryReturns,

    /// <summary>Run payroll and view salary vouchers.</summary>
    RunPayroll,

    /// <summary>Import from or export to Tally and Excel.</summary>
    ImportExportData,
}
