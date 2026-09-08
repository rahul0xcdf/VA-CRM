using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Gst;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The GST rate schedules screen (phase-5-gst-returns.md §2, checkpoint C5.5): dated rate rows
/// per HSN/SAC code. Rows are never edited — a new row supersedes the open-ended one, and the
/// history below stays as the audit evidence for every return built from it.
/// </summary>
public sealed partial class GstRateSchedulesViewModel : ObservableObject, IScreenShortcuts
{
    public const string ShortcutHints = "Ctrl+A Save · Ctrl+Q Cancel · Alt+G Go To";

    private readonly IHsnSacService _hsnSac;
    private readonly IGstRateService _rates;

    [ObservableProperty]
    private ObservableCollection<HsnSacSummary> _codeChoices = [];

    [ObservableProperty]
    private HsnSacSummary? _selectedCode;

    [ObservableProperty]
    private ObservableCollection<object> _scheduleRows = [];

    [ObservableProperty]
    private string _editRatePercent = string.Empty;

    [ObservableProperty]
    private string _editCessPercent = "0";

    [ObservableProperty]
    private DateTime _editEffectiveFrom = DateTime.Today;

    [ObservableProperty]
    private string _status = "Pick a code to see its schedule history.";

    [ObservableProperty]
    private bool _isBusy;

    public GstRateSchedulesViewModel(IHsnSacService hsnSac, IGstRateService rates)
    {
        _hsnSac = hsnSac ?? throw new ArgumentNullException(nameof(hsnSac));
        _rates = rates ?? throw new ArgumentNullException(nameof(rates));
    }

    public string? StatusBarHints => ShortcutHints;

    public ICommand? AcceptCommand => AddScheduleCommand;

    public ICommand? CancelCommand => ClearEditorCommand;

    partial void OnSelectedCodeChanged(HsnSacSummary? value)
    {
        if (value is not null)
        {
            LoadHistoryCommand.Execute(null);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            CodeChoices = new ObservableCollection<HsnSacSummary>(
                await _hsnSac.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(true));

            Status = $"{CodeChoices.Count} code(s) available.";
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
    private async Task LoadHistoryAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var schedules = await _rates.ListAsync(SelectedCode?.Id, cancellationToken)
                .ConfigureAwait(true);

            ScheduleRows = new ObservableCollection<object>(schedules
                .OrderBy(row => row.EffectiveFrom)
                .Select(row => new
                {
                    EffectiveFrom = row.EffectiveFrom.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
                    EffectiveTo = row.EffectiveTo?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? "open",
                    RatePercent = MoneyScale.FromMinor(row.RatePercentMinor).ToString("0.##", CultureInfo.InvariantCulture),
                    CessPercent = MoneyScale.FromMinor(row.CompensationCessPercentMinor).ToString("0.##", CultureInfo.InvariantCulture),
                }));

            Status = SelectedCode is null
                ? $"{ScheduleRows.Count} schedule row(s) across all codes."
                : $"{ScheduleRows.Count} schedule row(s) for {SelectedCode.Code}.";
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
    private async Task AddScheduleAsync(CancellationToken cancellationToken)
    {
        if (SelectedCode is null)
        {
            Status = "Choose the code this schedule applies to first.";
            return;
        }

        if (!TryParsePercent(EditRatePercent, out var ratePercent))
        {
            Status = "The rate percent must be a number of zero or more.";
            return;
        }

        if (!TryParsePercent(EditCessPercent, out var cessPercent))
        {
            Status = "The cess percent must be a number of zero or more.";
            return;
        }

        IsBusy = true;

        try
        {
            var added = await _rates.AddAsync(
                new SaveGstRateScheduleRequest(
                    SelectedCode.Id,
                    MoneyScale.ToMinor(ratePercent),
                    DateOnly.FromDateTime(EditEffectiveFrom),
                    MoneyScale.ToMinor(cessPercent)),
                cancellationToken)
                .ConfigureAwait(true);

            // The service closes any open-ended row the day before the new one begins; say so,
            // because the history grid is about to show exactly that.
            Status = $"Added {MoneyScale.FromMinor(added.RatePercentMinor):0.##}% for {SelectedCode.Code} "
                + $"from {added.EffectiveFrom:dd-MMM-yyyy}. Any earlier open-ended row was closed automatically.";

            EditRatePercent = string.Empty;
            EditCessPercent = "0";

            await LoadHistoryAsync(cancellationToken).ConfigureAwait(true);
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
        EditRatePercent = string.Empty;
        EditCessPercent = "0";
        EditEffectiveFrom = DateTime.Today;
        Status = "Ready.";
    }

    private static bool TryParsePercent(string text, out decimal percent)
    {
        var ok = decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out percent);
        return ok && percent >= 0m;
    }
}
