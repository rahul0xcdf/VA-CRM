using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Interop;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The export screen (phase-8-interoperability.md §2): the books written out as a Tally interchange
/// file or a workbook.
/// </summary>
/// <remarks>
/// <para>
/// Building and writing are two steps on screen as well as in the service, because they answer
/// different questions. <b>Preview</b> reads the books and says what is there; <b>Ctrl+E</b> writes
/// it. The owner sends this file to their accountant every month and a preview that shows nothing is
/// worth seeing before the file leaves, not after.
/// </para>
/// <para>
/// One rule is enforced here rather than left to the eye: an export whose vouchers do not balance is
/// refused. Every voucher in the books balances by construction, so an unbalanced export is evidence
/// the selection dropped a line — and it is the accountant who would find out.
/// </para>
/// </remarks>
public sealed partial class InteropExportViewModel : ObservableObject, IScreenShortcuts
{
    private readonly IInteropExportService _export;
    private readonly IInteropWorkbookExporter _workbook;

    [ObservableProperty]
    private bool _includeMasters = true;

    [ObservableProperty]
    private bool _includeVouchers = true;

    [ObservableProperty]
    private DateTime _fromDate = new(DateTime.Today.Year, 4, 1);

    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    [ObservableProperty]
    private bool _wholeBook;

    [ObservableProperty]
    private InteropFormat _format = InteropFormat.TallyXml;

    [ObservableProperty]
    private ObservableCollection<CountedKind> _contents = [];

    [ObservableProperty]
    private string _status = "Choose what to export, then Preview.";

    [ObservableProperty]
    private string _totals = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    private InteropDocument? _built;

