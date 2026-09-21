using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using VinayakaAgencies.Application.Banking;
using VinayakaAgencies.Application.Printing;

namespace VinayakaAgencies.App.Presentation;

/// <summary>
/// Places a cheque's five values on a blank page at the millimetres the layout names
/// (phase-7-tds-payroll-banking.md §4a D4, checkpoint C7.8).
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is drawn but the values.</b> The leaf is pre-printed by the bank; the printer's whole
/// job is landing text in boxes that already exist, so the page is empty apart from the fields. A
/// border or a background would print over the bank's own artwork.
/// </para>
/// <para>
/// <b>Absolute positioning, hence a Canvas rather than the flowing layout an invoice uses.</b>
/// <c>TradeDocumentPrinter</c> builds paragraphs and lets WPF lay them out, because an invoice grows
/// with its line count. A cheque does not grow: every value has a measured home, and "flow" is
/// precisely the wrong model for stationery.
/// </para>
/// <para>
/// A field name the layout carries but this build does not recognise is skipped, not thrown on. A
/// layout saved against a newer palette must not leave an older build unable to print at all —
/// losing one field is recoverable, losing the cheque run is not.
/// </para>
/// </remarks>
internal static class ChequePrinter
{
    /// <summary>WPF device-independent units per inch.</summary>
    private const double UnitsPerInch = 96d;

    /// <summary>Millimetres per inch, for converting the owner's ruler to WPF's units.</summary>
    private const double MillimetresPerInch = 25.4d;

    /// <summary>A standard CTS-2010 leaf: 202 mm wide.</summary>
    private const double DefaultWidthMm = 202d;

    /// <summary>A standard CTS-2010 leaf: 92 mm tall.</summary>
    private const double DefaultHeightMm = 92d;

    /// <summary>Builds the printable page for one cheque.</summary>
    internal static FixedDocument Build(ChequePrintData cheque, PrintTemplateLayout layout)
    {
        ArgumentNullException.ThrowIfNull(cheque);
        ArgumentNullException.ThrowIfNull(layout);

        var pageWidth = ToUnits(DefaultWidthMm);
        var pageHeight = ToUnits(DefaultHeightMm);

        var canvas = new Canvas
        {
            Width = pageWidth,
            Height = pageHeight,

            // Transparent, not white: a white fill would be printed as ink on some drivers and
            // wash out the bank's own background pattern.
            Background = Brushes.Transparent,
        };

        foreach (var placement in layout.PlacedFields)
        {
            if (!placement.IsVisible)
            {
                continue;
            }

            var text = ValueFor(cheque, placement.Field);

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            canvas.Children.Add(Place(text, placement));
        }

        var page = new FixedPage { Width = pageWidth, Height = pageHeight };
        page.Children.Add(canvas);

        var content = new PageContent();
        ((System.Windows.Markup.IAddChild)content).AddChild(page);

        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);
        document.Pages.Add(content);

        return document;
    }

    /// <summary>
    /// Which value a field name prints. An unknown name yields null and is skipped.
    /// </summary>
    /// <remarks>
    /// The date is rendered <c>ddMMyyyy</c> with no separators, because a CTS-2010 leaf has eight
    /// individual boxes for it and a slash would land in one of them.
    /// </remarks>
    private static string? ValueFor(ChequePrintData cheque, string field) => field switch
    {
        ChequeLayoutDefaults.PayeeField => cheque.PayeeName,
        ChequeLayoutDefaults.DateField =>
            cheque.InstrumentDate.ToString("ddMMyyyy", CultureInfo.InvariantCulture),
        ChequeLayoutDefaults.AmountInWordsField => cheque.AmountInWords,
        ChequeLayoutDefaults.AmountInFiguresField => cheque.AmountInFigures,
        ChequeLayoutDefaults.AccountPayeeField => "A/C PAYEE",
        _ => null,
    };

    private static TextBlock Place(string text, PrintFieldPlacement placement)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = placement.FontSize,
            FontWeight = placement.IsBold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Brushes.Black,
            TextWrapping = placement.WidthMm.HasValue ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };

        if (placement.WidthMm is { } widthMm)
        {
            block.Width = ToUnits(widthMm);
        }

        Canvas.SetLeft(block, ToUnits(placement.LeftMm));
        Canvas.SetTop(block, ToUnits(placement.TopMm));

        return block;
    }

    private static double ToUnits(double millimetres) => millimetres / MillimetresPerInch * UnitsPerInch;
}
