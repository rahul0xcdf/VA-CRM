using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Gst;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The HSN/SAC master (phase-5-gst-returns.md §2): create, revise, deactivate and reactivate
/// codes, with each code's currently effective rate and compensation cess shown beside it.
/// </summary>
/// <remarks>
/// Per-item assignment lives on the Items screen; this master owns the codes themselves. Nothing
/// tax-related is hardcoded — every rate comes from the schedule service's resolution for today.
/// </remarks>
public sealed partial class HsnSacMasterViewModel : ObservableObject, IScreenShortcuts
{
    public const string ShortcutHints = "Alt+C New · Ctrl+A Save · Alt+D Deactivate · Alt+X Abandon";

    private readonly IHsnSacService _hsnSac;
    private readonly IGstRateService _rates;
    private List<HsnSacCodeRow> _allRows = [];

    [ObservableProperty]
    private ObservableCollection<HsnSacCodeRow> _rows = [];

    [ObservableProperty]
    private HsnSacCodeRow? _selectedRow;

    [ObservableProperty]
    private bool _includeInactive;

    [ObservableProperty]
    private Guid? _editId;

    [ObservableProperty]
    private string _editCode = string.Empty;

    [ObservableProperty]
    private string _editDescription = string.Empty;

    [ObservableProperty]
    private bool _editIsActive = true;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public HsnSacMasterViewModel(IHsnSacService hsnSac, IGstRateService rates)
    {
        _hsnSac = hsnSac ?? throw new ArgumentNullException(nameof(hsnSac));
        _rates = rates ?? throw new ArgumentNullException(nameof(rates));
    }

    public string? StatusBarHints => ShortcutHints;

    public ICommand? AcceptCommand => SaveCommand;

    public ICommand? CancelCommand => ClearEditorCommand;

    /// <summary>
    /// Alt+C and Alt+D (ADR-0007). Deactivate, not delete: an HSN code that has priced an invoice
    /// cannot be removed without orphaning the tax snapshot on it, so this master offers no delete
    /// and Alt+D runs the destructive act it does have.
    /// </summary>
    public IReadOnlyList<ShortcutButton> ActionBar =>
    [
        new ShortcutButton(AppShortcuts.CreateOnTheFly, "New code", ClearEditorCommand),
        new ShortcutButton(AppShortcuts.Delete, "Deactivate", DeactivateSelectedCommand),
    ];

    partial void OnSelectedRowChanged(HsnSacCodeRow? value)
    {
        if (value is null)
        {
            return;
        }

        EditId = value.Id;
        EditCode = value.Code;
        EditDescription = value.Description;
        EditIsActive = value.IsActive;
    }

    partial void OnIncludeInactiveChanged(bool value) => ApplyFilter();

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var summaries = await _hsnSac.ListAsync(includeInactive: true, cancellationToken)
                .ConfigureAwait(true);

            var today = DateOnly.FromDateTime(DateTime.Today);
            var rows = new List<HsnSacCodeRow>(summaries.Count);

            foreach (var summary in summaries)
            {
                var resolved = await _rates.ResolveAsync(summary.Id, today, cancellationToken)
                    .ConfigureAwait(true);

                rows.Add(new HsnSacCodeRow(
                    summary.Id,
                    summary.Code,
                    summary.Description ?? string.Empty,
                    summary.IsActive,
                    MoneyScale.FromMinor(resolved.RatePercentMinor).ToString("0.##", CultureInfo.InvariantCulture),
                    MoneyScale.FromMinor(resolved.CompensationCessPercentMinor).ToString("0.##", CultureInfo.InvariantCulture)));
            }

            _allRows = rows.OrderBy(row => row.Code, StringComparer.Ordinal).ToList();
            ApplyFilter();

            Status = $"{_allRows.Count} code(s) on file.";
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
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(EditCode))
        {
            Status = "Type the HSN/SAC code first.";
            return;
        }

        IsBusy = true;

        try
        {
            var saved = await _hsnSac.SaveAsync(
                new SaveHsnSacRequest(
                    EditId,
                    EditCode.Trim(),
                    NullIfEmpty(EditDescription),
                    EditIsActive),
                cancellationToken)
                .ConfigureAwait(true);

            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            // Refresh overwrites the status with the list summary; the save outcome is what the
            // user just did, so it wins.
            Status = $"Saved '{saved.Code}'.";
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
    private async Task DeactivateSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedRow is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _hsnSac.DeactivateAsync(SelectedRow.Id, cancellationToken).ConfigureAwait(true);
            Status = $"Deactivated '{SelectedRow.Code}'. Its history stays on file.";

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

    [RelayCommand]
    private async Task ActivateSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedRow is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _hsnSac.ActivateAsync(SelectedRow.Id, cancellationToken).ConfigureAwait(true);
            Status = $"Reactivated '{SelectedRow.Code}'.";

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

    [RelayCommand]
    private void ClearEditor()
    {
        SelectedRow = null;
        EditId = null;
        EditCode = string.Empty;
        EditDescription = string.Empty;
        EditIsActive = true;
        Status = "New code — fill the form and save.";
    }

    private void ApplyFilter()
    {
        Rows = new ObservableCollection<HsnSacCodeRow>(
            IncludeInactive
                ? _allRows
                : _allRows.Where(row => row.IsActive));
    }

    private static string? NullIfEmpty(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
