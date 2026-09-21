using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Payroll;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Payroll;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// Payroll configuration: pay heads, the statutory rates and slabs, and the three flags that
/// decide whether any of it runs (phase-7-tds-payroll-banking.md §4a D3, checkpoint C7.7).
/// </summary>
/// <remarks>
/// <para>
/// <b>The three flags are the screen's point.</b> PF is mandatory above twenty employees and ESI
/// above ten; both thresholds change, and the owner declares whether the business is covered rather
/// than the software computing headcount and deciding. With all three off the payroll module is
/// inert — no statutory line, no rate table read, no challan row.
/// </para>
/// <para>
/// Every rate, ceiling and slab below is entered here and stored as a dated row. Nothing is seeded:
/// §2 extends non-negotiable #6 to all of them by name.
/// </para>
/// </remarks>
public sealed partial class PayrollSettingsViewModel : ObservableObject, IScreenShortcuts
{
    public const string ShortcutHints = "Ctrl+A Save settings · Alt+C New pay head · Alt+X Reload";

    private readonly IPayrollMasterService _payroll;
    private readonly IChartOfAccountsService _chart;

    [ObservableProperty]
    private ObservableCollection<PayHeadSummary> _payHeads = [];

    [ObservableProperty]
    private ObservableCollection<StatutoryRateSummary> _rates = [];

    [ObservableProperty]
    private ObservableCollection<ProfessionalTaxSlabSummary> _slabs = [];

    [ObservableProperty]
    private ObservableCollection<LedgerChoice> _ledgerChoices = [];

    [ObservableProperty]
    private string _salaryPayableLedgerName = string.Empty;

    [ObservableProperty]
    private bool _isProvidentFundEnabled;

    [ObservableProperty]
    private bool _isEmployeeStateInsuranceEnabled;

    [ObservableProperty]
    private bool _isProfessionalTaxEnabled;

    [ObservableProperty]
    private string _headName = string.Empty;

    [ObservableProperty]
    private PayHeadType _headType = PayHeadType.Earning;

    [ObservableProperty]
    private PayComputationBasis _headBasis = PayComputationBasis.FlatAmount;

    [ObservableProperty]
    private StatuteKind? _headStatute;

    [ObservableProperty]
    private LedgerChoice? _headLedger;

    [ObservableProperty]
    private StatuteKind _rateStatute = StatuteKind.ProvidentFund;

    [ObservableProperty]
    private string _employeeRatePercent = string.Empty;

    [ObservableProperty]
    private string _employerRatePercent = string.Empty;

    [ObservableProperty]
    private string _wageCeiling = string.Empty;

    [ObservableProperty]
    private string _eligibilityCeiling = string.Empty;

    [ObservableProperty]
    private DateTime _rateEffectiveFrom = DateTime.Today;

    [ObservableProperty]
    private string _slabFrom = string.Empty;

    [ObservableProperty]
    private string _slabTo = string.Empty;

    [ObservableProperty]
    private string _slabTax = string.Empty;

    [ObservableProperty]
    private DateTime _slabEffectiveFrom = DateTime.Today;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public PayrollSettingsViewModel(IPayrollMasterService payroll, IChartOfAccountsService chart)
    {
        _payroll = payroll ?? throw new ArgumentNullException(nameof(payroll));
        _chart = chart ?? throw new ArgumentNullException(nameof(chart));
    }

    public string? StatusBarHints => ShortcutHints;

    public ICommand? AcceptCommand => SaveSettingsCommand;

    public ICommand? CancelCommand => RefreshCommand;

    public IReadOnlyList<PayHeadType> TypeChoices { get; } =
    [
        PayHeadType.Earning,
        PayHeadType.Deduction,
        PayHeadType.EmployerContribution,
    ];

    public IReadOnlyList<PayComputationBasis> BasisChoices { get; } =
    [
        PayComputationBasis.FlatAmount,
        PayComputationBasis.PercentOfBasic,
        PayComputationBasis.PercentOfGross,
        PayComputationBasis.Statutory,
    ];

    /// <summary>Null is a valid choice — most heads are not statutory.</summary>
    public IReadOnlyList<StatuteKind?> StatuteChoices { get; } =
    [
        null,
        StatuteKind.ProvidentFund,
        StatuteKind.EmployeeStateInsurance,
        StatuteKind.ProfessionalTax,
    ];

    /// <summary>Only the two wage-based statutes have contribution rates; PT has slabs.</summary>
    public IReadOnlyList<StatuteKind> RateStatuteChoices { get; } =
    [
        StatuteKind.ProvidentFund,
        StatuteKind.EmployeeStateInsurance,
    ];

    /// <inheritdoc />
    public IReadOnlyList<ShortcutButton> ActionBar =>
    [
        new ShortcutButton(AppShortcuts.CreateOnTheFly, "Add pay head", AddPayHeadCommand),
    ];

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var settings = await _payroll.GetSettingsAsync(cancellationToken).ConfigureAwait(true);

