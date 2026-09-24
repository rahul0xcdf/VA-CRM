using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Interop;
using VinayakaAgencies.Domain.Interop;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The import screen (phase-8-interoperability.md §2, §4a D2): pick a file, read the report, then
/// commit it or go and fix the source.
/// </summary>
/// <remarks>
/// <para>
/// The screen is deliberately two steps, because the import is. <b>Check</b> validates the whole file
/// and writes nothing; <b>Ctrl+A</b> commits it, and refuses while any problem is outstanding. §4a D2
/// describes the realistic migration as run, read the report, fix the source, run again — several
/// times over — and this is the shape of that loop.
/// </para>
/// <para>
/// Committing re-validates rather than trusting what is on screen. Between reading the report and
/// pressing the key the owner may well have gone and corrected the file, which is exactly the
/// workflow above.
/// </para>
/// </remarks>
public sealed partial class InteropImportViewModel : ObservableObject, IScreenShortcuts
{
    private readonly IInteropImportService _import;
    private readonly IInteropSourceFactory _sources;
    private readonly IExcelMappingService _mappings;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private bool _importMasters = true;

    [ObservableProperty]
    private bool _importVouchers = true;

    [ObservableProperty]
    private ObservableCollection<ExcelColumnMappingSummary> _mappingChoices = [];

    [ObservableProperty]
    private ExcelColumnMappingSummary? _selectedMapping;

    [ObservableProperty]
    private ObservableCollection<ReportLine> _problems = [];

    [ObservableProperty]
    private ObservableCollection<ReportLine> _warnings = [];

    [ObservableProperty]
    private ObservableCollection<InteropRecordTally> _tallies = [];

    [ObservableProperty]
    private string _status = "Pick a file, then press Check.";

    [ObservableProperty]
    private string _reconciliation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressFraction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
    private bool _isProgressDeterminate;

    [ObservableProperty]
    private string _progressText = string.Empty;

    private InteropImportPreview? _preview;

    public InteropImportViewModel(
        IInteropImportService import,
        IInteropSourceFactory sources,
        IExcelMappingService mappings)
    {
        _import = import ?? throw new ArgumentNullException(nameof(import));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
    }

