using VinayakaAgencies.Domain.Printing;

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
/// <param name="Fields">
/// Absolutely-positioned fields, in millimetres from the top-left of the page. Empty or null for a
/// flowing document such as an invoice, which lays itself out; used by a
/// <see cref="PrintDocumentKind.Cheque"/> layout, where the whole job is putting five values on
/// pre-printed stationery at the millimetre the bank put the boxes
/// (phase-7-tds-payroll-banking.md §4a D4, checkpoint C7.8).
/// </param>
public sealed record PrintTemplateLayout(
    string Title,
    bool ShowLogo,
    string? LogoPath,
    IReadOnlyList<string> HeaderLines,
    string? FooterNote,
    string? TermsAndConditions,
    IReadOnlyList<string> VisibleColumns,
    IReadOnlyList<string> CopyLabels,
    IReadOnlyList<PrintFieldPlacement>? Fields = null)
{
    /// <summary>The placed fields, never null, so a renderer needs no null check.</summary>
    /// <remarks>
    /// <see cref="Fields"/> is nullable because template rows written before Phase 7 have no such
    /// property in their stored JSON, and deserialising those gives null. Normalising here rather
    /// than migrating the JSON keeps every existing revision readable exactly as it was saved —
    /// which is the point of versioning them in the first place.
    /// </remarks>
    public IReadOnlyList<PrintFieldPlacement> PlacedFields => Fields ?? [];
}

/// <summary>
/// One value placed at a measured spot on the page (phase-7-tds-payroll-banking.md §4a D4).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what makes checkpoint C7.8 a settings change.</b> A cheque layout is field positions
/// on a leaf; CTS-2010 standardised Indian cheque dimensions, so one adjustable layout covers most
/// banks with millimetre tweaks rather than a per-bank rewrite. The owner prints onto a real
/// cheque, measures what is off, edits these numbers and saves — which appends a template version
/// like any other. Nothing is rebuilt.
/// </para>
/// <para>
/// Millimetres, not pixels or points: the owner measures a cheque with a ruler, and asking them to
/// convert would be asking them to be wrong.
/// </para>
/// </remarks>
/// <param name="Field">
/// Which value prints here, named from the renderer's fixed palette — see
/// <c>ChequeFields</c>. An unrecognised name prints nothing rather than throwing, because a layout
/// saved against a newer palette must not make an older build unable to print at all.
/// </param>
/// <param name="LeftMm">Distance from the left edge of the page.</param>
/// <param name="TopMm">Distance from the top edge of the page.</param>
/// <param name="WidthMm">Wrapping width, or null to run as far as the text needs.</param>
/// <param name="FontSize">Point size.</param>
/// <param name="IsBold">Whether the value prints bold.</param>
/// <param name="IsVisible">
/// False to suppress the field without losing its measured position — which is how an owner turns
/// off, say, the A/C-payee crossing on a bank whose leaves are pre-crossed, and turns it back on
/// later without re-measuring.
/// </param>
public sealed record PrintFieldPlacement(
    string Field,
    double LeftMm,
    double TopMm,
    double? WidthMm = null,
    double FontSize = 11d,
    bool IsBold = false,
    bool IsVisible = true);

/// <summary>Read model for a template row, without its layout text.</summary>
public sealed record PrintTemplateSummary(
    Guid Id,
    PrintDocumentKind DocumentKind,
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
    PrintDocumentKind DocumentKind,
    string Name,
    string? Description,
    int Priority,
    int CurrentVersion,
    bool IsActive,
    PrintTemplateLayout Layout,
    IReadOnlyList<PrintTemplateRevisionSummary> Revisions);

public sealed record CreatePrintTemplateRequest(
    PrintDocumentKind DocumentKind,
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
        PrintDocumentKind? documentKind = null,
        CancellationToken cancellationToken = default);

    /// <summary>The template that prints by default for a kind: lowest priority among active ones.</summary>
    Task<PrintTemplateSummary?> ResolveDefaultAsync(
        PrintDocumentKind documentKind,
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
