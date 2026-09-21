using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using VinayakaAgencies.App.Presentation;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Printing;
using VinayakaAgencies.Application.Trade;
using VinayakaAgencies.Domain.Printing;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.App.Views;

/// <summary>
/// Interaction logic for PrintPreviewWindow.
/// </summary>
/// <remarks>
/// One Rule-46 copy label per page; the selector flips between them and Print sends them all to
/// the chosen printer in order.
/// </remarks>
public partial class PrintPreviewWindow : Window
{
    private readonly TradeDocumentSummary _document;
    private readonly PrintTemplateLayout _layout;
    private readonly string? _companyName;
    private readonly IReadOnlyList<string> _copyLabels;

    public PrintPreviewWindow(
        TradeDocumentSummary document,
        PrintTemplateDetail? template,
        CompanySummary? company)
    {
        InitializeComponent();

        _document = document;
        _layout = (template is not null ? template.Layout : null) ?? BuiltInLayout(document.Kind.ToPrintKind());
        _companyName = company?.LegalName ?? company?.Name;

        // A template with no copy labels prints one unlabelled copy; a seeded one prints its
        // Rule-46 set, one per page.
        var labels = _layout.CopyLabels;
        _copyLabels = labels.Count > 0 ? labels : [string.Empty];

        CopySelector.ItemsSource = _copyLabels.Select((label, index) => new
        {
            Label = index + 1,
            Name = string.IsNullOrWhiteSpace(label) ? "(single copy)" : label,
        }).ToList();
        CopySelector.DisplayMemberPath = "Name";
        CopySelector.SelectedIndex = 0;

        Title = $"Print preview — {document.Kind} {document.DisplayNumber}";
    }

    private void Render(string copyLabel) =>
        Viewer.Document = TradeDocumentPrinter.Build(_document, _layout, _companyName, copyLabel);

    private static PrintTemplateLayout BuiltInLayout(PrintDocumentKind kind) =>
        new(
            Title: kind.ToString(),
            ShowLogo: false,
            LogoPath: null,
            HeaderLines: [],
            FooterNote: null,
            TermsAndConditions: null,
            VisibleColumns: ["Sr", "Item", "Qty", "Rate", "Amount"],
            CopyLabels: []);

    private void OnCopySelected(object sender, SelectionChangedEventArgs e)
    {
        if (Viewer is not null && CopySelector.SelectedIndex >= 0
            && CopySelector.SelectedIndex < _copyLabels.Count)
        {
            Render(_copyLabels[CopySelector.SelectedIndex]);
        }
    }

    private void OnPrintClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Controls.PrintDialog();

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var label in _copyLabels)
        {
            var flow = TradeDocumentPrinter.Build(_document, _layout, _companyName, label);

            if (flow is System.Windows.Documents.IDocumentPaginatorSource paginator)
            {
                dialog.PrintDocument(
                    paginator.DocumentPaginator,
                    $"{_document.Kind} {_document.DisplayNumber}");
            }
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
