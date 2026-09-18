using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Printing;
using VinayakaAgencies.Domain.Trade;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The print-template designer: an operator edits title, header lines, visible columns, terms and
/// copy labels, and saves a new version — no rebuild, no developer (checkpoint C3.2). Two seeded
/// tax-invoice templates demonstrate the priority rule that picks the default (C3.6).
/// </summary>
public sealed partial class PrintTemplatesViewModel : ObservableObject, IScreenShortcuts
{
    /// <summary>The column palette the renderer can print; templates pick from it.</summary>
    public IReadOnlyList<string> ColumnPalette { get; } = ["Sr", "Item", "Qty", "Rate", "Discount", "Amount"];

    /// <summary>Ctrl+A — save the layout as a new version (ADR-0007).</summary>
    ICommand? IScreenShortcuts.AcceptCommand => SaveLayoutCommand;

    /// <summary>Alt+X — abandon the edits and reload the selected template.</summary>
    ICommand? IScreenShortcuts.CancelCommand => LoadSelectedCommand;

    /// <summary>
    /// Ctrl+Enter loads the highlighted template into the designer, which is this screen's drill:
    /// the list says which templates exist, the designer says what one of them is.
    /// </summary>
    IReadOnlyList<ShortcutButton> IScreenShortcuts.ActionBar =>
    [
        new ShortcutButton(AppShortcuts.DrillDown, "Open template", LoadSelectedCommand),
        new ShortcutButton(AppShortcuts.Delete, "Activate/deactivate", ToggleSelectedActiveCommand),
    ];

    string? IScreenShortcuts.StatusBarHints =>
        "Ctrl+Enter Open · Ctrl+A Save as a new version · Alt+D Activate or deactivate";

    [ObservableProperty]
    private TradeDocumentKind? _kindFilter;

    [ObservableProperty]
    private ObservableCollection<PrintTemplateSummary> _templates = [];

    [ObservableProperty]
    private PrintTemplateSummary? _selectedTemplate;

    [ObservableProperty]
    private Guid _editingTemplateId;

    [ObservableProperty]
    private string _title = "TAX INVOICE";

    [ObservableProperty]
    private bool _showLogo;

    [ObservableProperty]
    private string _logoPath = string.Empty;

    [ObservableProperty]
    private string _headerLines = string.Empty;

    [ObservableProperty]
    private string _footerNote = string.Empty;

    [ObservableProperty]
    private string _termsAndConditions = string.Empty;

    [ObservableProperty]
    private bool _columnSr = true;

    [ObservableProperty]
    private bool _columnItem = true;

    [ObservableProperty]
    private bool _columnQty = true;

    [ObservableProperty]
    private bool _columnRate = true;

    [ObservableProperty]
    private bool _columnDiscount;

    [ObservableProperty]
    private bool _columnAmount = true;

    [ObservableProperty]
    private string _copyLabels = "Original for Recipient;Duplicate for Transporter;Triplicate for Supplier";

    [ObservableProperty]
    private ObservableCollection<PrintTemplateRevisionSummary> _revisions = [];

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public IReadOnlyList<TradeDocumentKind> KindChoices { get; } =
    [
        TradeDocumentKind.Quotation,
        TradeDocumentKind.SalesOrder,
        TradeDocumentKind.DeliveryChallan,
        TradeDocumentKind.TaxInvoice,
        TradeDocumentKind.PurchaseOrder,
        TradeDocumentKind.GoodsReceipt,
        TradeDocumentKind.PurchaseInvoice,
    ];

    private readonly IPrintTemplateService _templateService;

