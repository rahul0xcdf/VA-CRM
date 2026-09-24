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
/// The column-mapping master (phase-8-interoperability.md §2): which column of the owner's own sheet
/// feeds which field, saved so the second month's import is two keystrokes.
/// </summary>
/// <remarks>
/// <para>
/// The screen opens a real sheet and proposes a mapping rather than showing an empty grid. Most
/// columns are named something close to the field they feed, and making the owner state that for
/// every column of every sheet is work software should do first and ask about second. Anything it
/// could not place is left blank, which is precisely the list worth the owner's attention.
/// </para>
/// <para>
/// The master key set of ADR-0007: <c>Alt+C</c> new, <c>Ctrl+A</c> save, <c>Alt+D</c> deactivate,
/// <c>Alt+X</c> put the editor back to the selected record.
/// </para>
/// </remarks>
public sealed partial class ExcelMappingsViewModel : ObservableObject, IScreenShortcuts
{
    private readonly IExcelMappingService _mappings;

    [ObservableProperty]
    private ObservableCollection<ExcelColumnMappingSummary> _saved = [];

    [ObservableProperty]
    private ExcelColumnMappingSummary? _selected;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private ExcelImportTarget _target = ExcelImportTarget.Items;

    [ObservableProperty]
    private string _sheetName = string.Empty;

    [ObservableProperty]
    private int _headerRowNumber = 1;

    [ObservableProperty]
    private ObservableCollection<MappingColumnEditor> _columns = [];

    [ObservableProperty]
    private ObservableCollection<string> _sheets = [];

    [ObservableProperty]
    private string _status = "Pick a saved mapping, or read a sheet to build one.";

    [ObservableProperty]
    private bool _isBusy;

    private Guid? _editingId;

    public ExcelMappingsViewModel(IExcelMappingService mappings)
    {
        _mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
    }

    public IReadOnlyList<ExcelImportTarget> Targets { get; } = Enum.GetValues<ExcelImportTarget>();

    /// <summary>The fields the current target accepts, for the picker on each line.</summary>
    public IReadOnlyList<string> AvailableFields =>
        [.. InteropFieldCatalogue.For(Target).Select(field => field.Name)];

    /// <summary>What a required field is still unmapped, so the owner sees it before saving.</summary>
    public string MissingRequired
    {
        get
        {
            var mapped = Columns
                .Select(column => column.TargetField)
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missing = InteropFieldCatalogue
                .RequiredFields(Target)
                .Where(field => !mapped.Contains(field))
                .ToList();

            return missing.Count == 0 ? string.Empty : "Still unmapped: " + string.Join(", ", missing);
        }
    }

    public ICommand? AcceptCommand => SaveCommand;

    public ICommand? CancelCommand => ReloadCommand;

    public string? StatusBarHints => "Alt+F Read a sheet · Alt+C New · Ctrl+A Save · Alt+D Deactivate";

