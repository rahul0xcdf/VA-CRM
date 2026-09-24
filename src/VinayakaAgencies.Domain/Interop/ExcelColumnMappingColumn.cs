using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Interop;

/// <summary>One column of the owner's sheet, and the field it feeds.</summary>
/// <remarks>
/// <see cref="TargetField"/> is a string rather than an enum because the set of fields differs per
/// <see cref="ExcelImportTarget"/> — an enum covering all of them would let a mapping for items name
/// a ledger's field and look perfectly valid. The names are validated against the catalogue for the
/// mapping's target in the Application layer, which is where the interop model is defined.
/// </remarks>
public sealed class ExcelColumnMappingColumn : Entity, ICreationStamped, IBelongsToAggregate
{
    private ExcelColumnMappingColumn()
    {
        // EF Core materialisation.
    }

    public ExcelColumnMappingColumn(
        Guid companyId,
        Guid mappingId,
        string sourceHeader,
        string targetField)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceHeader);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetField);

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (mappingId == Guid.Empty)
        {
            throw new ArgumentException("MappingId must not be empty.", nameof(mappingId));
        }

        CompanyId = companyId;
        MappingId = mappingId;
        SourceHeader = sourceHeader.Trim();
        SourceHeaderNormalised = NameNormaliser.Normalise(SourceHeader);
        TargetField = targetField.Trim();
    }

    public Guid CompanyId { get; private set; }

    public Guid MappingId { get; private set; }

    /// <summary>The header text as it appears in the owner's sheet.</summary>
    public string SourceHeader { get; private set; } = string.Empty;

    /// <summary>
    /// Comparison form of the header.
    /// </summary>
    /// <remarks>
    /// Matching normalised means "Item Name", "ITEM NAME" and "Item  Name" are the same column, which
    /// is what the owner means and what a sheet that has been through two other programs produces.
    /// </remarks>
    public string SourceHeaderNormalised { get; private set; } = string.Empty;

    /// <summary>The canonical-model field this column supplies.</summary>
    public string TargetField { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string AggregateType => nameof(ExcelColumnMapping);

    public string AggregateId => MappingId.ToString();
}
