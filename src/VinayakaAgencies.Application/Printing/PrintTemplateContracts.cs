using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.Application.Printing;

/// <summary>
/// The layout a document prints with. This record is the whole contract between the designer
/// screen, the stored JSON and the renderer — an operator edits its fields, never code
/// (checkpoint C3.2).
/// </summary>
/// <param name="Title">The heading printed above the party block ("Tax Invoice", "Quotation").</param>
/// <param name="ShowLogo">Whether the company logo prints.</param>
/// <param name="LogoPath">Image file the logo loads from when <see cref="ShowLogo"/> is set.</param>
/// <param name="HeaderLines">Static lines under the title — address, GSTIN, bank details.</param>
/// <param name="FooterNote">Note printed under the totals ("Subject to Jamakhandi jurisdiction").</param>
/// <param name="TermsAndConditions">Terms printed at the foot of every page.</param>
/// <param name="VisibleColumns">
/// Item-grid columns to print, named from the renderer's fixed palette ("Sr", "Item", "Qty",
/// "Rate", "Discount", "Amount").
/// </param>
/// <param name="CopyLabels">
/// Rule-46 copy labels ("Original for Recipient", "Duplicate for Transporter", "Triplicate for
/// Supplier"); one print run per label, empty meaning a single unlabelled copy.
/// </param>
public sealed record PrintTemplateLayout(
    string Title,
    bool ShowLogo,
    string? LogoPath,
    IReadOnlyList<string> HeaderLines,
    string? FooterNote,
    string? TermsAndConditions,
    IReadOnlyList<string> VisibleColumns,
    IReadOnlyList<string> CopyLabels);

/// <summary>Read model for a template row, without its layout text.</summary>
public sealed record PrintTemplateSummary(
    Guid Id,
    TradeDocumentKind DocumentKind,
    string Name,
    string? Description,
    int Priority,
    int CurrentVersion,
    bool IsActive);

/// <summary>One row of a template's version history.</summary>
public sealed record PrintTemplateRevisionSummary(
    int Version,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy);

/// <summary>A template with its current layout and history, as the designer screen shows it.</summary>
public sealed record PrintTemplateDetail(
    Guid Id,
    TradeDocumentKind DocumentKind,
    string Name,
    string? Description,
    int Priority,
    int CurrentVersion,
    bool IsActive,
    PrintTemplateLayout Layout,
    IReadOnlyList<PrintTemplateRevisionSummary> Revisions);

public sealed record CreatePrintTemplateRequest(
    TradeDocumentKind DocumentKind,
    string Name,
    int Priority,
    string? Description,
    PrintTemplateLayout Layout);

/// <summary>
/// Template maintenance: list, inspect, create and re-version print templates
/// (phase-3-sales-purchase-pipeline.md §2). Rendering itself is presentation-layer work; this
/// service only keeps the data honest — versions append, pointers advance, nothing edits history.
/// </summary>
public interface IPrintTemplateService
{
    Task<IReadOnlyList<PrintTemplateSummary>> ListAsync(
        TradeDocumentKind? documentKind = null,
        CancellationToken cancellationToken = default);

    /// <summary>The template that prints by default for a kind: lowest priority among active ones.</summary>
    Task<PrintTemplateSummary?> ResolveDefaultAsync(
        TradeDocumentKind documentKind,
        CancellationToken cancellationToken = default);

    Task<PrintTemplateDetail?> GetAsync(Guid templateId, CancellationToken cancellationToken = default);

    Task<PrintTemplateDetail> CreateAsync(
        CreatePrintTemplateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Saves an edited layout as the next version; previous revisions stay readable.</summary>
    Task<PrintTemplateDetail> SaveLayoutAsync(
        Guid templateId,
        PrintTemplateLayout layout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates or retires a template. Retired ones stop being default-selection candidates
    /// (checkpoint C3.6) but keep their revision history for the audit.
    /// </summary>
    Task<PrintTemplateSummary> SetActiveAsync(
        Guid templateId,
        bool isActive,
        CancellationToken cancellationToken = default);
}