    public IReadOnlyList<ShortcutButton> ActionBar =>
    [
        new ShortcutButton(AppShortcuts.Find, "Read a sheet", ReadSheetCommand),
        new ShortcutButton(AppShortcuts.CreateOnTheFly, "New mapping", NewCommand),
        new ShortcutButton(AppShortcuts.Delete, "Deactivate", Selected is null ? null : DeactivateCommand),
    ];

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var saved = await _mappings.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
            Saved = new ObservableCollection<ExcelColumnMappingSummary>(saved);
            Status = saved.Count == 0
                ? "No mappings saved yet. Press Alt+F to read a sheet and build one."
                : $"{saved.Count} mapping(s).";
        }
        finally
        {
            IsBusy = false;
            RaiseBars();
        }
    }

    /// <summary>
    /// Opens a real sheet, and fills the grid with what it found.
    /// </summary>
    /// <remarks>
    /// Deliberately the way a new mapping is started. Typing column headers from memory is how a
    /// mapping ends up naming a column that is spelled differently in the file, and the import then
    /// reports a missing column for a sheet that has it.
    /// </remarks>
    [RelayCommand]
    private async Task ReadSheetAsync(CancellationToken cancellationToken)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Read a sheet's column headers",
            Filter = "Excel workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var sheets = await _mappings.ListSheetsAsync(dialog.FileName, cancellationToken).ConfigureAwait(true);
            Sheets = new ObservableCollection<string>(sheets);

            if (string.IsNullOrWhiteSpace(SheetName) || !sheets.Contains(SheetName, StringComparer.OrdinalIgnoreCase))
            {
                SheetName = sheets.Count > 0 ? sheets[0] : string.Empty;
            }

            var suggestions = await _mappings
                .SuggestAsync(dialog.FileName, Target, SheetName, HeaderRowNumber, cancellationToken)
                .ConfigureAwait(true);

            Columns = new ObservableCollection<MappingColumnEditor>(
                suggestions.Select(suggestion => new MappingColumnEditor(this)
                {
                    SourceHeader = suggestion.SourceHeader,
                    TargetField = suggestion.TargetField ?? string.Empty,
                }));

            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = Path.GetFileNameWithoutExtension(dialog.FileName);
            }

            var placed = suggestions.Count(suggestion => suggestion.TargetField is not null);

            Status = $"{Path.GetFileName(dialog.FileName)}: {suggestions.Count} column(s), {placed} matched. "
                + "Fill in the rest and press Ctrl+A.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(MissingRequired));
            RaiseBars();
        }
    }

    [RelayCommand]
    private void New()
    {
        _editingId = null;
        Selected = null;
        Name = string.Empty;
        SheetName = string.Empty;
        HeaderRowNumber = 1;
        Columns = [];
        Status = "New mapping. Press Alt+F to read the sheet it is for.";
        OnPropertyChanged(nameof(MissingRequired));
        RaiseBars();
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Status = "A mapping needs a name, so it can be picked next time.";
            return;
        }

        var lines = Columns
            .Where(column => !string.IsNullOrWhiteSpace(column.SourceHeader)
                && !string.IsNullOrWhiteSpace(column.TargetField))
            .Select(column => new ExcelColumnMappingLine(column.SourceHeader.Trim(), column.TargetField.Trim()))
            .ToList();

        if (lines.Count == 0)
        {
            Status = "No column is mapped to anything yet.";
            return;
        }

        IsBusy = true;

        try
        {
            var saved = await _mappings.SaveAsync(
                    new SaveExcelColumnMappingRequest(
                        _editingId,
                        Name.Trim(),
                        Target,
                        lines,
                        string.IsNullOrWhiteSpace(SheetName) ? null : SheetName.Trim(),
                        HeaderRowNumber),
                    cancellationToken)
                .ConfigureAwait(true);

            _editingId = saved.Id;
            await LoadAsync(cancellationToken).ConfigureAwait(true);

            Selected = Saved.FirstOrDefault(mapping => mapping.Id == saved.Id);
            Status = $"'{saved.Name}' saved — {saved.Columns.Count} column(s).";
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
    private async Task DeactivateAsync(CancellationToken cancellationToken)
    {
        if (Selected is null)
        {
            return;
        }

        var name = Selected.Name;
        await _mappings.DeleteAsync(Selected.Id, cancellationToken).ConfigureAwait(true);
        await LoadAsync(cancellationToken).ConfigureAwait(true);

        New();
        Status = $"'{name}' deactivated. Imports that used it are still in the edit log.";
    }

    /// <summary>Alt+X — put the editor back to the selected record, discarding what was typed.</summary>
    [RelayCommand]
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        if (Selected is null)
        {
            New();
            return;
        }

        await LoadSelectedAsync(Selected.Id, cancellationToken).ConfigureAwait(true);
        Status = $"'{Selected.Name}' reloaded.";
    }

    partial void OnSelectedChanged(ExcelColumnMappingSummary? value)
    {
        if (value is not null)
        {
            _ = LoadSelectedAsync(value.Id, CancellationToken.None);
        }

        RaiseBars();
    }

    partial void OnTargetChanged(ExcelImportTarget value)
    {
        _ = value;

        // The field list changes with the target, so a line pointing at a field the new target does
        // not have is cleared rather than left to fail at save time.
        foreach (var column in Columns)
        {
            if (!InteropFieldCatalogue.Knows(Target, column.TargetField))
            {
                column.TargetField = string.Empty;
            }
        }

        OnPropertyChanged(nameof(AvailableFields));
        OnPropertyChanged(nameof(MissingRequired));
    }

    private async Task LoadSelectedAsync(Guid id, CancellationToken cancellationToken)
    {
        var detail = await _mappings.GetAsync(id, cancellationToken).ConfigureAwait(true);

        if (detail is null)
        {
            return;
        }

        _editingId = detail.Id;
        Name = detail.Name;
        Target = detail.Target;
        SheetName = detail.SheetName ?? string.Empty;
        HeaderRowNumber = detail.HeaderRowNumber;

        Columns = new ObservableCollection<MappingColumnEditor>(
            detail.Columns.Select(column => new MappingColumnEditor(this)
            {
                SourceHeader = column.SourceHeader,
                TargetField = column.TargetField,
            }));

        OnPropertyChanged(nameof(MissingRequired));
    }

    private void RaiseBars()
    {
        OnPropertyChanged(nameof(ActionBar));
    }

    internal void ColumnChanged() => OnPropertyChanged(nameof(MissingRequired));

    /// <summary>One editable line of the mapping grid.</summary>
    /// <remarks>
    /// Holds a back-reference so a change to either cell re-computes
    /// <see cref="ExcelMappingsViewModel.MissingRequired"/> — the owner should see a required field
    /// go from unmapped to mapped as they fill it in, not when they press save.
    /// </remarks>
    public sealed partial class MappingColumnEditor : ObservableObject
    {
        private readonly ExcelMappingsViewModel _owner;

        [ObservableProperty]
        private string _sourceHeader = string.Empty;

        [ObservableProperty]
        private string _targetField = string.Empty;

        public MappingColumnEditor(ExcelMappingsViewModel owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public IReadOnlyList<string> AvailableFields => _owner.AvailableFields;

        partial void OnTargetFieldChanged(string value)
        {
            _ = value;
            _owner.ColumnChanged();
        }
    }
}