    public PrintTemplatesViewModel(IPrintTemplateService templates)
    {
        _templateService = templates ?? throw new ArgumentNullException(nameof(templates));
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var rows = await _templateService.ListAsync(KindFilter, cancellationToken)
                .ConfigureAwait(true);

            Templates = new ObservableCollection<PrintTemplateSummary>(rows);
            Status = $"{Templates.Count} template(s). The lowest active priority per kind prints by default.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedTemplate is null)
        {
            Status = "Pick a template in the list first.";
            return;
        }

        IsBusy = true;

        try
        {
            var detail = await _templateService.GetAsync(SelectedTemplate.Id, cancellationToken)
                .ConfigureAwait(true);

            if (detail is null)
            {
                Status = "That template no longer exists.";
                return;
            }

            EditingTemplateId = detail.Id;
            Title = detail.Layout.Title;
            ShowLogo = detail.Layout.ShowLogo;
            LogoPath = detail.Layout.LogoPath ?? string.Empty;
            HeaderLines = string.Join(Environment.NewLine, detail.Layout.HeaderLines);
            FooterNote = detail.Layout.FooterNote ?? string.Empty;
            TermsAndConditions = detail.Layout.TermsAndConditions ?? string.Empty;
            CopyLabels = string.Join(";", detail.Layout.CopyLabels);

            ColumnSr = detail.Layout.VisibleColumns.Contains("Sr");
            ColumnItem = detail.Layout.VisibleColumns.Contains("Item");
            ColumnQty = detail.Layout.VisibleColumns.Contains("Qty");
            ColumnRate = detail.Layout.VisibleColumns.Contains("Rate");
            ColumnDiscount = detail.Layout.VisibleColumns.Contains("Discount");
            ColumnAmount = detail.Layout.VisibleColumns.Contains("Amount");

            Revisions = new ObservableCollection<PrintTemplateRevisionSummary>(detail.Revisions);
            Status = $"Loaded '{detail.Name}' at version {detail.CurrentVersion}. Saving appends version "
                + $"{detail.CurrentVersion + 1}; earlier versions stay in the list below.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveLayoutAsync(CancellationToken cancellationToken)
    {
        if (EditingTemplateId == Guid.Empty)
        {
            Status = "Load a template before saving to it.";
            return;
        }

        IsBusy = true;

        try
        {
            var detail = await _templateService.SaveLayoutAsync(
                    EditingTemplateId,
                    BuildLayout(),
                    cancellationToken)
                .ConfigureAwait(true);

            Revisions = new ObservableCollection<PrintTemplateRevisionSummary>(detail.Revisions);
            Status = $"Saved as version {detail.CurrentVersion}.";
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Retires the selected template, or brings a retired one back. The default-selection rule
    /// (lowest active priority per kind, checkpoint C3.6) re-runs on every refresh.
    /// </summary>
    [RelayCommand]
    private async Task ToggleSelectedActiveAsync(CancellationToken cancellationToken)
    {
        if (SelectedTemplate is null)
        {
            Status = "Pick a template in the list first.";
            return;
        }

        IsBusy = true;

        try
        {
            var updated = await _templateService.SetActiveAsync(
                    SelectedTemplate.Id,
                    isActive: !SelectedTemplate.IsActive,
                    cancellationToken)
                .ConfigureAwait(true);

            Status = updated.IsActive
                ? $"'{updated.Name}' is active again."
                : $"'{updated.Name}' was retired. It stops being a default candidate but keeps its history.";
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private PrintTemplateLayout BuildLayout()
    {
        var columns = new List<string>();

        if (ColumnSr) columns.Add("Sr");
        if (ColumnItem) columns.Add("Item");
        if (ColumnQty) columns.Add("Qty");
        if (ColumnRate) columns.Add("Rate");
        if (ColumnDiscount) columns.Add("Discount");
        if (ColumnAmount) columns.Add("Amount");

        if (columns.Count == 0)
        {
            Status = "At least one column must stay visible; keeping Item and Amount.";
            columns.Add("Item");
            columns.Add("Amount");
        }

        return new PrintTemplateLayout(
            Title: Title.Trim(),
            ShowLogo: ShowLogo,
            LogoPath: string.IsNullOrWhiteSpace(LogoPath) ? null : LogoPath.Trim(),
            HeaderLines: HeaderLines
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            FooterNote: string.IsNullOrWhiteSpace(FooterNote) ? null : FooterNote.Trim(),
            TermsAndConditions: string.IsNullOrWhiteSpace(TermsAndConditions) ? null : TermsAndConditions.Trim(),
            VisibleColumns: columns,
            CopyLabels: CopyLabels
                .Split([';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList());
    }
}
