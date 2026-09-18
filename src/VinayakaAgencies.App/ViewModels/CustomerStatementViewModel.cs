using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VinayakaAgencies.App.Presentation;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Customers;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The customer statement and bill-wise outstanding report (phase-4-customer-pricing.md §2,
/// checkpoints C4.3/C4.6): what one customer was billed, what they paid, and which bills remain
/// open oldest-first. Printable and exportable.
/// </summary>
public sealed partial class CustomerStatementViewModel : ObservableObject, IScreenShortcuts
{
    /// <summary>
    /// Ctrl+P and Ctrl+E (ADR-0007). This is the only screen that has both, and they are on the bar
    /// because they exist here — ADR-0006's rule is that a key is advertised where it works and
    /// nowhere else, which is why no other report shows them.
    /// </summary>
    IReadOnlyList<Presentation.Keyboard.ShortcutButton> IScreenShortcuts.ActionBar =>
    [
        new(Presentation.Keyboard.AppShortcuts.Print, "Print", PrintCommand),
        new(Presentation.Keyboard.AppShortcuts.Export, "Export CSV", ExportCsvCommand),
    ];

    string? IScreenShortcuts.StatusBarHints => "Ctrl+P Print · Ctrl+E Export CSV · Esc Back";

    private readonly ICustomerStatementService _statements;
    private readonly ICustomerService _customers;
    private readonly ICompanyService _companies;
    private readonly ICompanyContext _companyContext;

    [ObservableProperty]
    private ObservableCollection<CustomerSummary> _customerChoices = [];

    [ObservableProperty]
    private CustomerSummary? _selectedCustomer;

    [ObservableProperty]
    private DateTime _fromDate = DateTime.Today.AddMonths(-6);

    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    [ObservableProperty]
    private CustomerStatement? _statement;

    [ObservableProperty]
    private ObservableCollection<OpenBillRow> _openBills = [];

    [ObservableProperty]
    private string _status = "Pick a customer and press Load.";

    [ObservableProperty]
    private bool _isBusy;

    public CustomerStatementViewModel(
        ICustomerStatementService statements,
        ICustomerService customers,
        ICompanyService companies,
        ICompanyContext companyContext)
    {
        _statements = statements ?? throw new ArgumentNullException(nameof(statements));
        _customers = customers ?? throw new ArgumentNullException(nameof(customers));
        _companies = companies ?? throw new ArgumentNullException(nameof(companies));
        _companyContext = companyContext ?? throw new ArgumentNullException(nameof(companyContext));
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;

        try
        {
            // Statements outlive activity: even deactivated customers keep an account here.
            CustomerChoices = new ObservableCollection<CustomerSummary>(
                await _customers.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(true));

            Status = $"{CustomerChoices.Count} customer(s) on the books.";
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
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (SelectedCustomer is null)
        {
            Status = "Pick a customer first.";
            return;
        }

        if (FromDate > ToDate)
        {
            Status = "The from-date must not be after the to-date.";
            return;
        }

        IsBusy = true;

        try
        {
            Statement = await _statements.CustomerStatementAsync(
                    SelectedCustomer.Id,
                    DateOnly.FromDateTime(FromDate),
                    DateOnly.FromDateTime(ToDate),
                    cancellationToken)
                .ConfigureAwait(true);

            OpenBills = new ObservableCollection<OpenBillRow>(
                await _statements.BillwiseOutstandingAsync(SelectedCustomer.Id, cancellationToken)
                    .ConfigureAwait(true));

            if (Statement is null)
            {
                Status = $"No ledger postings found for {SelectedCustomer.Name}.";
                OpenBills = [];
                return;
            }

            var outstandingMinor = MoneyScale.ToMinor(OpenBills.Sum(bill => bill.OutstandingAmount));

            Status = $"{Statement.Rows.Count} posting(s); closing balance ₹{Statement.ClosingBalance:0.##}; "
                     + $"open bills ₹{MoneyScale.FromMinor(outstandingMinor):0.##}.";
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
    private async Task PrintAsync(CancellationToken cancellationToken)
    {
        if (Statement is null)
        {
            Status = "Load a statement first.";
            return;
        }

        IsBusy = true;

        try
        {
            var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(true);
            var company = await _companies.GetAsync(companyId, cancellationToken).ConfigureAwait(true);

            var flow = CustomerStatementPrinter.Build(
                Statement,
                OpenBills.ToList(),
                company?.LegalName ?? company?.Name);

            var dialog = new System.Windows.Controls.PrintDialog();

            if (dialog.ShowDialog() != true)
            {
                Status = "Print cancelled.";
                return;
            }

            if (flow is System.Windows.Documents.IDocumentPaginatorSource paginator)
            {
                dialog.PrintDocument(paginator.DocumentPaginator, $"Statement — {Statement.CustomerName}");
            }

            Status = $"Printed the statement for {Statement.CustomerName}.";
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
    private void ExportCsv()
    {
        if (Statement is null)
        {
            Status = "Load a statement first.";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"statement-{SanitiseFileName(Statement.CustomerName)}-{FromDate:yyyyMMdd}-{ToDate:yyyyMMdd}.csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var csv = new StringBuilder();

        csv.AppendLine("Date,Voucher number,Voucher type,Debit,Credit,Running balance");

        foreach (var row in Statement.Rows)
        {
            csv.AppendLine(string.Join(
                ',',
                Escape(row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Escape(row.VoucherNumber),
                Escape(row.VoucherTypeName),
                row.Debit.ToString(CultureInfo.InvariantCulture),
                row.Credit.ToString(CultureInfo.InvariantCulture),
                row.RunningBalance.ToString(CultureInfo.InvariantCulture)));
        }

        csv.AppendLine();
        csv.AppendLine(
            CultureInfo.InvariantCulture,
            $"Opening balance,,,{Statement.OpeningBalance.ToString("0.####", CultureInfo.InvariantCulture)},,");
        csv.AppendLine(
            CultureInfo.InvariantCulture,
            $"Closing balance,,,{Statement.ClosingBalance.ToString("0.####", CultureInfo.InvariantCulture)},,");

        File.WriteAllText(dialog.FileName, csv.ToString(), Encoding.UTF8);
        Status = $"Exported to {dialog.FileName}.";
    }

    private static string SanitiseFileName(string name)
    {
        foreach (var character in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(character, '-');
        }

        return name;
    }

    private static string Escape(string field) => '"' + field.Replace("\"", "\"\"") + '"';
}
