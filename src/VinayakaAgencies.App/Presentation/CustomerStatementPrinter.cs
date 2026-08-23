using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using VinayakaAgencies.Application.Customers;

namespace VinayakaAgencies.App.Presentation;

/// <summary>
/// Lays the customer statement out as a plain A4 FlowDocument — the same no-template fallback
/// philosophy the trade-document printer follows: Print always works, before any design exists.
/// </summary>
public static class CustomerStatementPrinter
{
    public static FlowDocument Build(
        CustomerStatement statement,
        IReadOnlyList<OpenBillRow> openBills,
        string? companyName)
    {
        var flow = new FlowDocument
        {
            PagePadding = new(64, 48, 64, 48),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
        };

        if (!string.IsNullOrWhiteSpace(companyName))
        {
            flow.Blocks.Add(new Paragraph(
                new Run(companyName)
                {
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x2A, 0x4A)),
                }));
        }

        flow.Blocks.Add(new Paragraph(
            new Run($"Statement of account — {statement.CustomerName}")
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
            }));

        flow.Blocks.Add(new Paragraph(
            new Run($"For {statement.FromDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)} to "
                    + $"{statement.ToDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}   ·   "
                    + $"Opening balance ₹{statement.OpeningBalance.ToString("0.##", CultureInfo.InvariantCulture)}   ·   "
                    + $"Closing balance ₹{statement.ClosingBalance.ToString("0.##", CultureInfo.InvariantCulture)}"))
        {
            Margin = new(0, 0, 0, 12),
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x6B, 0x85)),
        });

        var table = new Table { CellSpacing = 0 };
        table.Columns.Add(new TableColumn { Width = new(70) });
        table.Columns.Add(new TableColumn { Width = new(150) });
        table.Columns.Add(new TableColumn { Width = new(120) });
        table.Columns.Add(new TableColumn { Width = new(90) });
        table.Columns.Add(new TableColumn { Width = new(90) });
        table.Columns.Add(new TableColumn { Width = new(110) });

        table.RowGroups.Add(new TableRowGroup());

        var header = table.RowGroups[0].Rows;
        header.Add(MakeRow(["Date", "Voucher", "Type", "Debit", "Credit", "Balance"], bold: true));

        foreach (var row in statement.Rows)
        {
            header.Add(MakeRow(
            [
                row.Date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
                row.VoucherNumber,
                row.VoucherTypeName,
                row.Debit == 0m ? string.Empty : row.Debit.ToString("N2", CultureInfo.InvariantCulture),
                row.Credit == 0m ? string.Empty : row.Credit.ToString("N2", CultureInfo.InvariantCulture),
                row.RunningBalance.ToString("N2", CultureInfo.InvariantCulture),
            ]));
        }

        flow.Blocks.Add(table);

        if (openBills.Count > 0)
        {
            flow.Blocks.Add(new Paragraph(
                new Run("Open bills (oldest paid first) — outstanding ₹"
                        + openBills.Sum(bill => bill.OutstandingAmount).ToString("N2", CultureInfo.InvariantCulture))
                {
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                })
            {
                Margin = new(0, 16, 0, 4),
            });

            var bills = new Table { CellSpacing = 0 };
            bills.Columns.Add(new TableColumn { Width = new(140) });
            bills.Columns.Add(new TableColumn { Width = new(90) });
            bills.Columns.Add(new TableColumn { Width = new(110) });
            bills.Columns.Add(new TableColumn { Width = new(110) });
            bills.Columns.Add(new TableColumn { Width = new(110) });
            bills.RowGroups.Add(new TableRowGroup());

            var billRows = bills.RowGroups[0].Rows;
            billRows.Add(MakeRow(["Bill", "Date", "Billed", "Paid", "Outstanding"], bold: true));

            foreach (var bill in openBills)
            {
                billRows.Add(MakeRow(
                [
                    bill.InvoiceNumber,
                    bill.InvoiceDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
                    bill.BilledAmount.ToString("N2", CultureInfo.InvariantCulture),
                    bill.PaidAmount.ToString("N2", CultureInfo.InvariantCulture),
                    bill.OutstandingAmount.ToString("N2", CultureInfo.InvariantCulture),
                ]));
            }

            flow.Blocks.Add(bills);
        }

        return flow;
    }

    private static TableRow MakeRow(IReadOnlyList<string> cells, bool bold = false)
    {
        var row = new TableRow();

        foreach (var text in cells)
        {
            row.Cells.Add(new TableCell(
                new Paragraph(new Run(text) { FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal }))
            {
                Padding = new(4, 2, 4, 2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0xE7, 0xEF)),
                BorderThickness = new(0, 0, 0, 1),
            });
        }

        return row;
    }
}
