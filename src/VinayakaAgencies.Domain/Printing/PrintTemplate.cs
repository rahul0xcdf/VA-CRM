using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Printing;

/// <summary>
/// A printable document template: the layout a quotation or tax invoice is printed with
/// (phase-3-sales-purchase-pipeline.md §2).
/// </summary>
/// <remarks>
/// Templates live as data — the layout itself in <see cref="PrintTemplateRevision"/> rows, this row
/// carrying identity, the current version pointer and the selection rule. More than one template
/// may exist per document kind; among the active ones, the lowest <see cref="Priority"/> wins as the
/// default, and an operator may always pick another at print time (checkpoint C3.6).
/// </remarks>
public sealed class PrintTemplate : Entity, IAuditStamped
{
    private PrintTemplate()
    {
        // EF Core materialisation.
    }

    public PrintTemplate(
        Guid companyId,
        PrintDocumentKind documentKind,
        string name,
        int priority,
        string? description)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ArgumentOutOfRangeException.ThrowIfNegative(priority);

        CompanyId = companyId;
        DocumentKind = documentKind;
        SetName(name);
        Priority = priority;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        CurrentVersion = 0;
        IsActive = true;
    }

    public Guid CompanyId { get; private set; }

    public PrintDocumentKind DocumentKind { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Comparison form of <see cref="Name"/> for the per-kind unique index.</summary>
    public string NameNormalised { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>
    /// The default-selection rule: of a kind's active templates, the lowest priority wins. Two
    /// seeded tax-invoice templates differ exactly here (checkpoint C3.6); owners reorder freely.
    /// </summary>
    public int Priority { get; private set; }

    /// <summary>The version the <see cref="CurrentRevisionId"/> revision carries.</summary>
    public int CurrentVersion { get; private set; }

    /// <summary>The layout currently printed — a <see cref="PrintTemplateRevision"/> row.</summary>
    public Guid CurrentRevisionId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    /// <summary>Renames the template; the normalised form follows for uniqueness.</summary>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        SetName(name);
    }

    /// <summary>Sets where this template sits in the default-selection rule.</summary>
    public void SetPriority(int priority)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(priority);
        Priority = priority;
    }

    /// <summary>
    /// Points the template at its freshly appended revision. Called once per save by the template
    /// service, which creates the revision row and this update inside one transaction.
    /// </summary>
    public void AttachRevision(Guid revisionId, int version)
    {
        if (revisionId == Guid.Empty)
        {
            throw new ArgumentException("RevisionId must not be empty.", nameof(revisionId));
        }

        if (version != CurrentVersion + 1)
        {
            throw new InvalidOperationException(
                $"Template versions advance one at a time; expected {CurrentVersion + 1}, got {version}.");
        }

        CurrentRevisionId = revisionId;
        CurrentVersion = version;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    private void SetName(string name)
    {
        Name = name.Trim();
        NameNormalised = NameNormaliser.Normalise(name);
    }
}