    /// <summary>True when the chosen file is a spreadsheet, so the mapping picker is needed.</summary>
    public bool IsWorkbook =>
        FilePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
        || FilePath.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase);

    public bool CanCommit => _preview is { CanCommit: true } && !IsBusy;

    /// <summary>The bar sweeps while a total is not yet known, and fills once it is.</summary>
    public bool IsProgressIndeterminate => IsBusy && !IsProgressDeterminate;

    public ICommand? AcceptCommand => CommitCommand;

    public ICommand? CancelCommand => ClearCommand;

    public string? StatusBarHints => "Alt+F Choose file · Ctrl+A Import · Alt+X Clear";

    public IReadOnlyList<ShortcutButton> ActionBar =>
    [
        new ShortcutButton(AppShortcuts.Find, "Choose file", ChooseFileCommand),
        new ShortcutButton(AppShortcuts.Accept, "Import", CanCommit ? CommitCommand : null),
    ];

    [RelayCommand]
    private async Task ChooseFileAsync(CancellationToken cancellationToken)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a file to import",
            Filter = "All supported (*.xml;*.json;*.xlsx)|*.xml;*.json;*.xlsx"
                + "|Tally XML (*.xml)|*.xml"
                + "|Tally JSON (*.json)|*.json"
                + "|Excel workbook (*.xlsx)|*.xlsx"
                + "|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        FilePath = dialog.FileName;
        FileName = Path.GetFileName(dialog.FileName);
        _preview = null;
        Problems = [];
        Warnings = [];
        Tallies = [];
        Reconciliation = string.Empty;

        if (IsWorkbook)
        {
            var available = await _mappings.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
            MappingChoices = new ObservableCollection<ExcelColumnMappingSummary>(available);
            SelectedMapping = available.Count > 0 ? available[0] : null;

            Status = available.Count == 0
                ? "No column mappings are saved yet. Build one on the Column mappings screen first."
                : $"{FileName} selected. Choose the mapping it was laid out in, then press Check.";
        }
        else
        {
            MappingChoices = [];
            SelectedMapping = null;
            Status = $"{FileName} selected. Press Check.";
        }

        OnPropertyChanged(nameof(IsWorkbook));
        RaiseBars();
    }

    [RelayCommand]
    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (await BuildSourceAsync(cancellationToken).ConfigureAwait(true) is not { } source)
        {
            return;
        }

        IsBusy = true;

        try
        {
            _preview = await _import
                .ValidateAsync(source, Options(), Reporter(), cancellationToken)
                .ConfigureAwait(true);

            Show(_preview);

            Status = _preview.Problems.Count > 0
                ? $"{_preview.Problems.Count} problem(s). Nothing will be written until they are fixed."
                : $"{_preview.RecordCount} record(s) ready — {_preview.CreatedCount} new, "
                    + $"{_preview.UpdatedCount} already in the books. Press Ctrl+A to import.";
        }
        catch (InvalidOperationException ex)
        {
            _preview = null;
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RaiseBars();
        }
    }

    [RelayCommand]
    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (await BuildSourceAsync(cancellationToken).ConfigureAwait(true) is not { } source)
        {
            return;
        }

        IsBusy = true;
        RaiseBars();

        try
        {
            var result = await _import
                .CommitAsync(source, Options(), Reporter(), cancellationToken)
                .ConfigureAwait(true);

            _preview = result.Preview;
            Show(result.Preview);

            if (!result.Committed)
            {
                Status = result.RefusalReason ?? "Nothing was written.";
                return;
            }

            Reconciliation = result.Reconciliation?.Describe() ?? string.Empty;

            Status = $"Imported {result.Preview.RecordCount} record(s) from {FileName}."
                + (result.Reconciliation is { IsClean: false }
                    ? " The books do not reconcile — see below."
                    : string.Empty);
        }
        catch (InvalidOperationException ex)
        {
            Status = ex.Message;
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
        _preview = null;
        FilePath = string.Empty;
        FileName = string.Empty;
        Problems = [];
        Warnings = [];
        Tallies = [];
        Reconciliation = string.Empty;
        ProgressText = string.Empty;
        ProgressFraction = 0d;
        IsProgressDeterminate = false;
        Status = "Cleared.";
        OnPropertyChanged(nameof(IsWorkbook));
        RaiseBars();
    }

    /// <summary>
    /// A progress sink that marshals back to the UI thread.
    /// </summary>
    /// <remarks>
    /// <see cref="Progress{T}"/> captures the synchronisation context it is constructed on — the UI
    /// thread here — so the handler does not have to marshal by hand. During validation the total is
    /// unknown and the bar runs indeterminate: a bar sitting at zero through the first half of a long
    /// import says less than a count that keeps moving.
    /// </remarks>
    private Progress<InteropImportProgress> Reporter() => new(report =>
    {
        IsProgressDeterminate = report.IsDeterminate;
        ProgressFraction = report.Fraction;

        ProgressText = report.IsDeterminate
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{report.RecordsRead:N0} of {report.RecordsTotal:N0} {report.CurrentKind}")
            : string.Create(CultureInfo.InvariantCulture, $"{report.RecordsRead:N0} read");
    });

    private InteropImportOptions Options() => new(ImportMasters, ImportVouchers);

    private async Task<IInteropSource?> BuildSourceAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            Status = "Choose a file first.";
            return null;
        }

        try
        {
            if (!IsWorkbook)
            {
                return _sources.ForTallyFile(FilePath);
            }

            if (SelectedMapping is null)
            {
                Status = "A spreadsheet needs a column mapping, so the import knows which column is "
                    + "which. Build one on the Column mappings screen.";
                return null;
            }

            return await _sources
                .ForWorkbookAsync(FilePath, SelectedMapping.Id, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            Status = ex.Message;
            return null;
        }
    }

    private void Show(InteropImportPreview preview)
    {
        Problems = new ObservableCollection<ReportLine>(
            preview.Problems.Select(problem => new ReportLine(
                problem.Location.Description,
                problem.RecordKind,
                problem.RecordLabel,
                problem.Reason)));

        Warnings = new ObservableCollection<ReportLine>(
            preview.Warnings.Select(warning => new ReportLine(
                warning.Location.Description,
                warning.RecordKind,
                warning.RecordLabel,
                warning.Note)));

        Tallies = new ObservableCollection<InteropRecordTally>(
            preview.Tallies.OrderBy(tally => tally.Kind, StringComparer.Ordinal));
    }

    private void RaiseBars()
    {
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(ActionBar));
    }

    /// <summary>One line of the problem or warning report, as the grid shows it.</summary>
    public sealed record ReportLine(string Where, string Kind, string Label, string Reason)
    {
        public override string ToString() =>
            string.Create(CultureInfo.InvariantCulture, $"{Where}: {Kind} '{Label}' — {Reason}");
    }
}
