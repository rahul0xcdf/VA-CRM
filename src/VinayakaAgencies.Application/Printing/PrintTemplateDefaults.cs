using VinayakaAgencies.Domain.Printing;

namespace VinayakaAgencies.Application.Printing;

/// <summary>
/// The layouts a fresh company starts with: two tax-invoice templates differing in density and
/// priority so checkpoint C3.6's selection rule has something real to select
/// (phase-3-sales-purchase-pipeline.md §4a).
/// </summary>
internal static class PrintTemplateDefaults
{
    /// <summary>The column palette the renderer knows. Templates pick from it; they never invent.</summary>
    internal static readonly string[] AllColumns =
        ["Sr", "Item", "Qty", "Rate", "Discount", "Amount"];

    internal static readonly string[] DefaultCopyLabels =
        ["Original for Recipient", "Duplicate for Transporter", "Triplicate for Supplier"];

    internal sealed record Seed(
        PrintDocumentKind DocumentKind,
        string Name,
        int Priority,
        string Description,
        PrintTemplateLayout Layout);

    internal static IReadOnlyList<Seed> Seeds { get; } =
    [
        new(
            PrintDocumentKind.TaxInvoice,
            "Tax Invoice — Detailed",
            10,
            "Default: full header, item grid and Rule-46 copy labels.",
            new PrintTemplateLayout(
                Title: "TAX INVOICE",
                ShowLogo: true,
                LogoPath: null,
                HeaderLines:
                [
                    "Vinayaka Agencies",
                    "Motors · Pumps · Spares"
                ],
                FooterNote: "Goods once sold will not be taken back. Interest at 18% p.a. on overdue bills.",
                TermsAndConditions:
                    "1. Payment due on receipt unless agreed otherwise.\n" +
                    "2. Warranty as per manufacturer's terms only.",
                VisibleColumns: AllColumns,
                CopyLabels: DefaultCopyLabels)),

        new(
            PrintDocumentKind.TaxInvoice,
            "Tax Invoice — Compact",
            20,
            "Denser grid for invoices with many lines.",
            new PrintTemplateLayout(
                Title: "TAX INVOICE",
                ShowLogo: false,
                LogoPath: null,
                HeaderLines: ["Vinayaka Agencies"],
                FooterNote: null,
                TermsAndConditions: "Subject to local jurisdiction.",
                VisibleColumns: ["Sr", "Item", "Qty", "Rate", "Amount"],
                CopyLabels: ["Original for Recipient"])),
    ];
}
