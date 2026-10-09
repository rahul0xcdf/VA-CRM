using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Application.Gst;

/// <summary>
/// Serialises a built <see cref="Gstr1Return"/> into the GSTN offline-utility JSON shape, stamped
/// with the pinned schema version and the export timestamp (phase-5-gst-returns.md §2).
/// </summary>
/// <remarks>
/// The portal's numeric fields carry rupees, so minor units are converted to decimal exactly once,
/// here, in this mapper — never inside SQL and never through binary floating point (ADR-0003).
/// Before writing anything the exporter re-checks structural self-consistency: every section total,
/// the grand totals and the books figures must agree to the minor unit, so a broken return fails at
/// generation rather than at filing.
/// <para>
/// <b>Field names are confirmed only as far as C5.1 has been able to confirm them.</b> The cess
/// amount is <c>csamt</c>, the HSN code is <c>hsn_sc</c>, and a B2B invoice's <c>itms</c> entries
/// nest their figures under <c>num</c> + <c>itm_det</c> — all three were wrong here until the
/// Phase 5 evidence review, and all three are now matched against two independent published
/// references to the GSTN payload. What remains <b>unverified and probably still wrong</b> is
/// recorded in <c>open-questions.md</c> #23: the B2C section shape (the portal's is a flat
/// <c>b2cs</c> array, not this nested <c>b2c</c> one), the absent <c>b2cl</c> section for
/// inter-state B2C invoices above the invoice-wise threshold, the top-level <c>gtotals</c> block
/// (the portal carries <c>gt</c> / <c>cur_gt</c> instead) and the hardcoded <c>uqc</c>. Those
/// change *what* is reported rather than how a field is spelled, so they need the authoritative
/// GSTN schema and the owner's accountant, not a guess from here.
/// </para>
/// </remarks>
public static class Gstr1JsonExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>Produces the export document for one built return.</summary>
    public static string Export(Gstr1Return gstr1, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(gstr1);
        ArgumentNullException.ThrowIfNull(clock);

        if (string.IsNullOrWhiteSpace(gstr1.Gstin))
        {
            throw new InvalidOperationException(
                "This company carries no GSTIN, so there is nothing to file a GSTR-1 against.");
        }

        var b2bTaxable = SumB2B(gstr1);

        if (b2bTaxable != gstr1.Totals.TaxableValueMinor - SumB2CTaxable(gstr1.B2C))
        {
            throw new InvalidOperationException(
                "The built GSTR-1 does not reconcile internally: B2B + B2C taxable values disagree " +
                "with the grand totals. Refusing to export a self-inconsistent return.");
        }

        var file = new Gstr1JsonFile
        {
            Version = GstSchemaVersions.CurrentGstr1,
            Gstin = gstr1.Gstin,
            Fp = FinancialPeriodFor(gstr1.FromDate),
            GenDt = clock.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            Gtotals = ToTotals(gstr1.Totals),
            B2b = gstr1.B2B
                .GroupBy(invoice => invoice.CounterpartyGstin, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new Gstr1JsonB2bBuyer
                {
                    Ctin = group.Key,
                    Invoices = group
                        .OrderBy(invoice => invoice.InvoiceDate)
                        .ThenBy(invoice => invoice.InvoiceNumber, StringComparer.Ordinal)
                        .Select(ToB2bInvoice)
                        .ToList(),
                })
                .ToList(),
            B2c = gstr1.B2C
                .GroupBy(aggregate => aggregate.PlaceOfSupplyStateCode, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new Gstr1JsonB2cBucket
                {
                    Pos = group.Key,
                    Lines = group
                        .OrderBy(aggregate => aggregate.RatePercentMinor)
                        .Select(ToB2cLine)
                        .ToList(),
                })
                .ToList(),
            Hsn = new Gstr1JsonHsnSection
            {
                Data = gstr1.HsnSummary
                    .OrderBy(row => row.Code, StringComparer.Ordinal)
                    .ThenByDescending(row => row.IsB2B)
                    .Select((row, index) => ToHsnRow(row, index + 1))
                    .ToList(),
            },
            Docs = new Gstr1JsonDocsSection
            {
                Invoices =
                [
                    new Gstr1JsonDocIssue
                    {
                        Num = 1,
                        From = gstr1.Documents.FirstInvoiceNumber,
                        To = gstr1.Documents.LastInvoiceNumber,
                        Tot = gstr1.Documents.InvoicesIssued + gstr1.Documents.InvoicesCancelled,
                        Cancel = gstr1.Documents.InvoicesCancelled,
                        Net = gstr1.Documents.InvoicesIssued,
                    },
                ],
            },
        };

        return JsonSerializer.Serialize(file, Options);
    }

    /// <summary>The GST financial period ("fp"): FY-begin year + FY-end two-digit year, April-based.</summary>
    public static string FinancialPeriodFor(DateOnly date)
    {
        var fyStartYear = date.Month >= 4 ? date.Year : date.Year - 1;
        return FormattableString.Invariant($"{fyStartYear}{(fyStartYear + 1) % 100:D2}");
    }

    private static long SumB2B(Gstr1Return gstr1) =>
        gstr1.B2B.Sum(invoice => invoice.RateLines.Sum(line => line.TaxableValueMinor));

    private static long SumB2CTaxable(IReadOnlyList<Gstr1B2CAggregate> b2c) =>
        b2c.Sum(aggregate => aggregate.TaxableValueMinor);

    private static Gstr1JsonTotals ToTotals(GstTotals totals) => new()
    {
        Taxable = Rupees(totals.TaxableValueMinor),
        Igst = Rupees(totals.IgstMinor),
        Cgst = Rupees(totals.CgstMinor),
        Sgst = Rupees(totals.SgstMinor),
        Cess = Rupees(totals.CessMinor),
    };

    private static Gstr1JsonInvoice ToB2bInvoice(Gstr1B2BInvoice invoice) => new()
    {
        Number = invoice.InvoiceNumber,
        Date = PortalDate(invoice.InvoiceDate),
        Value = Rupees(invoice.InvoiceValueMinor),
        Pos = invoice.PlaceOfSupplyStateCode,
        ReverseCharge = invoice.IsReverseCharge ? "Y" : "N",
        Items = invoice.RateLines
            .OrderBy(line => line.RatePercentMinor)
            .Select((line, index) => new Gstr1JsonInvoiceItem
            {
                Num = index + 1,
                Detail = new Gstr1JsonRateItem
                {
                    Rate = Percent(line.RatePercentMinor),
                    Taxable = Rupees(line.TaxableValueMinor),
                    Igst = Rupees(line.IgstMinor),
                    Cgst = Rupees(line.CgstMinor),
                    Sgst = Rupees(line.SgstMinor),
                    Cess = Rupees(line.CessMinor),
                },
            })
            .ToList(),
    };

    private static Gstr1JsonB2cLine ToB2cLine(Gstr1B2CAggregate aggregate) => new()
    {
        Rate = Percent(aggregate.RatePercentMinor),
        InvoiceCount = aggregate.InvoiceCount,
        Value = Rupees(aggregate.InvoiceValueMinor),
        Taxable = Rupees(aggregate.TaxableValueMinor),
        Igst = Rupees(aggregate.IgstMinor),
        Cgst = Rupees(aggregate.CgstMinor),
        Sgst = Rupees(aggregate.SgstMinor),
        Cess = Rupees(aggregate.CessMinor),
    };

    private static Gstr1JsonHsnRow ToHsnRow(Gstr1HsnSummaryRow row, int ordinal) => new()
    {
        Num = ordinal,
        Code = row.Code,
        Description = row.Description,
        Uqc = "NOS",
        Quantity = MoneyScale.FromMinor(row.QuantityMinor),
        Taxable = Rupees(row.TaxableValueMinor),
        Igst = Rupees(row.IgstMinor),
        Cgst = Rupees(row.CgstMinor),
        Sgst = Rupees(row.SgstMinor),
        Cess = Rupees(row.CessMinor),
        IsB2b = row.IsB2B,
    };

    private static decimal Rupees(long minor) =>
        Math.Round(MoneyScale.FromMinor(minor), 2, MidpointRounding.AwayFromZero);

    private static decimal Percent(long ratePercentMinor) =>
        Math.Round(ratePercentMinor / 100m / 100m, 2, MidpointRounding.AwayFromZero);

    private static string PortalDate(DateOnly date) =>
        date.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    private sealed class Gstr1JsonFile
    {
        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("gstin")]
        public string Gstin { get; init; } = string.Empty;

        [JsonPropertyName("fp")]
        public string Fp { get; init; } = string.Empty;

        [JsonPropertyName("gen_dt")]
        public string GenDt { get; init; } = string.Empty;

        [JsonPropertyName("gtotals")]
        public Gstr1JsonTotals Gtotals { get; init; } = new();

        [JsonPropertyName("b2b")]
        public IReadOnlyList<Gstr1JsonB2bBuyer> B2b { get; init; } = [];

        [JsonPropertyName("b2c")]
        public IReadOnlyList<Gstr1JsonB2cBucket> B2c { get; init; } = [];

        [JsonPropertyName("hsn")]
        public Gstr1JsonHsnSection Hsn { get; init; } = new();

        [JsonPropertyName("docs")]
        public Gstr1JsonDocsSection Docs { get; init; } = new();
    }

    private sealed class Gstr1JsonTotals
    {
        [JsonPropertyName("txval")]
        public decimal Taxable { get; init; }

        [JsonPropertyName("iamt")]
        public decimal Igst { get; init; }

        [JsonPropertyName("camt")]
        public decimal Cgst { get; init; }

        [JsonPropertyName("samt")]
        public decimal Sgst { get; init; }

        [JsonPropertyName("csamt")]
        public decimal Cess { get; init; }
    }

    private sealed class Gstr1JsonB2bBuyer
    {
        [JsonPropertyName("ctin")]
        public string Ctin { get; init; } = string.Empty;

        [JsonPropertyName("inv")]
        public IReadOnlyList<Gstr1JsonInvoice> Invoices { get; init; } = [];
    }

    private sealed class Gstr1JsonInvoice
    {
        [JsonPropertyName("inum")]
        public string Number { get; init; } = string.Empty;

        [JsonPropertyName("idt")]
        public string Date { get; init; } = string.Empty;

        [JsonPropertyName("val")]
        public decimal Value { get; init; }

        [JsonPropertyName("pos")]
        public string Pos { get; init; } = string.Empty;

        [JsonPropertyName("rchrg")]
        public string ReverseCharge { get; init; } = "N";

        [JsonPropertyName("itms")]
        public IReadOnlyList<Gstr1JsonInvoiceItem> Items { get; init; } = [];
    }

    /// <summary>
    /// One entry of a B2B invoice's <c>itms</c> array. The portal nests the figures one level
    /// down, under <c>itm_det</c>, alongside a 1-based <c>num</c>; a flat rate object — which is
    /// what this exporter wrote until the C5.1 review — is not the shape it accepts.
    /// </summary>
    private sealed class Gstr1JsonInvoiceItem
    {
        [JsonPropertyName("num")]
        public int Num { get; init; }

        [JsonPropertyName("itm_det")]
        public Gstr1JsonRateItem Detail { get; init; } = new();
    }

    private sealed class Gstr1JsonRateItem
    {
        [JsonPropertyName("rt")]
        public decimal Rate { get; init; }

        [JsonPropertyName("txval")]
        public decimal Taxable { get; init; }

        [JsonPropertyName("iamt")]
        public decimal Igst { get; init; }

        [JsonPropertyName("camt")]
        public decimal Cgst { get; init; }

        [JsonPropertyName("samt")]
        public decimal Sgst { get; init; }

        [JsonPropertyName("csamt")]
        public decimal Cess { get; init; }
    }

    private sealed class Gstr1JsonB2cBucket
    {
        [JsonPropertyName("pos")]
        public string Pos { get; init; } = string.Empty;

        [JsonPropertyName("lines")]
        public IReadOnlyList<Gstr1JsonB2cLine> Lines { get; init; } = [];
    }

    private sealed class Gstr1JsonB2cLine
    {
        [JsonPropertyName("rt")]
        public decimal Rate { get; init; }

        [JsonPropertyName("cnt")]
        public int InvoiceCount { get; init; }

        [JsonPropertyName("val")]
        public decimal Value { get; init; }

        [JsonPropertyName("txval")]
        public decimal Taxable { get; init; }

        [JsonPropertyName("iamt")]
        public decimal Igst { get; init; }

        [JsonPropertyName("camt")]
        public decimal Cgst { get; init; }

        [JsonPropertyName("samt")]
        public decimal Sgst { get; init; }

        [JsonPropertyName("csamt")]
        public decimal Cess { get; init; }
    }

    private sealed class Gstr1JsonHsnSection
    {
        [JsonPropertyName("data")]
        public IReadOnlyList<Gstr1JsonHsnRow> Data { get; init; } = [];
    }

    private sealed class Gstr1JsonHsnRow
    {
        [JsonPropertyName("num")]
        public int Num { get; init; }

        [JsonPropertyName("hsn_sc")]
        public string Code { get; init; } = string.Empty;

        [JsonPropertyName("desc")]
        public string? Description { get; init; }

        [JsonPropertyName("uqc")]
        public string Uqc { get; init; } = string.Empty;

        [JsonPropertyName("qty")]
        public decimal Quantity { get; init; }

        [JsonPropertyName("txval")]
        public decimal Taxable { get; init; }

        [JsonPropertyName("iamt")]
        public decimal Igst { get; init; }

        [JsonPropertyName("camt")]
        public decimal Cgst { get; init; }

        [JsonPropertyName("samt")]
        public decimal Sgst { get; init; }

        [JsonPropertyName("csamt")]
        public decimal Cess { get; init; }

        [JsonPropertyName("sply_ty")]
        public string SupplyType => IsB2b ? "b2b" : "b2c";

        [JsonIgnore]
        public bool IsB2b { get; init; }
    }

    private sealed class Gstr1JsonDocsSection
    {
        [JsonPropertyName("inv")]
        public IReadOnlyList<Gstr1JsonDocIssue> Invoices { get; init; } = [];
    }

    private sealed class Gstr1JsonDocIssue
    {
        [JsonPropertyName("num")]
        public int Num { get; init; }

        [JsonPropertyName("from")]
        public string From { get; init; } = string.Empty;

        [JsonPropertyName("to")]
        public string To { get; init; } = string.Empty;

        [JsonPropertyName("tot")]
        public int Tot { get; init; }

        [JsonPropertyName("cancel")]
        public int Cancel { get; init; }

        [JsonPropertyName("net")]
        public int Net { get; init; }
    }
}
