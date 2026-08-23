using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The stock reports screen: on-hand, the C2.1 valuation reconciliation, reorder alerts and aging
/// buckets (phase-2-inventory.md §1).
/// </summary>
public sealed partial class StockReportsViewModel : ObservableObject
{
    private readonly IStockReportService _reports;

    [ObservableProperty]
    private DateTime _asOfDate = DateTime.Today;

    [ObservableProperty]
    private ObservableCollection<object> _rows = [];

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public StockReportsViewModel(IStockReportService reports)
    {
        _reports = reports ?? throw new ArgumentNullException(nameof(reports));
    }

    /// <summary>The reconciliation verdict, shown once a reconciliation has been run.</summary>
    [ObservableProperty]
    private Visibility _reconciliationBannerVisibility = Visibility.Collapsed;

    public IReadOnlyList<string> ReportChoices { get; } =
    [
        "On hand",
        "Valuation vs control account (C2.1)",
        "Reorder alerts",
        "Stock aging",
        "Item lookup",
    ];

    [ObservableProperty]
    private string _selectedReport = "On hand";

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        ReconciliationBannerVisibility = Visibility.Collapsed;

        try
        {
            var asOf = DateOnly.FromDateTime(AsOfDate);

            switch (SelectedReport)
            {
                case "On hand":
                    await LoadOnHandAsync(asOf, cancellationToken).ConfigureAwait(true);
                    break;

                case "Valuation vs control account (C2.1)":
                    await LoadReconciliationAsync(asOf, cancellationToken).ConfigureAwait(true);
                    break;

                case "Reorder alerts":
                    await LoadAlertsAsync(asOf, cancellationToken).ConfigureAwait(true);
                    break;

                case "Stock aging":
                    await LoadAgingAsync(asOf, cancellationToken).ConfigureAwait(true);
                    break;

                case "Item lookup":
                    await LoadLookupAsync(cancellationToken).ConfigureAwait(true);
                    break;
            }

            Status = $"{Rows.Count} row(s).";
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

    private async Task LoadOnHandAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        var rows = await _reports.GetOnHandAsync(asOf, godownId: null, cancellationToken)
            .ConfigureAwait(true);

        Rows = new ObservableCollection<object>(rows
            .Select(row => new
            {
                row.ItemName,
                Code = row.ItemCode ?? string.Empty,
                Godown = row.GodownName,
                In = row.QuantityIn.ToString("0.####", CultureInfo.InvariantCulture),
                Out = row.QuantityOut.ToString("0.####", CultureInfo.InvariantCulture),
                OnHand = row.OnHand.ToString("0.####", CultureInfo.InvariantCulture),
                Value = MoneyScale.FromMinor(row.ValueMinor).ToString("N4", CultureInfo.InvariantCulture),
            }));
    }

    private async Task LoadReconciliationAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        var result = await _reports.ReconcileValuationAsync(asOf, cancellationToken)
            .ConfigureAwait(true);

        Rows = new ObservableCollection<object>(result.Items
            .Select(row => new
            {
                row.ItemName,
                OnHand = row.OnHand.ToString("0.####", CultureInfo.InvariantCulture),
                Value = MoneyScale.FromMinor(row.ValueMinor).ToString("N4", CultureInfo.InvariantCulture),
            }));

        Summary = $"Stock value {MoneyScale.FromMinor(result.TotalStockValueMinor):N4}  |  "
            + $"{result.ControlLedgerName} balance {MoneyScale.FromMinor(result.ControlAccountBalanceMinor):N4}";

        ReconciliationBannerVisibility = Visibility.Visible;

        if (result.TotalStockValueMinor != result.ControlAccountBalanceMinor)
        {
            MessageBox.Show(
                "The stock value and the control account do not agree. Investigate before relying "
                + "on either figure.",
                "Reconciliation mismatch",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task LoadAlertsAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        var rows = await _reports.GetReorderAlertsAsync(asOf, cancellationToken).ConfigureAwait(true);

        Rows = new ObservableCollection<object>(rows
            .Select(row => new
            {
                row.ItemName,
                Code = row.ItemCode ?? string.Empty,
                OnHand = row.OnHand.ToString("0.####", CultureInfo.InvariantCulture),
                ReorderLevel = row.ReorderLevel.ToString("0.####", CultureInfo.InvariantCulture),
            }));
    }

    private async Task LoadAgingAsync(DateOnly asOf, CancellationToken cancellationToken)
    {
        var rows = await _reports.GetAgingBucketsAsync(asOf, godownId: null, cancellationToken)
            .ConfigureAwait(true);

        Rows = new ObservableCollection<object>(rows
            .Select(row => new
            {
                row.ItemName,
                Bucket = row.BucketDaysTo == int.MaxValue
                    ? $">{row.BucketDaysFrom} days"
                    : $"{row.BucketDaysFrom}–{row.BucketDaysTo} days",
                Quantity = row.Quantity.ToString("0.####", CultureInfo.InvariantCulture),
                Value = MoneyScale.FromMinor(row.ValueMinor).ToString("N4", CultureInfo.InvariantCulture),
            }));
    }

    private async Task LoadLookupAsync(CancellationToken cancellationToken)
    {
        var rows = await _reports.SearchItemsAsync(string.Empty, take: 500, cancellationToken)
            .ConfigureAwait(true);

        Rows = new ObservableCollection<object>(rows
            .Select(row => new
            {
                row.Name,
                Code = row.Code ?? string.Empty,
                Barcode = row.Barcode ?? string.Empty,
                Unit = row.BaseUnitCode,
                SerialTracked = row.IsSerialTracked ? "yes" : string.Empty,
                Kit = row.IsKit ? "kit" : string.Empty,
            }));
    }
}


