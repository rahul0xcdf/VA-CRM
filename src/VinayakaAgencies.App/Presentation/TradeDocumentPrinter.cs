using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using VinayakaAgencies.Application.Printing;
using VinayakaAgencies.Application.Trade;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.App.Presentation;

/// <summary>
/// Renders a trade document into a <see cref="FlowDocument"/> from its summary and a template
/// layout (phase-3 §2; ADR-0004).
/// </summary>
/// <remarks>
/// Pure presentation: everything it needs arrives in the arguments, nothing here touches the
/// database, and the layout's column names come from the fixed palette the designer screen offers.
/// One Rule-46 copy label renders per section; callers pass them one at a time.
/// </remarks>
internal static class TradeDocumentPrinter
{
    internal static FlowDocument Build(
        TradeDocumentSummary document,
        PrintTemplateLayout layout,
        string? companyName,
        string? copyLabel)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layout);

        var flow = new FlowDocument
        {
            PagePadding = new Thickness(48),
            ColumnWidth = double.MaxValue,
        };

        var heading = new Paragraph
        {
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4),
        };

        var titleRun = new Run(layout.Title);
        heading.Inlines.Add(titleRun);

        if (!string.IsNullOrWhiteSpace(copyLabel))
        {
            var label = new Run($"  —  {copyLabel}")
            {
                FontSize = 11,
                FontWeight = FontWeights.Normal,
            };
            heading.Inlines.Add(label);
        }

        flow.Blocks.Add(heading);

        if (layout.HeaderLines.Count > 0 || !string.IsNullOrWhiteSpace(companyName))
        {
            var header = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };

            if (!string.IsNullOrWhiteSpace(companyName))
            {
                header.Inlines.Add(new Run(companyName) { FontWeight = FontWeights.SemiBold });
                header.Inlines.Add(new LineBreak());
            }

            foreach (var line in layout.HeaderLines)
            {
                header.Inlines.Add(new Run(line));
                header.Inlines.Add(new LineBreak());
            }

            flow.Blocks.Add(header);
        }

        flow.Blocks.Add(BuildPartyBlock(document));
        flow.Blocks.Add(BuildLinesTable(document, layout));

        var total = new Paragraph
        {
            Margin = new Thickness(0, 6, 0, 0),
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Right,
        };
        total.Inlines.Add(new Run(
            $"Total: {document.TotalNetValue.ToString("0.##", CultureInfo.InvariantCulture)}"));
        flow.Blocks.Add(total);

        if (!string.IsNullOrWhiteSpace(layout.FooterNote))
        {
            flow.Blocks.Add(new Paragraph(new Run(layout.FooterNote))
            {
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 10,
            });
        }

        if (!string.IsNullOrWhiteSpace(layout.TermsAndConditions))
        {
            flow.Blocks.Add(new Paragraph(new Run(layout.TermsAndConditions))
            {
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 9,
            });
        }

        return flow;
    }

    private static Paragraph BuildPartyBlock(TradeDocumentSummary document)
    {
        var block = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };

        block.Inlines.Add(new Run($"{(document.Kind is TradeDocumentKind.Quotation or TradeDocumentKind.SalesOrder or TradeDocumentKind.DeliveryChallan or TradeDocumentKind.TaxInvoice ? "Bill to" : "Supplier")}: ")
        {
            FontWeight = FontWeights.SemiBold,
        });
        block.Inlines.Add(new Run(document.PartyLedgerName));
        block.Inlines.Add(new LineBreak());
        block.Inlines.Add(new Run($"{document.Kind} no.: {document.DisplayNumber}    Date: {document.DocumentDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}"));

        if (!string.IsNullOrWhiteSpace(document.PartyReferenceNumber))
        {
            block.Inlines.Add(new LineBreak());
            block.Inlines.Add(new Run($"Your reference: {document.PartyReferenceNumber}"));
        }

        return block;
    }

    private static Table BuildLinesTable(TradeDocumentSummary document, PrintTemplateLayout layout)
    {
        var visible = layout.VisibleColumns.Count > 0 ? layout.VisibleColumns : ["Sr", "Item", "Qty", "Rate", "Amount"];

        var table = new Table { CellSpacing = 0 };

        foreach (var _ in visible)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        }

        var headerGroup = new TableRowGroup();
        var headerRow = new TableRow { Background = System.Windows.Media.Brushes.WhiteSmoke };

        foreach (var column in visible)
        {
            headerRow.Cells.Add(new TableCell(new Paragraph(new Run(column)
            {
                FontWeight = FontWeights.SemiBold,
            })));
        }

        headerGroup.Rows.Add(headerRow);
        table.RowGroups.Add(headerGroup);

        var bodyGroup = new TableRowGroup();

        foreach (var line in document.Lines)
        {
            var row = new TableRow();

            foreach (var column in visible)
            {
                var text = column switch
                {
                    "Sr" => line.LineNumber.ToString(CultureInfo.InvariantCulture),
                    "Item" => line.ItemName,
                    "Qty" => line.Quantity.ToString("0.####", CultureInfo.InvariantCulture),
                    "Rate" => line.Rate.ToString("0.##", CultureInfo.InvariantCulture),
                    "Discount" => $"{line.DiscountPercent:0.##}%",
                    "Amount" => line.NetValue.ToString("0.##", CultureInfo.InvariantCulture),
                    _ => string.Empty,
                };

                row.Cells.Add(new TableCell(new Paragraph(new Run(text))));
            }

            bodyGroup.Rows.Add(row);
        }

        table.RowGroups.Add(bodyGroup);

        return table;
    }
}
