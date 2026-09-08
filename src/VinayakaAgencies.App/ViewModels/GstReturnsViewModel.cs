using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Gst;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The GST returns screen (phase-5-gst-returns.md §1): builds GSTR-1 / GSTR-3B / GSTR-9 for one
/// month of the open financial year from the posted snapshots, and exports the GSTR-1 JSON in the
/// pinned GSTN schema via a save dialog.
/// </summary>
public sealed partial class GstReturnsViewModel : ObservableObject, IScreenShortcuts
{
    public const string ShortcutHints = "Ctrl+A Save · Ctrl+Q Cancel · Alt+G Go To";

    private readonly IGstReturnService _returns;
    private readonly ICompanyService _companies;
    private readonly ICompanyContext _companyContext;
    private DateOnly _fyStartDate = new(DateTime.Today.Year, 4, 1);
    private bool _periodsBuilt;

    [ObservableProperty]
    private ObservableCollection<DateTime> _periodChoices = [];

    [ObservableProperty]
    private DateTime _selectedPeriod = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty]
    private string _selectedReturnType = "GSTR-1";

    [ObservableProperty]
    private bool _isGstr1 = true;

    [ObservableProperty]
    private bool _isGstr3B;

    [ObservableProperty]
    private bool _isGstr9;

    [ObservableProperty]
    private ObservableCollection<object> _b2bRows = [];

    [ObservableProperty]
    private ObservableCollection<object> _b2cRows = [];

    [ObservableProperty]
    private ObservableCollection<object> _hsnRows = [];

    [ObservableProperty]
    private string _documentsSummary = string.Empty;

    [ObservableProperty]
    private ObservableCollection<object> _threeBSections = [];

    [ObservableProperty]
    private ObservableCollection<object> _nineMonthRows = [];

    [ObservableProperty]
    private decimal _totalTaxable;

    [ObservableProperty]
    private decimal _totalIgst;

    [ObservableProperty]
    private decimal _totalCgst;

    [ObservableProperty]
    private decimal _totalSgst;

    [ObservableProperty]
    private decimal _totalCess;

    [ObservableProperty]
    private decimal _totalTax;

    [ObservableProperty]
    private string _status = "Pick a period and press Load.";

    [ObservableProperty]
    private bool _isBusy;

    public GstReturnsViewModel(
        IGstReturnService returns,
        ICompanyService companies,
        ICompanyContext companyContext)
    {
        _returns = returns ?? throw new ArgumentNullException(nameof(returns));
        _companies = companies ?? throw new ArgumentNullException(nameof(companies));
        _companyContext = companyContext ?? throw new ArgumentNullException(nameof(companyContext));
    }

    /// <summary>The return types offered; the labels are the portal's own names.</summary>
    public IReadOnlyList<string> ReturnTypeChoices { get; } = ["GSTR-1", "GSTR-3B", "GSTR-9"];

    public string? StatusBarHints => ShortcutHints;

    public ICommand? AcceptCommand => ExportGstr1JsonCommand;

    public ICommand? CancelCommand => ClearResultsCommand;

    partial void OnSelectedReturnTypeChanged(string value)
    {
        IsGstr1 = value == "GSTR-1";
        IsGstr3B = value == "GSTR-3B";
        IsGstr9 = value == "GSTR-9";
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!_periodsBuilt)
        {
            await BuildPeriodChoicesAsync(cancellationToken).ConfigureAwait(true);
        }

        var fromDate = DateOnly.FromDateTime(SelectedPeriod);
        var toDate = fromDate.AddMonths(1).AddDays(-1);

        IsBusy = true;

        try
        {
            switch (SelectedReturnType)
            {
                case "GSTR-3B":
                    await LoadGstr3BAsync(fromDate, toDate, cancellationToken).ConfigureAwait(true);
                    break;

                case "GSTR-9":
                    await LoadGstr9Async(toDate, cancellationToken).ConfigureAwait(true);
                    break;

                default:
                    await LoadGstr1Async(fromDate, toDate, cancellationToken).ConfigureAwait(true);
                    break;
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

    [RelayCommand]
    private async Task ExportGstr1JsonAsync(CancellationToken cancellationToken)
    {
        if (!_periodsBuilt)
        {
            await BuildPeriodChoicesAsync(cancellationToken).ConfigureAwait(true);
        }

        var fromDate = DateOnly.FromDateTime(SelectedPeriod);
        var toDate = fromDate.AddMonths(1).AddDays(-1);

        IsBusy = true;

        try
        {
            var json = await _returns.ExportGstr1JsonAsync(fromDate, toDate, cancellationToken)
                .ConfigureAwait(true);

            var dialog = new SaveFileDialog
            {
                Title = "Export GSTR-1 JSON",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"GSTR-1_{fromDate:yyyy_MM}.json",
            };

            if (dialog.ShowDialog() != true)
            {
                Status = "Export cancelled.";
                return;
            }

            await File.WriteAllTextAsync(dialog.FileName, json, cancellationToken).ConfigureAwait(true);

            Status = $"Exported GSTR-1 {fromDate:MMM yyyy} to {dialog.FileName} — "
                + $"schema {GstSchemaVersions.CurrentGstr1}, exported {DateTime.Now:dd-MMM-yyyy HH:mm:ss}.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearResults()
    {
        B2bRows = [];
        B2cRows = [];
        HsnRows = [];
        ThreeBSections = [];
        NineMonthRows = [];
        DocumentsSummary = string.Empty;
        SetTotals(0, 0, 0, 0, 0);
        Status = "Ready.";
    }

    private async Task LoadGstr1Async(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var gstr1 = await _returns.BuildGstr1Async(fromDate, toDate, cancellationToken)
            .ConfigureAwait(true);

        B2bRows = new ObservableCollection<object>(gstr1.B2B.SelectMany(invoice => invoice.RateLines
            .Select(line => new
            {
                InvoiceNumber = invoice.InvoiceNumber,
                InvoiceDate = invoice.InvoiceDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
                invoice.CounterpartyGstin,
                PlaceOfSupply = invoice.PlaceOfSupplyStateCode,
                ReverseCharge = invoice.IsReverseCharge ? "RCM" : string.Empty,
                RatePercent = FormatPercent(line.RatePercentMinor),
                TaxableValue = MoneyScale.FromMinor(line.TaxableValueMinor),
                Igst = MoneyScale.FromMinor(line.IgstMinor),
                Cgst = MoneyScale.FromMinor(line.CgstMinor),
                Sgst = MoneyScale.FromMinor(line.SgstMinor),
                Cess = MoneyScale.FromMinor(line.CessMinor),
                InvoiceValue = MoneyScale.FromMinor(invoice.InvoiceValueMinor),
            })));

        B2cRows = new ObservableCollection<object>(gstr1.B2C.Select(row => new
        {
            PlaceOfSupply = row.PlaceOfSupplyStateCode,
            RatePercent = FormatPercent(row.RatePercentMinor),
            row.InvoiceCount,
            TaxableValue = MoneyScale.FromMinor(row.TaxableValueMinor),
            Igst = MoneyScale.FromMinor(row.IgstMinor),
            Cgst = MoneyScale.FromMinor(row.CgstMinor),
            Sgst = MoneyScale.FromMinor(row.SgstMinor),
            Cess = MoneyScale.FromMinor(row.CessMinor),
            InvoiceValue = MoneyScale.FromMinor(row.InvoiceValueMinor),
        }));

        HsnRows = new ObservableCollection<object>(gstr1.HsnSummary.Select(row => new
        {
            row.Code,
            Description = row.Description ?? string.Empty,
            SupplyType = row.IsB2B ? "B2B" : "B2C",
            Quantity = MoneyScale.FromMinor(row.QuantityMinor),
            TaxableValue = MoneyScale.FromMinor(row.TaxableValueMinor),
            Igst = MoneyScale.FromMinor(row.IgstMinor),
            Cgst = MoneyScale.FromMinor(row.CgstMinor),
            Sgst = MoneyScale.FromMinor(row.SgstMinor),
            Cess = MoneyScale.FromMinor(row.CessMinor),
        }));

        DocumentsSummary = $"Documents issued: {gstr1.Documents.InvoicesIssued} "
            + $"(cancelled: {gstr1.Documents.InvoicesCancelled}), "
            + $"{gstr1.Documents.FirstInvoiceNumber} … {gstr1.Documents.LastInvoiceNumber}.";

        SetTotals(gstr1.Totals);

        Status = $"GSTR-1 {fromDate:MMM yyyy}: {B2bRows.Count} B2B rate line(s), "
            + $"{B2cRows.Count} B2C aggregate(s), {HsnRows.Count} Table-12 row(s).";
    }

    private async Task LoadGstr3BAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var summary = await _returns.BuildGstr3BAsync(fromDate, toDate, cancellationToken)
            .ConfigureAwait(true);

        ThreeBSections = new ObservableCollection<object>(
        [
            SectionRow("3.1(a) Outward taxable supplies", summary.OutwardTaxableSupplies),
            SectionRow("3.1(d) Inward reverse-charge supplies", summary.InwardReverseChargeSupplies),
            SectionRow("4(A) Input tax credit claimed", summary.ItcClaimed),
            SectionRow("Net tax payable", summary.NetTaxPayable),
        ]);

        SetTotals(summary.NetTaxPayable);

        Status = summary.ReconcilesTo(await _returns.BuildGstr1Async(fromDate, toDate, cancellationToken)
                .ConfigureAwait(true))
            ? $"GSTR-3B {fromDate:MMM yyyy} built. It agrees with GSTR-1 for the same period exactly."
            : $"GSTR-3B {fromDate:MMM yyyy} built — OUT OF RECONCILIATION with GSTR-1; investigate before filing.";
    }

    private async Task LoadGstr9Async(DateOnly toDate, CancellationToken cancellationToken)
    {
        var summary = await _returns.BuildGstr9Async(_fyStartDate, toDate, cancellationToken)
            .ConfigureAwait(true);

        NineMonthRows = new ObservableCollection<object>(summary.Months.Select(month => new
        {
            Period = $"{new DateTime(month.Year, month.Month, 1):MMM yyyy}",
            TaxableValue = MoneyScale.FromMinor(month.OutwardTaxableSupplies.TaxableValueMinor),
            Igst = MoneyScale.FromMinor(month.OutwardTaxableSupplies.IgstMinor),
            Cgst = MoneyScale.FromMinor(month.OutwardTaxableSupplies.CgstMinor),
            Sgst = MoneyScale.FromMinor(month.OutwardTaxableSupplies.SgstMinor),
            Cess = MoneyScale.FromMinor(month.OutwardTaxableSupplies.CessMinor),
            NetTaxPayable = MoneyScale.FromMinor(month.NetTaxPayable.TotalTaxMinor),
        }));

        SetTotals(summary.OutwardTaxableSupplies);

        Status = $"GSTR-9 {_fyStartDate:MMM yyyy}–{toDate:MMM yyyy}: {NineMonthRows.Count} month(s) aggregated.";
    }

    /// <summary>
    /// Lists the months of the open financial year up to the current month, defaulting the picker
    /// to the current month. The financial-year start month comes from the company master.
    /// </summary>
    private async Task BuildPeriodChoicesAsync(CancellationToken cancellationToken)
    {
        const int defaultFyStartMonth = 4;

        var fyStartMonth = defaultFyStartMonth;

        if (_companyContext.HasCompany)
        {
            var company = await _companies.GetAsync(_companyContext.CompanyId, cancellationToken)
                .ConfigureAwait(true);

            if (company is not null)
            {
                fyStartMonth = company.FinancialYearStartMonth;
            }
        }

        var today = DateTime.Today;
        var fyStartYear = today.Month >= fyStartMonth ? today.Year : today.Year - 1;
        _fyStartDate = new DateOnly(fyStartYear, fyStartMonth, 1);

        var months = new List<DateTime>();
        for (var month = _fyStartDate; month <= DateOnly.FromDateTime(today); month = month.AddMonths(1))
        {
            months.Add(month.ToDateTime(TimeOnly.MinValue));
        }

        PeriodChoices = new ObservableCollection<DateTime>(months);
        SelectedPeriod = months[^1];
        _periodsBuilt = true;
    }

    private static object SectionRow(string label, GstTotals totals) => new
    {
        Section = label,
        TaxableValue = MoneyScale.FromMinor(totals.TaxableValueMinor),
        Igst = MoneyScale.FromMinor(totals.IgstMinor),
        Cgst = MoneyScale.FromMinor(totals.CgstMinor),
        Sgst = MoneyScale.FromMinor(totals.SgstMinor),
        Cess = MoneyScale.FromMinor(totals.CessMinor),
        TotalTax = MoneyScale.FromMinor(totals.TotalTaxMinor),
    };

    private void SetTotals(GstTotals totals) =>
        SetTotals(
            MoneyScale.FromMinor(totals.TaxableValueMinor),
            MoneyScale.FromMinor(totals.IgstMinor),
            MoneyScale.FromMinor(totals.CgstMinor),
            MoneyScale.FromMinor(totals.SgstMinor),
            MoneyScale.FromMinor(totals.CessMinor));

    private void SetTotals(decimal taxable, decimal igst, decimal cgst, decimal sgst, decimal cess)
    {
        TotalTaxable = taxable;
        TotalIgst = igst;
        TotalCgst = cgst;
        TotalSgst = sgst;
        TotalCess = cess;
        TotalTax = igst + cgst + sgst + cess;
    }

    private static string FormatPercent(long percentMinor) =>
        MoneyScale.FromMinor(percentMinor).ToString("0.##", CultureInfo.InvariantCulture);
}
