using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using VinayakaAgencies.Application.Gst;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The GSTR-2B reconciliation screen (phase-5-gst-returns.md §1, checkpoint C5.4): imports a
/// portal-format GSTR-2B JSON file, classifies every line against the books' posted purchase
/// invoices — Matched / MissingInBooks / MissingInPortal — and shows counts and value totals per
/// classification alongside the grouped detail.
/// </summary>
public sealed partial class Gstr2BReconciliationViewModel : ObservableObject, IScreenShortcuts
{
    public const string ShortcutHints = "Ctrl+A Save · Alt+X Cancel";

    private readonly IGstReturnService _returns;

    [ObservableProperty]
    private DateTime _booksFromDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    [ObservableProperty]
    private DateTime _booksToDate = DateTime.Today;

    [ObservableProperty]
    private ObservableCollection<object> _results = [];

    [ObservableProperty]
    private int _matchedCount;

    [ObservableProperty]
    private int _missingInBooksCount;

    [ObservableProperty]
    private int _missingInPortalCount;

    [ObservableProperty]
    private decimal _matchedValue;

    [ObservableProperty]
    private decimal _missingInBooksValue;

    [ObservableProperty]
    private decimal _missingInPortalValue;

    [ObservableProperty]
    private string _matchedSummary = "0 · ₹0.00";

    [ObservableProperty]
    private string _missingInBooksSummary = "0 · ₹0.00";

    [ObservableProperty]
    private string _missingInPortalSummary = "0 · ₹0.00";

    [ObservableProperty]
    private string _importedFileName = string.Empty;

    [ObservableProperty]
    private string _status = "Import a portal-format GSTR-2B JSON file to reconcile.";

    [ObservableProperty]
    private bool _isBusy;

    public Gstr2BReconciliationViewModel(IGstReturnService returns)
    {
        _returns = returns ?? throw new ArgumentNullException(nameof(returns));
    }

    public string? StatusBarHints => ShortcutHints;

    public ICommand? AcceptCommand => ImportAndReconcileCommand;

    public ICommand? CancelCommand => ClearResultsCommand;

    [RelayCommand]
    private async Task ImportAndReconcileAsync(CancellationToken cancellationToken)
    {
        if (BooksFromDate > BooksToDate)
        {
            Status = "The books from-date must not be after the to-date.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import portal GSTR-2B JSON",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true)
        {
            Status = "Import cancelled.";
            return;
        }

        IsBusy = true;

        try
        {
            var json = await File.ReadAllTextAsync(dialog.FileName, cancellationToken)
                .ConfigureAwait(true);

            var result = await _returns.ReconcileGstr2BAsync(
                json,
                DateOnly.FromDateTime(BooksFromDate),
                DateOnly.FromDateTime(BooksToDate),
                cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            Results = new ObservableCollection<object>(result.Results.Select(row => new
            {
                Classification = row.Classification.ToString(),
                row.SupplierGstin,
                PortalInvoiceNumber = row.InvoiceNumber,
                PortalInvoiceDate = FormatDate(row.PortalInvoiceDate),
                PortalInvoiceValue = MoneyScale.FromMinor(row.PortalInvoiceValueMinor ?? 0),
                BooksInvoiceNumber = row.BooksInvoiceNumber ?? string.Empty,
                BooksInvoiceDate = FormatDate(row.BooksInvoiceDate),
                BooksInvoiceValue = MoneyScale.FromMinor(row.BooksInvoiceValueMinor ?? 0),
                MismatchDetails = string.Join("; ", row.MismatchDetails),
            }));

            MatchedCount = result.MatchedCount;
            MissingInBooksCount = result.MissingInBooksCount;
            MissingInPortalCount = result.MissingInPortalCount;

            (MatchedValue, MissingInBooksValue, MissingInPortalValue) = SumValuesPerClassification(result.Results);

            MatchedSummary = FormatSummary(MatchedCount, MatchedValue);
            MissingInBooksSummary = FormatSummary(MissingInBooksCount, MissingInBooksValue);
            MissingInPortalSummary = FormatSummary(MissingInPortalCount, MissingInPortalValue);

            ImportedFileName = Path.GetFileName(dialog.FileName);
            Status = $"{Path.GetFileName(dialog.FileName)} vs books {DateOnly.FromDateTime(BooksFromDate):dd-MMM-yyyy}"
                + $"–{DateOnly.FromDateTime(BooksToDate):dd-MMM-yyyy}: {MatchedCount} matched, "
                + $"{MissingInBooksCount} missing in books, {MissingInPortalCount} missing in portal.";
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or ArgumentException or IOException or JsonException)
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
        Results = [];
        MatchedCount = 0;
        MissingInBooksCount = 0;
        MissingInPortalCount = 0;
            MatchedValue = 0;
            MissingInBooksValue = 0;
            MissingInPortalValue = 0;
            MatchedSummary = "0 · ₹0.00";
            MissingInBooksSummary = "0 · ₹0.00";
            MissingInPortalSummary = "0 · ₹0.00";
            ImportedFileName = string.Empty;
        Status = "Ready.";
    }

    /// <summary>Value totals per classification: the portal value where present, else the books'.
    /// A single pass keeps the three buckets consistent with the detail rows.</summary>
    private static (decimal Matched, decimal MissingInBooks, decimal MissingInPortal) SumValuesPerClassification(
        IReadOnlyList<Gstr2BMatchResult> results)
    {
        decimal matched = 0;
        decimal missingInBooks = 0;
        decimal missingInPortal = 0;

        foreach (var row in results)
        {
            var value = MoneyScale.FromMinor(row.PortalInvoiceValueMinor ?? row.BooksInvoiceValueMinor ?? 0);

            switch (row.Classification)
            {
                case Gstr2bClassification.Matched:
                    matched += value;
                    break;

                case Gstr2bClassification.MissingInBooks:
                    missingInBooks += value;
                    break;

                case Gstr2bClassification.MissingInPortal:
                    missingInPortal += value;
                    break;
            }
        }

        return (matched, missingInBooks, missingInPortal);
    }

    private static string FormatSummary(int count, decimal value) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} · ₹{value:N2}");

    private static string FormatDate(DateOnly? date) =>
        date?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
}
