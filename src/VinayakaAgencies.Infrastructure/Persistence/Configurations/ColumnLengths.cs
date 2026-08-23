namespace VinayakaAgencies.Infrastructure.Persistence.Configurations;

/// <summary>
/// String column widths that are stated explicitly rather than left on the convention default.
/// </summary>
/// <remarks>
/// <c>AppDbContext.ConfigureConventions</c> caps every non-key string at 512 characters so nothing
/// maps to <c>nvarchar(max)</c> by accident at Phase 9. SQLite ignores the cap entirely, so a wrong
/// width is invisible through Phase 8 and only surfaces on the provider switch — which is exactly
/// the class of Phase 9 surprise the spec's column-length ruling (§5) exists to prevent.
/// <para>
/// <b>The 900-byte rule.</b> SQL Server refuses to create a non-clustered index whose key exceeds
/// 900 bytes, and <c>nvarchar</c> costs two bytes per character. Any string column that appears in
/// an index therefore has to be sized here: at the 512 default, a single indexed name column is
/// 1024 bytes on its own and the index cannot be created at all. That is the reasoning §4.2 used to
/// size <see cref="MaterialisedPath"/>, applied to every other indexed string in the phase.
/// </para>
/// </remarks>
internal static class ColumnLengths
{
    /// <summary>
    /// Master names and their normalised comparison forms, matching <c>Company.Name</c>.
    /// </summary>
    /// <remarks>
    /// Indexed: <c>AccountGroup</c>, <c>Ledger</c> and <c>VoucherType</c> each carry a company-scoped
    /// unique index over the normalised form (spec §5).
    /// </remarks>
    internal const int Name = 200;

    /// <summary>
    /// The <c>/</c>-separated ancestor chain, sized by spec §4.2 for ten levels of GUIDs.
    /// </summary>
    /// <remarks>
    /// 400, not the 512 default. <c>(CompanyId, MaterialisedPath)</c> at 512 is 1040 bytes and SQL
    /// Server refuses to create the index (spec §5, column-lengths note).
    /// </remarks>
    internal const int MaterialisedPath = 400;

    /// <summary>
    /// Voucher numbers and their normalised comparison form.
    /// </summary>
    /// <remarks>
    /// Sized to hold the widest number the series format can produce — see
    /// <see cref="NumberAffix"/> — with room for a manual override that does not follow the series
    /// pattern. It is the fifth key column of the filtered unique index that enforces numbering,
    /// so it cannot be left at the 512 default.
    /// </remarks>
    internal const int VoucherNumber = 100;

    /// <summary>The prefix and suffix of a numbering series, e.g. <c>PMT/</c>.</summary>
    /// <remarks>
    /// Bounded so that <c>prefix + 12 padded digits + suffix</c> cannot exceed
    /// <see cref="VoucherNumber"/> and truncate the number that was printed on a customer's invoice.
    /// </remarks>
    internal const int NumberAffix = 32;

    /// <summary>
    /// Free-text narration on a voucher revision or a journal line.
    /// </summary>
    /// <remarks>
    /// Widened from the 512 default per spec §5: 512 is tight for an operator used to Tally, and the
    /// column costs nothing on SQLite.
    /// </remarks>
    internal const int Narration = 1000;

    /// <summary>Audit stamp authors, matching <c>CompanyConfiguration</c> and the edit log.</summary>
    internal const int UserName = 256;

    /// <summary>
    /// Enum values, which are stored as their names.
    /// </summary>
    /// <remarks>
    /// Spec §5: enums are persisted with <c>HasConversion&lt;string&gt;()</c> so that a later
    /// reordering of the enum cannot silently change what an existing row means — the same reasoning
    /// §11.3 applies to the audit log, and it applies harder here because Phase 5 and Phase 8
    /// reporting SQL filters on these values.
    /// </remarks>
    internal const int EnumName = 32;

    /// <summary>Operator-facing item codes and barcodes.</summary>
    internal const int ItemCode = 64;

    /// <summary>Batch names on stock movements.</summary>
    internal const int BatchNumber = 64;

    /// <summary>
    /// Serial-number values and their normalised comparison forms.
    /// </summary>
    /// <remarks>
    /// Indexed: the unique <c>(CompanyId, ItemId, ValueNormalised)</c> index that makes "in stock
    /// exactly once" a database fact rather than a convention. At 100 characters × 3 columns the
    /// composite stays inside SQL Server's 900-byte index limit.
    /// </remarks>
    internal const int SerialValue = 100;

    /// <summary>Unit-of-measure codes ("NOS", "BOX").</summary>
    internal const int UnitCode = 16;

    /// <summary>
    /// The ';'-joined serial numbers staged on a trade document line.
    /// </summary>
    /// <remarks>
    /// Not indexed — availability is checked against the serial table at posting time — so the
    /// width only bounds storage, sized for roughly a hundred typical motor/pump serials.
    /// </remarks>
    internal const int SerialList = 2000;

    /// <summary>
    /// One version of a print template's layout as JSON.
    /// </summary>
    /// <remarks>
    /// Not indexed either; the bound exists so Phase 9's provider switch maps to a bounded column
    /// rather than an unbounded one by accident.
    /// </remarks>
    internal const int TemplateLayoutJson = 8000;
}
