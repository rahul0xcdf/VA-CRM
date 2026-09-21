using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Payroll;
using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The monthly payroll run: preview, post, and the statutory challan figures
/// (phase-7-tds-payroll-banking.md §4a D3, checkpoint C7.7).
/// </summary>
/// <remarks>
/// <para>
/// Preview first, always. A payroll run is the one posting in the product that touches every
/// employee at once, and an operator needs to see the figures before the voucher exists — so the
/// screen computes on demand and posts only when asked.
/// </para>
/// <para>
/// Warnings are shown, never swallowed. "PF is on but no rate is in force this month" produces
/// zero either way; the difference is whether anybody finds out before the remittance is due.
/// </para>
/// </remarks>
public sealed partial class PayrollRunViewModel : ObservableObject, IScreenShortcuts
{
    public const string ShortcutHints =
        "Ctrl+A Post · Ctrl+Enter Challan summary · Alt+X Recompute";

    private readonly IPayrollRunService _runs;
    private readonly IVoucherPostingService _vouchers;

    [ObservableProperty]
    private ObservableCollection<PaySlip> _paySlips = [];

    [ObservableProperty]
    private ObservableCollection<StatutoryChallanLine> _challanRows = [];

    [ObservableProperty]
    private ObservableCollection<string> _warnings = [];

    [ObservableProperty]
    private ObservableCollection<PostedPayrollSummary> _runsHistory = [];

    [ObservableProperty]
    private PaySlip? _selectedSlip;

    [ObservableProperty]
    private DateTime _periodEnd = LastDayOfLastMonth();

    [ObservableProperty]
    private bool _isChallanVisible;

    [ObservableProperty]
    private string _grossEarnings = "0.00";

    [ObservableProperty]
    private string _totalDeductions = "0.00";

    [ObservableProperty]
    private string _employerContributions = "0.00";

    [ObservableProperty]
    private string _netPayable = "0.00";

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public PayrollRunViewModel(IPayrollRunService runs, IVoucherPostingService vouchers)
    {
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _vouchers = vouchers ?? throw new ArgumentNullException(nameof(vouchers));
    }

    public string? StatusBarHints => ShortcutHints;

    public ICommand? AcceptCommand => PostCommand;

    public ICommand? CancelCommand => PreviewCommand;

    /// <summary>The selected employee's pay-slip lines, for the detail pane.</summary>
    public IReadOnlyList<PaySlipLine> SelectedSlipLines => SelectedSlip?.Lines ?? [];

    /// <inheritdoc />
    public IReadOnlyList<ShortcutButton> ActionBar =>
    [
        new ShortcutButton(AppShortcuts.DrillDown, "Challan summary", ToggleChallanCommand),
    ];

    partial void OnSelectedSlipChanged(PaySlip? value) =>
        OnPropertyChanged(nameof(SelectedSlipLines));

    [RelayCommand]
    private async Task PreviewAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var preview = await _runs
                .PreviewAsync(DateOnly.FromDateTime(PeriodEnd), cancellationToken)
                .ConfigureAwait(true);

            PaySlips = new ObservableCollection<PaySlip>(preview.PaySlips);
            Warnings = new ObservableCollection<string>(preview.Warnings);

            GrossEarnings = Money(preview.GrossEarnings);
            TotalDeductions = Money(preview.TotalDeductions);
            EmployerContributions = Money(preview.EmployerContributions);
            NetPayable = Money(preview.NetPayable);

            Status = preview.PaySlips.Count == 0
                ? $"Nobody was on the payroll in {PeriodEnd:MMMM yyyy}."
                : $"{preview.PaySlips.Count} pay slip(s) computed — not posted yet.";

            if (preview.Warnings.Count > 0)
            {
                Status += $" {preview.Warnings.Count} warning(s).";
            }

            await LoadRunsAsync(cancellationToken).ConfigureAwait(true);
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
    private async Task PostAsync(CancellationToken cancellationToken)
    {
        if (PaySlips.Count == 0)
        {
            Status = "Compute the month first — there is nothing to post.";
            return;
        }

        IsBusy = true;

        try
        {
            var voucherTypes = await _vouchers.ListVoucherTypesAsync(cancellationToken)
                .ConfigureAwait(true);

            var journal = voucherTypes
                .FirstOrDefault(type => type.Kind == VoucherKind.Journal && type.IsActive);

            if (journal is null)
            {
                Status = "No active Journal voucher type is configured to post payroll through.";
                return;
            }

            var posted = await _runs.PostAsync(
                    DateOnly.FromDateTime(PeriodEnd),
                    journal.Id,
                    narration: null,
                    cancellationToken)
                .ConfigureAwait(true);

            await LoadRunsAsync(cancellationToken).ConfigureAwait(true);

            Status = $"Posted {posted.PostedVoucherNumber} for {PeriodEnd:MMMM yyyy} — "
                + $"{posted.EmployeeCount} employee(s), {Money(posted.NetPayable)} net.";
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

    /// <summary>Ctrl+Enter: the challan roll-up, queried only when it is asked for (ADR-0007).</summary>
    [RelayCommand]
    private async Task ToggleChallanAsync(CancellationToken cancellationToken)
    {
        IsChallanVisible = !IsChallanVisible;

        if (!IsChallanVisible)
        {
            Status = "Challan summary closed.";
            return;
        }

        IsBusy = true;

        try
        {
            var periodEnd = DateOnly.FromDateTime(PeriodEnd);

            var challan = await _runs
                .ChallanSummaryAsync(
                    new DateOnly(periodEnd.Year, periodEnd.Month, 1),
                    periodEnd,
                    cancellationToken)
                .ConfigureAwait(true);

            ChallanRows = new ObservableCollection<StatutoryChallanLine>(challan);

            // An empty list is the honest answer when no statute is in force — three zero rows
            // would each claim a scheme applies (C7.7).
            Status = challan.Count == 0
                ? "Nothing statutory to remit — no statute is switched on for this period."
                : $"{challan.Count} statutory remittance(s) for {PeriodEnd:MMMM yyyy}.";
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

    private async Task LoadRunsAsync(CancellationToken cancellationToken)
    {
        RunsHistory = new ObservableCollection<PostedPayrollSummary>(
            await _runs.ListRunsAsync(cancellationToken).ConfigureAwait(true));
    }

    /// <summary>
    /// Payroll is run after the month it covers, so the last day of the previous month is the
    /// sensible landing default rather than today.
    /// </summary>
    private static DateTime LastDayOfLastMonth()
    {
        var firstOfThisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        return firstOfThisMonth.AddDays(-1);
    }

    private static string Money(decimal value) =>
        value.ToString("N2", CultureInfo.InvariantCulture);
}
