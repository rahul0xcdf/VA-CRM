using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Tds;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Tds;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The TDS/TCS section master and its dated rate rows
/// (phase-7-tds-payroll-banking.md §2, checkpoints C7.2 and C7.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>This screen is checkpoint C7.2.</b> Adding a section is typing a code and a name, then
/// attaching a rate row with its thresholds — two saves, and the section is usable on the next
/// voucher. Nothing is seeded and nothing is special-cased: the April 2026 renumbering is the same
/// two saves plus deactivating the old row.
/// </para>
/// <para>
/// Master key set per ADR-0007: <c>Alt+C</c> new, <c>Ctrl+A</c> save, <c>Alt+D</c> deactivate,
/// <c>Alt+X</c> reload the selected record.
/// </para>
/// </remarks>
public sealed partial class TaxSectionsViewModel : ObservableObject, IScreenShortcuts
{
    public const string ShortcutHints =
        "Alt+C New · Ctrl+A Save · Alt+D Deactivate · Alt+X Reload";

    private readonly ITaxSectionService _sections;
    private List<TaxSectionRow> _allSections = [];

    [ObservableProperty]
    private ObservableCollection<TaxSectionRow> _sectionRows = [];

    [ObservableProperty]
    private ObservableCollection<TaxRateRow> _rateRows = [];

    [ObservableProperty]
    private TaxSectionRow? _selectedSection;

    [ObservableProperty]
    private bool _includeInactive;

    [ObservableProperty]
    private Guid? _editId;

    [ObservableProperty]
    private TaxSectionKind _editKind = TaxSectionKind.Tds;

    [ObservableProperty]
    private string _editCode = string.Empty;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editStatutePaymentCode = string.Empty;

    [ObservableProperty]
    private PayeeCategory _rateCategory = PayeeCategory.Any;

    [ObservableProperty]
    private string _ratePercent = string.Empty;

    [ObservableProperty]
    private string _rateWithoutPanPercent = string.Empty;

    [ObservableProperty]
    private string _singleThreshold = string.Empty;

    [ObservableProperty]
    private string _annualThreshold = string.Empty;

    [ObservableProperty]
    private DateTime _rateEffectiveFrom = DateTime.Today;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public TaxSectionsViewModel(ITaxSectionService sections)
    {
        _sections = sections ?? throw new ArgumentNullException(nameof(sections));
    }

    public string? StatusBarHints => ShortcutHints;

    public ICommand? AcceptCommand => SaveSectionCommand;

    public ICommand? CancelCommand => ReloadSelectedCommand;

    public IReadOnlyList<TaxSectionKind> KindChoices { get; } =
    [
        TaxSectionKind.Tds,
        TaxSectionKind.Tcs,
    ];

    public IReadOnlyList<PayeeCategory> CategoryChoices { get; } =
    [
        PayeeCategory.Any,
        PayeeCategory.IndividualOrHuf,
        PayeeCategory.Other,
    ];

    /// <inheritdoc />
    public IReadOnlyList<ShortcutButton> ActionBar =>
    [
        new ShortcutButton(AppShortcuts.CreateOnTheFly, "New section", ClearEditorCommand),
        new ShortcutButton(AppShortcuts.Delete, "Deactivate", DeactivateSelectedCommand),
    ];

    partial void OnSelectedSectionChanged(TaxSectionRow? value)
    {
        if (value is null)
        {
            return;
        }

        EditId = value.Id;
        EditKind = value.Kind;
        EditCode = value.Code;
        EditName = value.Name;
        EditStatutePaymentCode = value.StatutePaymentCode ?? string.Empty;

        _ = LoadRatesAsync(value.Id, CancellationToken.None);
    }

    partial void OnIncludeInactiveChanged(bool value) => ApplyFilter();

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var summaries = await _sections
                .ListSectionsAsync(kind: null, includeInactive: true, cancellationToken)
                .ConfigureAwait(true);

            _allSections = summaries
                .Select(summary => new TaxSectionRow(
                    summary.Id,
                    summary.Kind,
                    summary.Code,
                    summary.Name,
                    summary.StatutePaymentCode,
                    summary.IsActive))
                .ToList();

            ApplyFilter();