            SalaryPayableLedgerName = settings.SalaryPayableLedgerName;
            IsProvidentFundEnabled = settings.IsProvidentFundEnabled;
            IsEmployeeStateInsuranceEnabled = settings.IsEmployeeStateInsuranceEnabled;
            IsProfessionalTaxEnabled = settings.IsProfessionalTaxEnabled;

            PayHeads = new ObservableCollection<PayHeadSummary>(
                await _payroll.ListPayHeadsAsync(includeInactive: true, cancellationToken)
                    .ConfigureAwait(true));

            Rates = new ObservableCollection<StatutoryRateSummary>(
                await _payroll.ListStatutoryRatesAsync(statute: null, cancellationToken)
                    .ConfigureAwait(true));

            Slabs = new ObservableCollection<ProfessionalTaxSlabSummary>(
                await _payroll.ListProfessionalTaxSlabsAsync(cancellationToken).ConfigureAwait(true));

            if (LedgerChoices.Count == 0)
            {
                var ledgers = await _chart.ListLedgersAsync(includeInactive: false, cancellationToken)
                    .ConfigureAwait(true);

                LedgerChoices = new ObservableCollection<LedgerChoice>(
                    ledgers.Select(ledger => new LedgerChoice(ledger.Id, ledger.Name)));
            }

            Status = settings.IsProvidentFundEnabled
                || settings.IsEmployeeStateInsuranceEnabled
                || settings.IsProfessionalTaxEnabled
                ? "Loaded."
                : "No statute is switched on — payroll runs without any statutory line.";
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
    private async Task SaveSettingsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            await _payroll.SaveSettingsAsync(
                    new SavePayrollSettingsRequest(
                        SalaryPayableLedgerName,
                        IsProvidentFundEnabled,
                        IsEmployeeStateInsuranceEnabled,
                        IsProfessionalTaxEnabled),
                    cancellationToken)
                .ConfigureAwait(true);

            Status = "Saved. A statute switched on needs a rate row in force before it computes.";
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
    private async Task AddPayHeadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(HeadName))
        {
            Status = "Name the pay head.";
            return;
        }

        if (HeadLedger is null)
        {
            Status = "Pick the ledger this head posts to.";
            return;
        }

        IsBusy = true;

        try
        {
            await _payroll.CreatePayHeadAsync(
                    new SavePayHeadRequest(
                        HeadName.Trim(),
                        HeadType,
                        HeadBasis,
                        HeadLedger.Id,
                        HeadStatute,
                        PayHeads.Count + 1),
                    cancellationToken)
                .ConfigureAwait(true);

            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            Status = $"Added pay head '{HeadName}'.";
            HeadName = string.Empty;
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
        if (!TryParsePercent(EmployeeRatePercent, out var employeeMinor)
            || !TryParsePercent(EmployerRatePercent, out var employerMinor))
        {
            Status = "Enter both contribution rates as percentages — 12, 3.25, 0.75.";
            return;
        }

        IsBusy = true;

        try
        {
            await _payroll.AddStatutoryRateAsync(
                    new SaveStatutoryRateRequest(
                        RateStatute,
                        employeeMinor,
                        employerMinor,
                        DateOnly.FromDateTime(RateEffectiveFrom),
                        ParseOptionalAmount(WageCeiling),
                        ParseOptionalAmount(EligibilityCeiling)),
                    cancellationToken)
                .ConfigureAwait(true);

            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            Status = $"{RateStatute} rate added from {RateEffectiveFrom:dd-MMM-yyyy}.";
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
    private async Task AddSlabAsync(CancellationToken cancellationToken)
    {
        if (ParseOptionalAmount(SlabFrom) is not { } from
            || ParseOptionalAmount(SlabTax) is not { } tax)
        {
            Status = "A slab needs its lower wage and the flat tax charged inside it.";
            return;
        }

        IsBusy = true;

        try
        {
            await _payroll.AddProfessionalTaxSlabAsync(
                    new SaveProfessionalTaxSlabRequest(
                        from,
                        ParseOptionalAmount(SlabTo),
                        tax,
                        DateOnly.FromDateTime(SlabEffectiveFrom)),
                    cancellationToken)
                .ConfigureAwait(true);

            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            SlabFrom = string.Empty;
            SlabTo = string.Empty;
            SlabTax = string.Empty;

            Status = "Slab added. Leave the upper bound blank for the open top band.";
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

    private static bool TryParsePercent(string text, out long percentMinor)
    {
        percentMinor = 0L;

        if (!decimal.TryParse(
                text?.Trim(),
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

    /// <summary>A blank ceiling is null — "this statute has no such rule" — and never zero.</summary>
    private static decimal? ParseOptionalAmount(string text) =>
        decimal.TryParse(text?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
        && value >= 0m
            ? value
            : null;
}
