using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Tds;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The deduction register and the challan figures it rolls up to
/// (phase-7-tds-payroll-banking.md §2, §4a D3's "challan-ready output").
/// </summary>
/// <remarks>
/// <para>
/// A report screen, so it follows ADR-0007's report shape rather than the master one: the challan
/// roll-up is the drill pane, collapsed until <c>Ctrl+Enter</c> asks for it. The register itself is
/// the default view because that is what an operator checking a month wants first; the challan is
/// what they want at the end of it.
/// </para>
/// <para>
/// Every row is a posted snapshot. The rate shown is the rate that was applied, not the rate the
/// master holds today — a return filed in July has to stay reproducible after an August rate change.
/// </para>
/// </remarks>
public sealed partial class TaxDeductionRegisterViewModel : ObservableObject, IScreenShortcuts
{
    public const string ShortcutHints = "Ctrl+Enter Challan summary · Ctrl+A Refresh · Alt+X Clear filters";

    private readonly ITaxDeductionService _deductions;
    private readonly ITaxSectionService _sections;

    [ObservableProperty]
    private ObservableCollection<DeductionRegisterRow> _rows = [];

    [ObservableProperty]
    private ObservableCollection<ChallanRow> _challanRows = [];

    [ObservableProperty]
    private ObservableCollection<TaxSectionChoice> _sectionChoices = [];

    [ObservableProperty]
    private TaxSectionChoice? _selectedSection;

    [ObservableProperty]
    private DateTime _fromDate = FirstOfThisMonth();

    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    [ObservableProperty]
    private bool _isChallanVisible;

    [ObservableProperty]
    private string _totalDeducted = "0.00";

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public TaxDeductionRegisterViewModel(
        ITaxDeductionService deductions,
        ITaxSectionService sections)
    {
        _deductions = deductions ?? throw new ArgumentNullException(nameof(deductions));
        _sections = sections ?? throw new ArgumentNullException(nameof(sections));
    }

    public string? StatusBarHints => ShortcutHints;

    public ICommand? AcceptCommand => RefreshCommand;

    public ICommand? CancelCommand => ClearFiltersCommand;

    /// <inheritdoc />
    public IReadOnlyList<ShortcutButton> ActionBar =>
    [
        new ShortcutButton(AppShortcuts.DrillDown, "Challan summary", ToggleChallanCommand),
    ];

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            if (SectionChoices.Count == 0)
            {
                await LoadSectionsAsync(cancellationToken).ConfigureAwait(true);
            }

            var from = DateOnly.FromDateTime(FromDate);
            var to = DateOnly.FromDateTime(ToDate);

            if (to < from)
            {
                Status = "The period ends before it begins.";
                return;
            }

            var register = await _deductions
                .ListAsync(from, to, SelectedSection?.Id, ledgerId: null, cancellationToken)
                .ConfigureAwait(true);

            Rows = new ObservableCollection<DeductionRegisterRow>(register.Select(row =>
                new DeductionRegisterRow(
                    row.DeductionDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
                    row.SectionCode,
                    row.LedgerName,
                    row.Pan ?? "(no PAN)",
                    Money(row.BaseAmount),
                    Money(row.TaxableBase),
                    Money(row.DeductedAmount),
                    Percent(row.AppliedRatePercentMinor),
                    row.CertificateNumber ?? (row.IsCertificateApplied ? "(certificate)" : "—"))));

            TotalDeducted = Money(register.Sum(row => row.DeductedAmount));

            Status = register.Count == 0
                ? "Nothing was deducted in that period."
                : $"{register.Count} deduction(s), {TotalDeducted} withheld.";

            if (IsChallanVisible)
            {
                await LoadChallanAsync(from, to, cancellationToken).ConfigureAwait(true);
            }
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
    /// Ctrl+Enter: opens the challan roll-up, and loads it only when it is actually asked for.
    /// </summary>
    /// <remarks>
    /// ADR-0007's report rule — the drill pane stays collapsed until asked for, and the query that
    /// fills it does not run until then. The trial balance's old drill-on-selection ran a statement
    /// query per arrow key, and this screen is built not to repeat it.
    /// </remarks>
    [RelayCommand]
    private async Task ToggleChallanAsync(CancellationToken cancellationToken)
    {
        IsChallanVisible = !IsChallanVisible;

        if (!IsChallanVisible)
        {
            Status = "Challan summary closed.";
            return;
        }

        await LoadChallanAsync(
                DateOnly.FromDateTime(FromDate),
                DateOnly.FromDateTime(ToDate),
                cancellationToken)
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ClearFiltersAsync(CancellationToken cancellationToken)
    {
        SelectedSection = null;
        FromDate = FirstOfThisMonth();
        ToDate = DateTime.Today;
        IsChallanVisible = false;

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task LoadChallanAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        try
        {
            var challan = await _deductions.ChallanSummaryAsync(from, to, cancellationToken)
                .ConfigureAwait(true);

            ChallanRows = new ObservableCollection<ChallanRow>(challan.Select(line =>
                new ChallanRow(
                    line.SectionCode,
                    line.Kind.ToString(),
                    line.StatutePaymentCode ?? "—",
                    line.DeducteeCount.ToString(CultureInfo.InvariantCulture),
                    Money(line.TotalBase),
                    Money(line.TotalDeducted))));

            Status = challan.Count == 0
                ? "Nothing to deposit for that period."
                : $"{challan.Count} section(s) to deposit — {Money(challan.Sum(l => l.TotalDeducted))}.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Status = ex.Message;
        }
    }

    private async Task LoadSectionsAsync(CancellationToken cancellationToken)
    {
        var sections = await _sections
            .ListSectionsAsync(kind: null, includeInactive: true, cancellationToken)
            .ConfigureAwait(true);

        SectionChoices = new ObservableCollection<TaxSectionChoice>(
            sections.Select(section => new TaxSectionChoice(
                section.Id,
                $"{section.Code} — {section.Name}")));
    }

    private static DateTime FirstOfThisMonth() =>
        new(DateTime.Today.Year, DateTime.Today.Month, 1);

    private static string Money(decimal value) =>
        value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Percent(long percentMinor) =>
        (percentMinor / 10_000m).ToString("0.####", CultureInfo.InvariantCulture) + "%";
}

/// <summary>One posted deduction, as the register shows it.</summary>
public sealed record DeductionRegisterRow(
    string Date,
    string Section,
    string Party,
    string Pan,
    string BaseAmount,
    string TaxableBase,
    string Deducted,
    string Rate,
    string Certificate);

/// <summary>One section's figure for the period — what the owner pays from.</summary>
public sealed record ChallanRow(
    string Section,
    string Kind,
    string PaymentCode,
    string Deductees,
    string TotalBase,
    string TotalDeducted);

/// <summary>A section in the filter picker.</summary>
public sealed record TaxSectionChoice(Guid Id, string Name);