            Status = _allSections.Count == 0
                ? "No sections yet. Nothing is seeded — add the sections this business deducts under."
                : $"{_allSections.Count} section(s) on file.";
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
    private async Task SaveSectionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(EditCode))
        {
            Status = "Type the section code first, exactly as it is written and filed.";
            return;
        }

        if (string.IsNullOrWhiteSpace(EditName))
        {
            Status = "Give the section a name, in your own words.";
            return;
        }

        IsBusy = true;

        try
        {
            var request = new SaveTaxSectionRequest(
                EditKind,
                EditCode.Trim(),
                EditName.Trim(),
                NullIfEmpty(EditStatutePaymentCode));

            var saved = EditId is { } id
                ? await _sections.UpdateSectionAsync(id, request, cancellationToken).ConfigureAwait(true)
                : await _sections.CreateSectionAsync(request, cancellationToken).ConfigureAwait(true);

            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            EditId = saved.Id;
            Status = $"Saved section '{saved.Code}'. Attach a rate row to make it usable.";
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
    private async Task AddRateAsync(CancellationToken cancellationToken)
    {
        if (EditId is not { } sectionId)
        {
            Status = "Save the section first, then attach a rate to it.";
            return;
        }

        if (!TryParsePercent(RatePercent, out var rateMinor))
        {
            Status = "Enter the rate as a percentage — 1, 2, 0.1.";
            return;
        }

        IsBusy = true;

        try
        {
            long? noPanMinor = TryParsePercent(RateWithoutPanPercent, out var parsedNoPan)
                ? parsedNoPan
                : null;

            await _sections.AddRateScheduleAsync(
                    new SaveTaxRateScheduleRequest(
                        sectionId,
                        RateCategory,
                        rateMinor,
                        DateOnly.FromDateTime(RateEffectiveFrom),
                        ParseAmount(SingleThreshold),
                        ParseAmount(AnnualThreshold),
                        noPanMinor),
                    cancellationToken)
                .ConfigureAwait(true);

            await LoadRatesAsync(sectionId, cancellationToken).ConfigureAwait(true);

            Status = $"Rate added from {RateEffectiveFrom:dd-MMM-yyyy}. "
                + "The previous open-ended row was closed the day before.";
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
        if (SelectedSection is null)
        {
            Status = "Pick a section to retire.";
            return;
        }

        IsBusy = true;

        try
        {
            await _sections.SetSectionActiveAsync(SelectedSection.Id, false, cancellationToken)
                .ConfigureAwait(true);

            var code = SelectedSection.Code;
            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            Status = $"Retired '{code}'. Deductions already made under it keep their history.";
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
        if (SelectedSection is null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _sections.SetSectionActiveAsync(SelectedSection.Id, true, cancellationToken)
                .ConfigureAwait(true);

            var code = SelectedSection.Code;
            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            Status = $"Reactivated '{code}'.";
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

    /// <summary>Alt+X on a master screen: reload the selected record, discarding edits.</summary>
    [RelayCommand]
    private void ReloadSelected()
    {
        if (SelectedSection is null)
        {
            ClearEditor();
            return;
        }

        var selected = SelectedSection;
        SelectedSection = null;
        SelectedSection = selected;
        Status = "Reloaded from the books — unsaved edits discarded.";
    }

    [RelayCommand]
    private void ClearEditor()
    {
        SelectedSection = null;
        EditId = null;
        EditKind = TaxSectionKind.Tds;
        EditCode = string.Empty;
        EditName = string.Empty;
        EditStatutePaymentCode = string.Empty;
        RateRows = [];
        Status = "New section — a code, a name, then a rate row. No rebuild needed (C7.2).";
    }

    private async Task LoadRatesAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        try
        {
            var schedules = await _sections.ListRateSchedulesAsync(sectionId, cancellationToken)
                .ConfigureAwait(true);

            RateRows = new ObservableCollection<TaxRateRow>(schedules.Select(schedule =>
                new TaxRateRow(
                    schedule.Id,
                    schedule.PayeeCategory,
                    FormatPercent(schedule.RatePercentMinor),
                    schedule.RateWithoutPanPercentMinor.HasValue
                        ? FormatPercent(schedule.RateWithoutPanPercentMinor.Value)
                        : "—",
                    FormatAmount(schedule.SingleTransactionThresholdMinor),
                    FormatAmount(schedule.AnnualThresholdMinor),
                    schedule.EffectiveFrom,
                    schedule.EffectiveTo)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
    }

    private void ApplyFilter() =>
        SectionRows = new ObservableCollection<TaxSectionRow>(
            (IncludeInactive ? _allSections : _allSections.Where(row => row.IsActive))
            .OrderBy(row => row.Kind)
            .ThenBy(row => row.Code, StringComparer.Ordinal));

    /// <summary>
    /// Reads a percentage into minor units of 1/10,000 of a percent.
    /// </summary>
    /// <remarks>
    /// The operator types "1" or "0.1" — what the notification says — and never the scaled integer.
    /// Invariant parsing, because a machine set to a comma-decimal locale would otherwise read
    /// "0.1" as one.
    /// </remarks>
    private static bool TryParsePercent(string text, out long percentMinor)
    {
        percentMinor = 0L;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!decimal.TryParse(
                text.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var percent)
            || percent < 0m)
        {
            return false;
        }

        percentMinor = MoneyScale.ToMinor(percent);
        return true;
    }

    /// <summary>A blank or unparseable threshold is zero, which means "no such test" (§2).</summary>
    private static long ParseAmount(string text) =>
        decimal.TryParse(text?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
        && value >= 0m
            ? MoneyScale.ToMinor(value)
            : 0L;

    private static string FormatPercent(long percentMinor) =>
        MoneyScale.FromMinor(percentMinor).ToString("0.####", CultureInfo.InvariantCulture) + "%";

    private static string FormatAmount(long amountMinor) =>
        amountMinor == 0L
            ? "—"
            : MoneyScale.FromMinor(amountMinor).ToString("N0", CultureInfo.InvariantCulture);

    private static string? NullIfEmpty(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>One row of the section list.</summary>
public sealed record TaxSectionRow(
    Guid Id,
    TaxSectionKind Kind,
    string Code,
    string Name,
    string? StatutePaymentCode,
    bool IsActive);

/// <summary>One dated rate row of the selected section.</summary>
public sealed record TaxRateRow(
    Guid Id,
    PayeeCategory PayeeCategory,
    string Rate,
    string RateWithoutPan,
    string SingleThreshold,
    string AnnualThreshold,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo)
{
    /// <summary>"Current" reads better than a blank on the row that is still in force.</summary>
    public string EffectiveToText =>
        EffectiveTo?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? "current";

    public string EffectiveFromText =>
        EffectiveFrom.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
}
