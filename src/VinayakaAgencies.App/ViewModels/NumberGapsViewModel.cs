using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The voucher-number gap report (spec §8.7): the audit half of decision D2. Gaps are never
/// backfilled — they are reported, with nothing hidden.
/// </summary>
public sealed partial class NumberGapsViewModel : ObservableObject
{
    private readonly IAccountingReportService _reports;
    private readonly IVoucherPostingService _postingService;
    private readonly IFinancialYearProvider _financialYears;

    [ObservableProperty]
    private ObservableCollection<VoucherTypeSummary> _seriesChoices = [];

    [ObservableProperty]
    private VoucherTypeSummary? _selectedSeries;

    [ObservableProperty]
    private ObservableCollection<FinancialYear> _yearsOnRecord = [];

    [ObservableProperty]
    private FinancialYear? _selectedYear;

    [ObservableProperty]
    private ObservableCollection<VoucherNumberGap> _gaps = [];

    [ObservableProperty]
    private string _status = "Press Load lists, then Show gaps.";

    [ObservableProperty]
    private bool _isBusy;

    public NumberGapsViewModel(
        IAccountingReportService reports,
        IVoucherPostingService postingService,
        IFinancialYearProvider financialYears)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
        _postingService = postingService ?? throw new ArgumentNullException(nameof(postingService));
        _financialYears = financialYears ?? throw new ArgumentNullException(nameof(financialYears));
    }

    [RelayCommand]
    private async Task LoadListsAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var types = await _postingService.ListVoucherTypesAsync(cancellationToken).ConfigureAwait(true);
            SeriesChoices = new ObservableCollection<VoucherTypeSummary>(types);

            if (SelectedSeries is null && SeriesChoices.Count > 0)
            {
                SelectedSeries = SeriesChoices[0];
            }

            var years = await _financialYears.ListAsync(cancellationToken).ConfigureAwait(true);
            YearsOnRecord = new ObservableCollection<FinancialYear>(years);

            if (SelectedYear is null && YearsOnRecord.Count > 0)
            {
                SelectedYear = YearsOnRecord[0];
            }

            Status = years.Count == 0
                ? "No financial years on record yet — post a voucher first."
                : $"{SeriesChoices.Count} series across {years.Count} year(s).";
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
    private async Task ShowGapsAsync(CancellationToken cancellationToken)
    {
        if (SelectedSeries?.DefaultSeriesId is not { } seriesId)
        {
            Status = "That voucher type has no default series.";
            return;
        }

        if (SelectedYear is null)
        {
            Status = "Choose a financial year.";
            return;
        }

        IsBusy = true;

        try
        {
            var gaps = await _reports.GetVoucherNumberGapsAsync(
                seriesId,
                SelectedYear.Id,
                cancellationToken)
                .ConfigureAwait(true);

            Gaps = new ObservableCollection<VoucherNumberGap>(gaps);

            Status = gaps.Count == 0
                ? "No gaps — every number from the first to the high-water mark is accounted for."
                : $"{gaps.Count} missing number(s). They are never reused; explain them or leave them visible.";
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
}
