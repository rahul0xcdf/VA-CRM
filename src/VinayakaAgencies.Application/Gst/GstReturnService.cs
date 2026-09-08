using System.Globalization;
using System.Text.Json;
using VinayakaAgencies.Application.Common;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Application.Gst;

/// <summary>
/// Builds GSTR-1/3B/9 from posted documents' tax snapshots and reconciles imported GSTR-2B files
/// against the books (phase-5-gst-returns.md §2).
/// </summary>
/// <remarks>
/// Every figure is read through the reporting seam from the snapshots the posting flow wrote —
/// never recomputed, never re-resolved — so GSTR-3B equals GSTR-1 equals the books to the minor
/// unit by construction (checkpoint C5.3). The reconciliation matches on supplier GSTIN + invoice
/// number and reports value differences beyond tolerance rather than silently absorbing them
/// (checkpoint C5.4). Export/SEZ/e-commerce sections are deliberately absent: domestic B2B + B2C
/// only (spec §4a), omitted entirely instead of emitted empty.
/// </remarks>
public sealed class GstReturnService(
    IUnitOfWork unitOfWork,
    ISqlQueryExecutor queryExecutor,
    ICompanyContext companyContext,
    IClock clock) : IGstReturnService
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork
        ?? throw new ArgumentNullException(nameof(unitOfWork));

    private readonly ISqlQueryExecutor _queryExecutor = queryExecutor
        ?? throw new ArgumentNullException(nameof(queryExecutor));

    private readonly ICompanyContext _companyContext = companyContext
        ?? throw new ArgumentNullException(nameof(companyContext));

    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <inheritdoc />
    public async Task<Gstr1Return> BuildGstr1Async(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        var (companyId, gstin) = await RequireCompanyAsync(cancellationToken).ConfigureAwait(false);

        var rows = await SalesLinesAsync(companyId, fromDate, toDate, cancellationToken)
            .ConfigureAwait(false);

        var b2b = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.PartyGstin))
            .GroupBy(row => row.DocumentId)
            .Select(group => ToB2BInvoice(group.ToList()))
            .OrderBy(invoice => invoice.InvoiceDate)
            .ThenBy(invoice => invoice.InvoiceNumber, StringComparer.Ordinal)
            .ToList();

        var b2c = rows
            .Where(row => string.IsNullOrWhiteSpace(row.PartyGstin))
            .GroupBy(row => (row.PlaceOfSupplyStateCode, row.RatePercentMinor))
            .Select(group =>
            {
                var taxable = group.Sum(row => row.TaxableValueMinor);
                var igst = group.Sum(row => row.IgstMinor);
                var cgst = group.Sum(row => row.CgstMinor);
                var sgst = group.Sum(row => row.SgstMinor);
                var cess = group.Sum(row => row.CessMinor);

                return new Gstr1B2CAggregate(
                    group.Key.PlaceOfSupplyStateCode,
                    group.Key.RatePercentMinor,
                    group.Select(row => row.DocumentId).Distinct().Count(),
                    taxable,
                    igst,
                    cgst,
                    sgst,
                    cess,
                    taxable + igst + cgst + sgst + cess);
            })
            .OrderBy(aggregate => aggregate.PlaceOfSupplyStateCode, StringComparer.Ordinal)
            .ThenBy(aggregate => aggregate.RatePercentMinor)
            .ToList();

        var hsn = BuildHsnSummary(rows);
        var totals = ToTotals(rows);

        return new Gstr1Return(
            fromDate,
            toDate,
            gstin,
            b2b,
            b2c,
            hsn,
            await DocumentIssueSummaryAsync(companyId, fromDate, toDate, cancellationToken)
                .ConfigureAwait(false),
            totals);
    }

    /// <inheritdoc />
    public async Task<Gstr3BSummary> BuildGstr3BAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        var gstr1 = await BuildGstr1Async(fromDate, toDate, cancellationToken).ConfigureAwait(false);

        var purchases = await PurchaseLinesAsync(fromDate, toDate, cancellationToken)
            .ConfigureAwait(false);

        var rcm = ToTotals(purchases.Where(row => row.IsReverseCharge).ToList());
        var itc = ToTotals(purchases.Where(row => !row.IsReverseCharge).ToList());

        var outward = gstr1.Totals;

        var net = new GstTotals(
            outward.TaxableValueMinor - itc.TaxableValueMinor,
            outward.IgstMinor + rcm.IgstMinor - itc.IgstMinor,
            outward.CgstMinor + rcm.CgstMinor - itc.CgstMinor,
            outward.SgstMinor + rcm.SgstMinor - itc.SgstMinor,
            outward.CessMinor + rcm.CessMinor - itc.CessMinor);

        return new Gstr3BSummary(
            fromDate,
            toDate,
            gstr1.Gstin,
            outward,
            rcm,
            itc,
            net);
    }

    /// <inheritdoc />
    public async Task<Gstr9AnnualSummary> BuildGstr9Async(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        var (companyId, gstin) = await RequireCompanyAsync(cancellationToken).ConfigureAwait(false);

        var sales = await SalesLinesAsync(companyId, fromDate, toDate, cancellationToken)
            .ConfigureAwait(false);
        var purchases = await PurchaseLinesAsync(fromDate, toDate, cancellationToken)
            .ConfigureAwait(false);

        var months = new List<Gstr9MonthRow>();

        for (var month = fromDate; month <= toDate; month = month.AddMonths(1))
        {
            var monthEnd = EndOfMonth(month);
            var windowEnd = monthEnd < toDate ? monthEnd : toDate;

            var monthSales = sales
                .Where(row => InWindow(row.DocumentDate, month, windowEnd))
                .ToList();
            var monthPurchases = purchases
                .Where(row => InWindow(row.DocumentDate, month, windowEnd))
                .ToList();

            var outward = ToTotals(monthSales);
            var itc = ToTotals(monthPurchases.Where(row => !row.IsReverseCharge).ToList());
            var rcm = ToTotals(monthPurchases.Where(row => row.IsReverseCharge).ToList());

            months.Add(new Gstr9MonthRow(
                month.Year,
                month.Month,
                outward,
                itc,
                NetTax(outward, itc, rcm)));
        }

        var totalOutward = months.Aggregate(
            new GstTotals(0, 0, 0, 0, 0),
            (current, row) => current + row.OutwardTaxableSupplies);
        var totalItc = months.Aggregate(
            new GstTotals(0, 0, 0, 0, 0),
            (current, row) => current + row.ItcClaimed);
        var totalNet = months.Aggregate(
            new GstTotals(0, 0, 0, 0, 0),
            (current, row) => current + row.NetTaxPayable);

        return new Gstr9AnnualSummary(fromDate, toDate, gstin, months, totalOutward, totalItc, totalNet);
    }

    /// <inheritdoc />
    public async Task<Gstr2BReconciliationResult> ReconcileGstr2BAsync(
        string importedJson,
        DateOnly booksFromDate,
        DateOnly booksToDate,
        long valueToleranceMinor = 100L,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(importedJson);

        var portal = ParsePortalInvoices(importedJson);

        var purchaseDocuments = (await PurchaseLinesAsync(
                    booksFromDate, booksToDate, cancellationToken)
                .ConfigureAwait(false))
            .GroupBy(row => row.DocumentId)
            .Select(group =>
            {
                var head = group.First();
                var value = group.Sum(row =>
                    row.TaxableValueMinor
                    + row.IgstMinor + row.CgstMinor + row.SgstMinor + row.CessMinor);

                return new BooksPurchase(
                    head.Document,
                    head.PartyGstin,
                    SupplierInvoiceNumber(head),
                    head.DocumentNumber,
                    head.DocumentDate,
                    value);
            })
            .ToList();

        var results = new List<Gstr2BMatchResult>();
        var matchedBookIds = new HashSet<Guid>();

        foreach (var portalInvoice in portal)
        {
            var key = MatchKey(portalInvoice.SupplierGstin, portalInvoice.InvoiceNumber);

            var book = purchaseDocuments.FirstOrDefault(candidate =>
                MatchKey(candidate.SupplierGstin, candidate.SupplierInvoiceNumber) == key);

            if (book is null)
            {
                results.Add(new Gstr2BMatchResult(
                    Gstr2bClassification.MissingInBooks,
                    portalInvoice.SupplierGstin ?? string.Empty,
                    portalInvoice.InvoiceNumber,
                    portalInvoice.InvoiceDate,
                    portalInvoice.InvoiceValueMinor,
                    null,
                    null,
                    null,
                    null,
                    []));
                continue;
            }

            matchedBookIds.Add(book.Document);

            var details = CompareValues(portalInvoice, book.ValueMinor, valueToleranceMinor);

            results.Add(new Gstr2BMatchResult(
                Gstr2bClassification.Matched,
                portalInvoice.SupplierGstin ?? string.Empty,
                portalInvoice.InvoiceNumber,
                portalInvoice.InvoiceDate,
                portalInvoice.InvoiceValueMinor,
                book.Document,
                book.DocumentNumber,
                book.BookDate,
                book.ValueMinor,
                details));
        }

        foreach (var book in purchaseDocuments.Where(candidate => !matchedBookIds.Contains(candidate.Document)))
        {
            results.Add(new Gstr2BMatchResult(
                Gstr2bClassification.MissingInPortal,
                book.SupplierGstin ?? string.Empty,
                book.SupplierInvoiceNumber ?? book.DocumentNumber,
                null,
                null,
                book.Document,
                book.DocumentNumber,
                book.BookDate,
                book.ValueMinor,
                []));
        }

        return new Gstr2BReconciliationResult(
            booksFromDate,
            booksToDate,
            results.OrderBy(row => row.Classification)
                .ThenBy(row => row.InvoiceNumber, StringComparer.Ordinal)
                .ToList());
    }

    /// <inheritdoc />
    public async Task<string> ExportGstr1JsonAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        var gstr1 = await BuildGstr1Async(fromDate, toDate, cancellationToken).ConfigureAwait(false);

        return Gstr1JsonExporter.Export(gstr1, _clock);
    }

    // -----------------------------------------------------------------------------------------
    // Line reads
    // -----------------------------------------------------------------------------------------

    private async Task<(Guid CompanyId, string Gstin)> RequireCompanyAsync(
        CancellationToken cancellationToken)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        var company = await _unitOfWork.Repository<Domain.Entities.Company>()
            .GetByIdAsync(companyId, cancellationToken)
            .ConfigureAwait(false);

        return company is null
            ? throw new InvalidOperationException("No company exists to build a return for.")
            : (companyId, company.Gstin ?? string.Empty);
    }

    private async Task<IReadOnlyList<InvoiceLineRow>> SalesLinesAsync(
        Guid companyId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken) =>
        await _queryExecutor.QueryAsync<InvoiceLineRow>(
            GstQueryKeys.SalesInvoiceLinesForPeriod,
            new { CompanyId = companyId, FromDate = CalendarText.For(fromDate), ToDate = CalendarText.For(toDate) },
            cancellationToken)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<InvoiceLineRow>> PurchaseLinesAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var companyId = await _companyContext.EnsureCompanyAsync(cancellationToken).ConfigureAwait(false);

        return await _queryExecutor.QueryAsync<InvoiceLineRow>(
            GstQueryKeys.PurchaseInvoiceLinesForPeriod,
            new { CompanyId = companyId, FromDate = CalendarText.For(fromDate), ToDate = CalendarText.For(toDate) },
            cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Gstr1DocumentIssueSummary> DocumentIssueSummaryAsync(
        Guid companyId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var statusRows = await _queryExecutor.QueryAsync<DocumentStatusCountRow>(
            GstQueryKeys.TaxInvoiceStatusCountsForPeriod,
            new { CompanyId = companyId, FromDate = CalendarText.For(fromDate), ToDate = CalendarText.For(toDate) },
            cancellationToken)
            .ConfigureAwait(false);

        var numbers = await _queryExecutor.QueryAsync<InvoiceNumberRangeRow>(
            GstQueryKeys.SalesInvoiceNumbersForPeriod,
            new { CompanyId = companyId, FromDate = CalendarText.For(fromDate), ToDate = CalendarText.For(toDate) },
            cancellationToken)
            .ConfigureAwait(false);

        var first = numbers.OrderBy(row => row.Number, StringComparer.Ordinal).FirstOrDefault();
        var last = numbers.OrderByDescending(row => row.Number, StringComparer.Ordinal).FirstOrDefault();

        return new Gstr1DocumentIssueSummary(
            statusRows.Where(row => row.Status == nameof(Domain.Trade.TradeDocumentStatus.Issued)).Sum(row => row.Count),
            statusRows.Where(row => row.Status == nameof(Domain.Trade.TradeDocumentStatus.Cancelled)).Sum(row => row.Count),
            first?.Number ?? string.Empty,
            last?.Number ?? string.Empty);
    }

    // -----------------------------------------------------------------------------------------
    // Aggregation
    // -----------------------------------------------------------------------------------------

    private static Gstr1B2BInvoice ToB2BInvoice(IReadOnlyList<InvoiceLineRow> documentRows)
    {
        var head = documentRows[0];

        var rateLines = documentRows
            .GroupBy(row => row.RatePercentMinor)
            .Select(group => new GstRateLine(
                group.Key,
                group.Sum(row => row.TaxableValueMinor),
                group.Sum(row => row.IgstMinor),
                group.Sum(row => row.CgstMinor),
                group.Sum(row => row.SgstMinor),
                group.Sum(row => row.CessMinor)))
            .OrderBy(line => line.RatePercentMinor)
            .ToList();

        var value = documentRows.Sum(row =>
            row.TaxableValueMinor + row.IgstMinor + row.CgstMinor + row.SgstMinor + row.CessMinor);

        return new Gstr1B2BInvoice(
            head.Document,
            head.DocumentNumber,
            head.DocumentDate,
            head.PartyGstin!,
            head.PlaceOfSupplyStateCode,
            head.IsReverseCharge,
            value,
            rateLines);
    }

    private static List<Gstr1HsnSummaryRow> BuildHsnSummary(
        IReadOnlyList<InvoiceLineRow> rows) =>
        rows
            .Where(row => !string.IsNullOrWhiteSpace(row.HsnCode))
            .GroupBy(row => (row.HsnCode!, row.IsB2B))
            .Select(group => new Gstr1HsnSummaryRow(
                group.Key.Item1,
                group.Select(row => row.HsnDescription).FirstOrDefault(description =>
                    !string.IsNullOrWhiteSpace(description)),
                group.Key.IsB2B,
                group.Sum(row => row.QuantityMinor),
                group.Sum(row => row.TaxableValueMinor),
                group.Sum(row => row.IgstMinor),
                group.Sum(row => row.CgstMinor),
                group.Sum(row => row.SgstMinor),
                group.Sum(row => row.CessMinor)))
            .OrderBy(row => row.Code, StringComparer.Ordinal)
            .ThenByDescending(row => row.IsB2B)
            .ToList();

    private static GstTotals ToTotals(IReadOnlyList<InvoiceLineRow> rows) => new(
        rows.Sum(row => row.TaxableValueMinor),
        rows.Sum(row => row.IgstMinor),
        rows.Sum(row => row.CgstMinor),
        rows.Sum(row => row.SgstMinor),
        rows.Sum(row => row.CessMinor));

    private static GstTotals NetTax(GstTotals outward, GstTotals itc, GstTotals rcm) => new(
        outward.TaxableValueMinor - itc.TaxableValueMinor,
        outward.IgstMinor + rcm.IgstMinor - itc.IgstMinor,
        outward.CgstMinor + rcm.CgstMinor - itc.CgstMinor,
        outward.SgstMinor + rcm.SgstMinor - itc.SgstMinor,
        outward.CessMinor + rcm.CessMinor - itc.CessMinor);

    private static bool InWindow(DateOnly date, DateOnly start, DateOnly end) =>
        date >= start && date <= end;

    private static DateOnly EndOfMonth(DateOnly date)
    {
        var days = DateTime.DaysInMonth(date.Year, date.Month);
        return new DateOnly(date.Year, date.Month, days);
    }

    // -----------------------------------------------------------------------------------------
    // GSTR-2B import parsing
    // -----------------------------------------------------------------------------------------

    private static string MatchKey(string? gstin, string? invoiceNumber) =>
        FormattableString.Invariant(
            $"{(gstin ?? string.Empty).Trim().ToUpperInvariant()}|{(invoiceNumber ?? string.Empty).Trim().ToUpperInvariant()}");

    /// <summary>
    /// The supplier's own invoice number as recorded on the books — keyed on the party-reference
    /// field of the purchase invoice where the operator enters it, falling back to our own number.
    /// </summary>
    private static string SupplierInvoiceNumber(InvoiceLineRow row) =>
        string.IsNullOrWhiteSpace(row.PartyReferenceNumber) ? row.DocumentNumber : row.PartyReferenceNumber;

    private static List<string> CompareValues(
        PortalInvoice portal,
        long booksValueMinor,
        long toleranceMinor)
    {
        var details = new List<string>();

        if (portal.InvoiceValueMinor.HasValue && booksValueMinor != portal.InvoiceValueMinor.Value)
        {
            var difference = Math.Abs(booksValueMinor - portal.InvoiceValueMinor.Value);

            if (difference > toleranceMinor)
            {
                details.Add(FormattableString.Invariant(
                    $"Invoice value differs by ₹{MoneyScale.FromMinor(difference):0.00}: books ₹{MoneyScale.FromMinor(booksValueMinor):0.00} vs portal ₹{MoneyScale.FromMinor(portal.InvoiceValueMinor.Value):0.00}."));
            }
        }

        return details;
    }

    private static List<PortalInvoice> ParsePortalInvoices(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var invoices = new List<PortalInvoice>();

        if (root.TryGetProperty("b2b", out var b2bSection) && b2bSection.ValueKind == JsonValueKind.Array)
        {
            foreach (var buyer in b2bSection.EnumerateArray())
            {
                var gstin = buyer.TryGetProperty("ctin", out var ctinElement)
                    ? ctinElement.GetString()
                    : null;

                if (!buyer.TryGetProperty("inv", out var invSection)
                    || invSection.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var invoice in invSection.EnumerateArray())
                {
                    invoices.Add(ParsePortalInvoice(gstin, invoice));
                }
            }
        }

        return invoices;
    }

    private static PortalInvoice ParsePortalInvoice(string? gstin, JsonElement invoice)
    {
        var number = invoice.TryGetProperty("inum", out var inumElement)
            ? inumElement.GetString() ?? string.Empty
            : string.Empty;

        DateOnly? date = null;
        long? value = null;

        if (invoice.TryGetProperty("idt", out var idtElement) && idtElement.ValueKind == JsonValueKind.String)
        {
            date = ParsePortalDate(idtElement.GetString());
        }

        if (invoice.TryGetProperty("val", out var valElement)
            && (valElement.ValueKind == JsonValueKind.Number))
        {
            value = MoneyScale.ToMinor(MoneyScale.Round(valElement.GetDecimal()));
        }

        return new PortalInvoice(gstin, number, date, value);
    }

    /// <summary>The portal renders dates "dd-mm-yyyy"; ISO "yyyy-MM-dd" accepted for robustness.</summary>
    private static DateOnly? ParsePortalDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateOnly.TryParseExact(
                text.Trim(),
                ["dd-MM-yyyy", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed record PortalInvoice(
        string? SupplierGstin,
        string InvoiceNumber,
        DateOnly? InvoiceDate,
        long? InvoiceValueMinor);

    private sealed record BooksPurchase(
        Guid Document,
        string? SupplierGstin,
        string? SupplierInvoiceNumber,
        string DocumentNumber,
        DateOnly BookDate,
        long ValueMinor);

    /// <summary>Dapper-mapped sales/purchase invoice line with its resolved header facts.</summary>
    /// <remarks>
    /// The document date travels as canonical calendar text (Dapper cannot bind
    /// <see cref="DateOnly"/>) and is parsed exactly once here, invariantly.
    /// </remarks>
    private sealed class InvoiceLineRow
    {
        public string DocumentId { get; init; } = string.Empty;

        public Guid Document => Guid.Parse(DocumentId);

        public string DocumentNumber { get; init; } = string.Empty;

        public string VoucherDate { get; init; } = string.Empty;

        public DateOnly DocumentDate => DateOnly.Parse(VoucherDate, CultureInfo.InvariantCulture);

        public string? PartyGstin { get; init; }

        public string PartyReferenceNumber { get; init; } = string.Empty;

        public string PlaceOfSupplyStateCode { get; init; } = string.Empty;

        public bool IsReverseCharge { get; init; }

        public bool IsB2B => !string.IsNullOrWhiteSpace(PartyGstin);

        public string ItemIdText { get; init; } = string.Empty;

        public string? HsnCode { get; init; }

        public string? HsnDescription { get; init; }

        public long QuantityMinor { get; init; }

        public long TaxableValueMinor { get; init; }

        public long RatePercentMinor { get; init; }

        public long IgstMinor { get; init; }

        public long CgstMinor { get; init; }

        public long SgstMinor { get; init; }

        public long CessMinor { get; init; }
    }

    private sealed class DocumentStatusCountRow
    {
        public string Status { get; init; } = string.Empty;

        public int Count { get; init; }
    }

    private sealed class InvoiceNumberRangeRow
    {
        public string Number { get; init; } = string.Empty;
    }
}
