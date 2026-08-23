using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.Application.Trade;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The order-outstanding report: what each open sales or purchase order is still waiting for
/// (checkpoint C3.4).
/// </summary>
public sealed partial class OutstandingOrdersViewModel : ObservableObject
{
    private readonly ITradeDocumentService _documents;

    [ObservableProperty]
    private bool _showPurchaseOrders;

    [ObservableProperty]
    private ObservableCollection<OrderOutstandingRow> _rows = [];

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private bool _isBusy;

    public OutstandingOrdersViewModel(ITradeDocumentService documents)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
    }

    public string Caption => ShowPurchaseOrders
        ? "Purchase orders — quantity still awaited"
        : "Sales orders — quantity still to deliver";

    partial void OnShowPurchaseOrdersChanged(bool value) => Status = "Switched. Refresh to reload.";

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            var rows = ShowPurchaseOrders
                ? await _documents.PurchaseOrderOutstandingAsync(cancellationToken).ConfigureAwait(true)
                : await _documents.SalesOrderOutstandingAsync(cancellationToken).ConfigureAwait(true);

            Rows = new ObservableCollection<OrderOutstandingRow>(rows);
            Status = $"{Rows.Count} pending line(s). Partial deliveries leave exactly what remains "
                + "(checkpoint C3.4).";
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
