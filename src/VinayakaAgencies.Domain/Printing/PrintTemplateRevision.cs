using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Printing;

/// <summary>
/// One version of a template's layout: an append-only row, never edited
/// (phase-3-sales-purchase-pipeline.md §2 — templates are versioned).
/// </summary>
/// <remarks>
/// Saving a template change appends a new revision and moves the parent's pointer; the previous
/// rows stay, so "what did the invoice I filed in March actually look like" is a query, not an
/// archaeology project. The layout text itself is opaque here — its shape is the application
/// layer's contract with the renderer.
/// </remarks>
public sealed class PrintTemplateRevision : Entity, ICreationStamped, IBelongsToAggregate
{
    private PrintTemplateRevision()
    {
        // EF Core materialisation.
    }

    public PrintTemplateRevision(
        Guid companyId,
        Guid templateId,
        int version,
        string layoutJson)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (templateId == Guid.Empty)
        {
            throw new ArgumentException("TemplateId must not be empty.", nameof(templateId));
        }

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version), version, "Versions start at 1.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(layoutJson);

        CompanyId = companyId;
        TemplateId = templateId;
        Version = version;
        LayoutJson = layoutJson.Trim();
    }

    public Guid CompanyId { get; private set; }

    public Guid TemplateId { get; private set; }

    public int Version { get; private set; }

    /// <summary>The layout definition as JSON. Shape owned by the printing contracts.</summary>
    public string LayoutJson { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public string AggregateType => nameof(PrintTemplate);

    public string AggregateId => TemplateId.ToString();
}