    public InteropExportViewModel(IInteropExportService export, IInteropWorkbookExporter workbook)
    {
        _export = export ?? throw new ArgumentNullException(nameof(export));
        _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));
    }

    /// <summary>The formats offered, in the order the picker lists them.</summary>
    public IReadOnlyList<InteropFormat> Formats { get; } =
    [
        InteropFormat.TallyXml,
        InteropFormat.TallyJson,
        InteropFormat.Excel,
    ];

    public bool CanWrite => _built is not null && !IsBusy;

    /// <summary>The date pickers are dead while the whole book is selected.</summary>
    public bool IsDateRangeEnabled => !WholeBook;

    public ICommand? AcceptCommand => WriteFileCommand;

    public ICommand? CancelCommand => ClearCommand;

    public string? StatusBarHints => "Ctrl+E Write file · Ctrl+A Write file · Alt+X Clear";

    public IReadOnlyList<ShortcutButton> ActionBar =>
    [
        // Declared only where it works. A Write entry offered before anything has been previewed
        // would teach the operator that the bar lies (ADR-0007).
        new ShortcutButton(AppShortcuts.Export, "Write file", CanWrite ? WriteFileCommand : null),
    ];

    [RelayCommand]
    private async Task PreviewAsync(CancellationToken cancellationToken)
    {
        if (!IncludeMasters && !IncludeVouchers)
        {
            Status = "Nothing is selected to export.";
            return;
        }

        if (!WholeBook && FromDate > ToDate)
        {
            Status = "The from-date must not be after the to-date.";
            return;
        }

        IsBusy = true;

        try
        {
            var request = new InteropExportRequest(
                IncludeMasters,
                IncludeVouchers,
                WholeBook ? null : DateOnly.FromDateTime(FromDate),
                WholeBook ? null : DateOnly.FromDateTime(ToDate));

            _built = await _export.BuildAsync(request, cancellationToken).ConfigureAwait(true);

            var summary = InteropExportSummary.For(_built, Format);
            Contents = new ObservableCollection<CountedKind>(Describe(summary));

            Totals = string.Create(
                CultureInfo.InvariantCulture,
                $"Debit ₹{Money(summary.TotalDebitMinor):N2} · Credit ₹{Money(summary.TotalCreditMinor):N2}");

            Status = summary.RecordCount == 0
                ? "Nothing matched. Widen the dates, or tick masters as well."
                : $"{summary.RecordCount} record(s) ready. Press Ctrl+E to write the file.";
        }
        catch (InvalidOperationException ex)
        {
            _built = null;
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RaiseBars();
        }
    }

    [RelayCommand]
    private async Task WriteFileAsync(CancellationToken cancellationToken)
    {
        if (_built is null)
        {
            Status = "Preview first, so you can see what is about to be written.";
            return;
        }

        var summary = InteropExportSummary.For(_built, Format);

        if (!summary.IsBalanced)
        {
            Status = "These vouchers do not balance among themselves. That is a fault in the "
                + "selection, not in the file — refusing to write it rather than send the accountant "
                + "a set of books that cannot be posted.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Write export file",
            FileName = SuggestedFileName(),
            Filter = FilterFor(Format),
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true)
        {
            Status = "Export cancelled. Nothing was written.";
            return;
        }

        IsBusy = true;
        RaiseBars();

        try
        {
            var stream = File.Create(dialog.FileName);

            await using (stream.ConfigureAwait(true))
            {
                if (Format == InteropFormat.Excel)
                {
                    await _workbook.WriteAsync(_built, stream, cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    await _export.WriteAsync(_built, Format, stream, cancellationToken).ConfigureAwait(true);
                }
            }

            Status = $"Written to {Path.GetFileName(dialog.FileName)} — {summary.RecordCount} record(s).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"The file could not be written: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            RaiseBars();
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _built = null;
        Contents = [];
        Totals = string.Empty;
        Status = "Cleared.";
        RaiseBars();
    }

    partial void OnWholeBookChanged(bool value)
    {
        _ = value;
        OnPropertyChanged(nameof(IsDateRangeEnabled));
    }

    partial void OnFormatChanged(InteropFormat value)
    {
        _ = value;

        if (_built is not null)
        {
            Contents = new ObservableCollection<CountedKind>(Describe(InteropExportSummary.For(_built, Format)));
        }
    }

    /// <summary>
    /// Tells the shell to re-read the bars.
    /// </summary>
    /// <remarks>
    /// The shell routes keys from the bars it last read, so a stale bar does not merely look wrong —
    /// it fires the wrong command, or none (ADR-0007).
    /// </remarks>
    private void RaiseBars()
    {
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(ActionBar));
    }

    private static IEnumerable<CountedKind> Describe(InteropExportSummary summary)
    {
        yield return new CountedKind("Account groups", summary.AccountGroupCount);
        yield return new CountedKind("Ledgers", summary.LedgerCount);
        yield return new CountedKind("Units", summary.UnitCount);
        yield return new CountedKind("Stock groups", summary.StockGroupCount);
        yield return new CountedKind("Godowns", summary.GodownCount);
        yield return new CountedKind("Items", summary.ItemCount);
        yield return new CountedKind("Voucher types", summary.VoucherTypeCount);
        yield return new CountedKind("Vouchers", summary.VoucherCount);
    }

    private string SuggestedFileName()
    {
        var extension = Format switch
        {
            InteropFormat.TallyJson => "json",
            InteropFormat.Excel => "xlsx",
            _ => "xml",
        };

        var period = WholeBook
            ? "all"
            : string.Create(CultureInfo.InvariantCulture, $"{FromDate:yyyyMMdd}-{ToDate:yyyyMMdd}");

        return $"vinayaka-{period}.{extension}";
    }

    private static string FilterFor(InteropFormat format) => format switch
    {
        InteropFormat.TallyJson => "Tally JSON (*.json)|*.json|All files (*.*)|*.*",
        InteropFormat.Excel => "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
        _ => "Tally XML (*.xml)|*.xml|All files (*.*)|*.*",
    };

    private static decimal Money(long minor) => Domain.Common.MoneyScale.FromMinor(minor);

    /// <summary>One line of the contents list: a record kind and how many of it the export holds.</summary>
    public sealed record CountedKind(string Kind, int Count);
}
