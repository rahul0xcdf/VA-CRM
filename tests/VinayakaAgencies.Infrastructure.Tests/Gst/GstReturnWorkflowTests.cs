using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Customers;
using VinayakaAgencies.Application.Gst;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Trade;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Customers;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Gst;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Domain.Trade;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;
using Shouldly;

namespace VinayakaAgencies.Infrastructure.Tests.Gst;

/// <summary>
/// The Phase 5 vertical slice against a real SQLite file: posted snapshots driving GSTR-1/3B/9 that
/// reconcile to each other and to the books (C5.3), the pinned-schema JSON export (C5.1) and the
/// GSTR-2B reconciliation classes (C5.4) — phase-5-gst-returns.md §2–§3.
/// </summary>
public sealed class GstReturnWorkflowTests
{
    private static readonly DateOnly PeriodStart = new(2026, 8, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 8, 31);
    private static readonly DateOnly InPeriod = new(2026, 8, 20);

    /// <summary>FY 2026-27, the year the GSTR-9 tests file for (open-questions.md #9: 1 April).</summary>
    private static readonly DateOnly FyStart = new(2026, 4, 1);
    private static readonly DateOnly FyEnd = new(2027, 3, 31);

    // ---------------------------------------------------------------- C5.3 — returns reconcile

    [Fact]
    public async Task BuildGstr3B_MatchesGstr1MatchesPostedSnapshots()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        await PostSalesInvoicesAsync(host, world);
        await PostPurchaseInvoiceAsync(host, world, "SP-2201", 200m);
        await PostPurchaseInvoiceAsync(host, world, "SP-2202", 100m);

        var gstr1 = await ReturnAsync(host, service => service.BuildGstr1Async(PeriodStart, PeriodEnd));
        var gstr3b = await ReturnAsync(host, service => service.BuildGstr3BAsync(PeriodStart, PeriodEnd));

        // 3B outward block equals 1's totals exactly — same snapshots, never recomputed.
        gstr3b.ReconcilesTo(gstr1).ShouldBeTrue();

        // …and both equal the sum of what was stamped onto the posted sales documents.
        var snapshot = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var documents = await unitOfWork.Repository<TradeDocument>().ListAsync();
            var salesIds = documents
                .Where(document => document.Kind == TradeDocumentKind.TaxInvoice
                    && document.Status == TradeDocumentStatus.Issued)
                .Select(document => document.Id)
                .ToHashSet();

            var lines = (await unitOfWork.Repository<TradeDocumentLine>().ListAsync())
                .Where(line => salesIds.Contains(line.TradeDocumentId))
                .ToList();

            return (
                TaxableMinor: lines.Sum(line => line.TaxableValueMinor),
                IgstMinor: lines.Sum(line => line.IgstMinor),
                CgstMinor: lines.Sum(line => line.CgstMinor),
                SgstMinor: lines.Sum(line => line.SgstMinor));
        });

        snapshot.TaxableMinor.ShouldBe(15_000_000L);   // ₹1,000 B2B + ₹500 B2C
        snapshot.IgstMinor.ShouldBe(1_800_000L);       // B2B to Maharashtra at 18%
        snapshot.CgstMinor.ShouldBe(450_000L);         // B2C intra-state halves of ₹90
        snapshot.SgstMinor.ShouldBe(450_000L);

        gstr1.Totals.TaxableValueMinor.ShouldBe(snapshot.TaxableMinor);
        gstr1.Totals.IgstMinor.ShouldBe(snapshot.IgstMinor);
        gstr1.Totals.CgstMinor.ShouldBe(snapshot.CgstMinor);
        gstr1.Totals.SgstMinor.ShouldBe(snapshot.SgstMinor);

        // B2B/B2C classification followed the counterparty GSTIN, and Table 12 is split.
        gstr1.B2B.Single().CounterpartyGstin.ShouldBe("27AAPFU0939F1ZV");
        gstr1.B2B.Single().InvoiceValueMinor.ShouldBe(11_800_000L);
        gstr1.B2C.Single().PlaceOfSupplyStateCode.ShouldBe("29");
        gstr1.HsnSummary.Count(row => row.IsB2B).ShouldBeGreaterThan(0);
        gstr1.HsnSummary.Count(row => !row.IsB2B).ShouldBeGreaterThan(0);

        // ITC from the two non-reverse-charge purchases; nothing reverse-charge in this run.
        gstr3b.ItcClaimed.IgstMinor.ShouldBe(540_000L);
        gstr3b.InwardReverseChargeSupplies.TotalTaxMinor.ShouldBe(0L);
    }

    // ---------------------------------------------------------------- C5.1 — pinned JSON export

    [Fact]
    public async Task ExportGstr1_StampsPinnedSchemaVersion_AndIsSelfConsistent()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        await PostSalesInvoicesAsync(host, world);

        var json = await ReturnAsync(host, service => service.ExportGstr1JsonAsync(PeriodStart, PeriodEnd));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // The stamp spec §2 demands: which schema wrote this file, and when.
        root.GetProperty("version").GetString().ShouldBe(GstSchemaVersions.CurrentGstr1);
        root.GetProperty("gen_dt").GetString().ShouldNotBeNullOrWhiteSpace();
        root.GetProperty("gstin").GetString().ShouldBe(world.CompanyGstin);
        root.GetProperty("fp").GetString().ShouldBe("202627");

        // Grand totals equal the books: ₹1,500 taxable, ₹180 IGST + ₹45 + ₹45 CGST/SGST.
        var totals = root.GetProperty("gtotals");
        totals.GetProperty("txval").GetDecimal().ShouldBe(1500m);
        totals.GetProperty("iamt").GetDecimal().ShouldBe(180m);
        totals.GetProperty("camt").GetDecimal().ShouldBe(45m);
        totals.GetProperty("samt").GetDecimal().ShouldBe(45m);

        // One B2B buyer with one invoice carrying its rate line; B2C aggregated by POS + rate.
        var b2b = root.GetProperty("b2b");
        b2b.GetArrayLength().ShouldBe(1);
        var invoice = b2b[0].GetProperty("inv")[0];
        invoice.GetProperty("inum").GetString().ShouldBe("INV/0001");
        invoice.GetProperty("rchrg").GetString().ShouldBe("N");

        // The portal nests an invoice's figures under num + itm_det rather than flat on the itms
        // entry. This assertion read `itms[0].rt` until the C5.1 review found the shape was wrong.
        var item = invoice.GetProperty("itms")[0];
        item.GetProperty("num").GetInt32().ShouldBe(1);
        item.GetProperty("itm_det").GetProperty("rt").GetDecimal().ShouldBe(18m);
        item.GetProperty("itm_det").GetProperty("csamt").GetDecimal().ShouldBe(0m);

        root.GetProperty("b2c")[0].GetProperty("pos").GetString().ShouldBe("29");

        // Table 12 present for both supply types; documents-issued accounts for both invoices.
        var hsnRows = root.GetProperty("hsn").GetProperty("data");
        hsnRows.GetArrayLength().ShouldBe(2);

        var docs = root.GetProperty("docs").GetProperty("inv")[0];
        docs.GetProperty("tot").GetInt32().ShouldBe(2);
        docs.GetProperty("cancel").GetInt32().ShouldBe(0);
        docs.GetProperty("net").GetInt32().ShouldBe(2);
    }

    // ---------------------------------------------------------------- C5.4 — GSTR-2B matching

    [Fact]
    public async Task ReconcileGstr2B_ClassifiesMatchedMissingInBooksAndMissingInPortal()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        await PostPurchaseInvoiceAsync(host, world, "SP-2201", 200m);
        await PostPurchaseInvoiceAsync(host, world, "SP-2202", 100m);

        // Portal file: SP-2201 exists (with a value difference beyond tolerance), SP-9999 does not
        // exist in the books at all; the books' SP-2202 never reached the portal.
        const string portalJson = """
            {
              "gstin": "29ABCDE1234F1Z5",
              "fp": "202627",
              "b2b": [
                {
                  "ctin": "24AACCE9487K1ZP",
                  "inv": [
                    { "inum": "SP-2201", "idt": "19-08-2026", "val": 999.00, "pos": "24",
                      "itms": [ { "rt": 18, "txval": 846.61, "iamt": 152.39 } ] },
                    { "inum": "SP-9999", "idt": "21-08-2026", "val": 118.00, "pos": "24",
                      "itms": [ { "rt": 18, "txval": 100.00, "iamt": 18.00 } ] }
                  ]
                }
              ]
            }
            """;

        var result = await ReturnAsync(host, service => service.ReconcileGstr2BAsync(
            portalJson, PeriodStart, PeriodEnd));

        result.MatchedCount.ShouldBe(1);
        result.MissingInBooksCount.ShouldBe(1);
        result.MissingInPortalCount.ShouldBe(1);

        var matched = result.Results.Single(row => row.Classification == Gstr2bClassification.Matched);
        matched.InvoiceNumber.ShouldBe("SP-2201");
        matched.BooksInvoiceNumber.ShouldBe("BILL/0001");
        matched.PortalInvoiceValueMinor.ShouldBe(9_990_000L);
        matched.MismatchDetails.ShouldNotBeEmpty();

        result.Results.Single(row => row.Classification == Gstr2bClassification.MissingInBooks)
            .InvoiceNumber.ShouldBe("SP-9999");

        var missingInPortal =
            result.Results.Single(row => row.Classification == Gstr2bClassification.MissingInPortal);
        missingInPortal.InvoiceNumber.ShouldBe("SP-2202");
        missingInPortal.BooksInvoiceValueMinor.ShouldBe(1_180_000L);
    }

    // ---------------------------------------------------------------- arrange

    private sealed record GstWorld(
        Guid CompanyId,
        string CompanyGstin,
        Guid MainGodownId,
        Guid CustomerLedgerId,
        Guid SupplierLedgerId,
        Guid ItemId);

    /// <summary>Runs one return operation in its own DI scope, as the shell would.</summary>
    [Fact]
    public async Task ExportGstr1_FromPostedInvoices_ValidatesAgainstTheAsBuiltSchema()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        await PostSalesInvoicesAsync(host, world);

        var json = await ReturnAsync(host, service => service.ExportGstr1JsonAsync(PeriodStart, PeriodEnd));

        // The half of C5.1 that does not need the authoritative GSTN schema: every field a real
        // export writes is checked against the pinned shape by a JSON Schema evaluator, including
        // the ones the test above does not happen to read. The schema asserts
        // additionalProperties:false throughout, so a field added or renamed in the exporter
        // without a matching schema change fails here. See Gstr1SchemaValidationTests for what the
        // schema is and is not, and open-questions.md #23 for what still blocks C5.1.
        var results = Gstr1Schema.Evaluate(
            Gstr1Schema.AsBuiltFileName,
            JsonNode.Parse(json)!);

        results.IsValid.ShouldBeTrue(Gstr1Schema.Explain(results));
    }

    // ---------------------------------------------------------------- GSTR-9 — the annual return

    [Fact]
    public async Task BuildGstr9_OverAFinancialYear_ReconcilesToItsMonthlyGstr1sAndTheBooks()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        await ReceiveOpeningStockAsync(host, world, FyStart, quantity: 30m);

        // One invoice in the first month of the year, one mid-year and one in the last month, so
        // an off-by-one at either end of the month loop cannot hide behind the middle.
        await PostDatedSalesInvoiceAsync(host, world, new DateOnly(2026, 4, 20), 500m);
        await PostDatedSalesInvoiceAsync(host, world, new DateOnly(2026, 7, 15), 700m);
        await PostDatedSalesInvoiceAsync(host, world, new DateOnly(2027, 3, 10), 900m);

        var gstr9 = await ReturnAsync(host, service => service.BuildGstr9Async(FyStart, FyEnd));

        gstr9.Months.Count.ShouldBe(12, "a financial year has twelve months");
        gstr9.Gstin.ShouldBe(world.CompanyGstin);
        gstr9.Months[0].Month.ShouldBe(4);
        gstr9.Months[0].Year.ShouldBe(2026);
        gstr9.Months[^1].Month.ShouldBe(3);
        gstr9.Months[^1].Year.ShouldBe(2027);

        // ₹2,100 taxable at 18%, all IGST — the B2B customer bills into Maharashtra.
        gstr9.OutwardTaxableSupplies.TaxableValueMinor.ShouldBe(MoneyScale.ToMinor(2_100m));
        gstr9.OutwardTaxableSupplies.IgstMinor.ShouldBe(MoneyScale.ToMinor(378m));
        gstr9.OutwardTaxableSupplies.CgstMinor.ShouldBe(0L);
        gstr9.OutwardTaxableSupplies.SgstMinor.ShouldBe(0L);

        // Each invoice lands in its own month and nowhere else.
        MonthRow(gstr9, 2026, 4).OutwardTaxableSupplies.TaxableValueMinor.ShouldBe(MoneyScale.ToMinor(500m));
        MonthRow(gstr9, 2026, 7).OutwardTaxableSupplies.TaxableValueMinor.ShouldBe(MoneyScale.ToMinor(700m));
        MonthRow(gstr9, 2027, 3).OutwardTaxableSupplies.TaxableValueMinor.ShouldBe(MoneyScale.ToMinor(900m));
        MonthRow(gstr9, 2026, 5).OutwardTaxableSupplies.TaxableValueMinor.ShouldBe(0L);

        // The annual figure equals the sum of its own twelve monthly rows. This is the assertion
        // that catches a month window which does not tile the year: a day that falls into no
        // month's window is dropped from the rows and therefore from the annual total too.
        SumOf(gstr9.Months.Select(row => row.OutwardTaxableSupplies))
            .TaxableValueMinor.ShouldBe(gstr9.OutwardTaxableSupplies.TaxableValueMinor);

        // …and equals the sum of the twelve monthly GSTR-1s, built independently. Two of the three
        // agreeing proves nothing about the third — the lesson C5.3 is built on.
        var monthlyTaxable = 0L;
        var monthlyIgst = 0L;

        for (var month = FyStart; month <= FyEnd; month = month.AddMonths(1))
        {
            var monthEnd = new DateOnly(
                month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));
            var monthly = await ReturnAsync(host, service => service.BuildGstr1Async(month, monthEnd));

            monthlyTaxable += monthly.Totals.TaxableValueMinor;
            monthlyIgst += monthly.Totals.IgstMinor;
        }

        monthlyTaxable.ShouldBe(gstr9.OutwardTaxableSupplies.TaxableValueMinor);
        monthlyIgst.ShouldBe(gstr9.OutwardTaxableSupplies.IgstMinor);

        // …and the books, read from the snapshots stamped onto the posted documents at posting
        // time rather than recomputed here.
        var booked = await PostedSalesSnapshotAsync(host);

        booked.TaxableMinor.ShouldBe(gstr9.OutwardTaxableSupplies.TaxableValueMinor);
        booked.IgstMinor.ShouldBe(gstr9.OutwardTaxableSupplies.IgstMinor);
    }

    [Fact]
    public async Task BuildGstr9_ForAPeriodNotStartingOnTheFirstOfAMonth_LosesNoInvoice()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        await ReceiveOpeningStockAsync(host, world, new DateOnly(2026, 4, 1), quantity: 30m);

        // 5 May falls in the first fortnight of the second month. Stepping the window forward by
        // whole months from a start date of 15 April puts the second window at 15 May – 31 May,
        // so this invoice belonged to no window at all and vanished from the annual return.
        await PostDatedSalesInvoiceAsync(host, world, new DateOnly(2026, 4, 20), 500m);
        await PostDatedSalesInvoiceAsync(host, world, new DateOnly(2026, 5, 5), 700m);

        var gstr9 = await ReturnAsync(host, service => service.BuildGstr9Async(
            new DateOnly(2026, 4, 15), new DateOnly(2026, 5, 31)));

        gstr9.OutwardTaxableSupplies.TaxableValueMinor.ShouldBe(
            MoneyScale.ToMinor(1_200m),
            "both invoices fall inside the requested period");

        SumOf(gstr9.Months.Select(row => row.OutwardTaxableSupplies))
            .TaxableValueMinor.ShouldBe(gstr9.OutwardTaxableSupplies.TaxableValueMinor);

        MonthRow(gstr9, 2026, 5).OutwardTaxableSupplies.TaxableValueMinor
            .ShouldBe(MoneyScale.ToMinor(700m));
    }

    private static Gstr9MonthRow MonthRow(Gstr9AnnualSummary summary, int year, int month) =>
        summary.Months.Single(row => row.Year == year && row.Month == month);

    private static GstTotals SumOf(IEnumerable<GstTotals> totals) =>
        totals.Aggregate(new GstTotals(0, 0, 0, 0, 0), (current, next) => current + next);

    private static Task<T> ReturnAsync<T>(SqliteTestHost host, Func<IGstReturnService, Task<T>> action) =>
        host.InScopeAsync(sp => action(sp.GetRequiredService<IGstReturnService>()));

    private static async Task<GstWorld> ArrangeAsync(SqliteTestHost host)
    {
        var companyId = await AddFullyProvisionedCompanyAsync(host);

        return await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var chartOfAccounts = sp.GetRequiredService<IChartOfAccountsService>();
            var hsnSacService = sp.GetRequiredService<IHsnSacService>();
            var rateService = sp.GetRequiredService<IGstRateService>();

            var company = await unitOfWork.Repository<Company>().GetByIdAsync(companyId);
            company!.SetGstin("29ABCDE1234F1Z5");
            unitOfWork.Repository<Company>().Update(company);
            await unitOfWork.SaveChangesAsync();

            var godown = (await unitOfWork.Repository<Godown>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId);

            var groups = await unitOfWork.Repository<AccountGroup>().ListAsync();
            var debtorsGroup = groups.Single(group => group.CompanyId == companyId && group.Name == "Sundry Debtors");
            var creditorsGroup = groups.Single(group => group.CompanyId == companyId && group.Name == "Sundry Creditors");

            var customerLedger = await chartOfAccounts.CreateLedgerAsync(
                new CreateLedgerRequest("Ram Motors", debtorsGroup.Id));
            var supplierLedger = await chartOfAccounts.CreateLedgerAsync(
                new CreateLedgerRequest("Cisno Pumps Ltd", creditorsGroup.Id));

            // A registered B2B customer billing into Maharashtra; everything else stays B2C.
            var category = (await unitOfWork.Repository<CustomerCategory>().ListAsync())
                .First(candidate => candidate.CompanyId == companyId);
            await unitOfWork.Repository<Customer>().AddAsync(new Customer(
                companyId,
                "Ram Motors",
                customerLedger.Id,
                category.Id,
                gstin: "27AAPFU0939F1ZV",
                placeOfSupplyStateCode: "27"));
            await unitOfWork.SaveChangesAsync();

            var savedCode = await hsnSacService.SaveAsync(
                new SaveHsnSacRequest(Id: null, Code: "84137010", Description: "Centrifugal pumps"));
            await rateService.AddAsync(new SaveGstRateScheduleRequest(
                savedCode.Id, 180_000L, EffectiveFrom: new DateOnly(2026, 4, 1)));

            var unit = (await unitOfWork.Repository<UnitOfMeasure>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId && candidate.Code == "NOS");
            var item = new Item(companyId, "Openwell Pump", unit.Id);
            item.AssignHsnSac(savedCode.Id);
            await unitOfWork.Repository<Item>().AddAsync(item);
            await unitOfWork.SaveChangesAsync();

            return new GstWorld(
                companyId,
                "29ABCDE1234F1Z5",
                godown.Id,
                customerLedger.Id,
                supplierLedger.Id,
                item.Id);
        });
    }

    private static async Task<Guid> AddFullyProvisionedCompanyAsync(SqliteTestHost host)
    {
        var companyId = await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var company = new Company("Vinayaka Agencies", "Vinayaka Agencies (Regd.)", "29");
            await unitOfWork.Repository<Company>().AddAsync(company);
            await unitOfWork.SaveChangesAsync();
            return company.Id;
        });

        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().ProvisionAsync(companyId));
        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().EnsureInventorySeedAsync(companyId));
        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().EnsurePipelineSeedAsync(companyId));
        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().EnsureCustomerSeedAsync(companyId));
        await host.InScopeAsync(sp =>
            sp.GetRequiredService<ICompanyProvisioningService>().EnsureGstSeedAsync(companyId));

        return companyId;
    }

    private static Task PostSalesInvoicesAsync(SqliteTestHost host, GstWorld world) =>
        host.InScopeAsync(async sp =>
        {
            var trade = sp.GetRequiredService<ITradeDocumentService>();
            var stock = sp.GetRequiredService<IStockVoucherService>();

            await stock.PostAsync(new PostStockVoucherRequest(
                StockVoucherPurpose.Receipt,
                PeriodStart,
                [
                    new StockVoucherLineRequest(
                        StockMovementDirection.Increase,
                        world.ItemId,
                        world.MainGodownId,
                        10m,
                        UnitCost: 100m),
                ],
                Narration: "opening stock",
                CounterLedgerId: world.SupplierLedgerId));

            // B2B: ₹1,000 taxable into Maharashtra at 18% → ₹1,180 billed, all whole rupees.
            var b2bDraft = await trade.CreateAsync(new SaveTradeDocumentRequest(
                TradeDocumentKind.TaxInvoice,
                InPeriod,
                world.CustomerLedgerId,
                [new TradeDocumentLineInput(world.ItemId, world.MainGodownId, 2m, 500m)]));
            await trade.PostInvoiceAsync(b2bDraft.Id);

            // B2C: ₹500 taxable intra-state → CGST ₹45 + SGST ₹45.
            var b2cDraft = await trade.CreateAsync(new SaveTradeDocumentRequest(
                TradeDocumentKind.TaxInvoice,
                InPeriod,
                world.SupplierLedgerId,
                [new TradeDocumentLineInput(world.ItemId, world.MainGodownId, 1m, 500m)]));
            await trade.PostInvoiceAsync(b2cDraft.Id);
        });

    private static Task ReceiveOpeningStockAsync(
        SqliteTestHost host,
        GstWorld world,
        DateOnly date,
        decimal quantity) =>
        host.InScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IStockVoucherService>()
                .PostAsync(new PostStockVoucherRequest(
                    StockVoucherPurpose.Receipt,
                    date,
                    [
                        new StockVoucherLineRequest(
                            StockMovementDirection.Increase,
                            world.ItemId,
                            world.MainGodownId,
                            quantity,
                            UnitCost: 100m),
                    ],
                    Narration: "opening stock",
                    CounterLedgerId: world.SupplierLedgerId));
        });

    /// <summary>One B2B tax invoice on a given date — the GSTR-9 tests need several months.</summary>
    private static Task PostDatedSalesInvoiceAsync(
        SqliteTestHost host,
        GstWorld world,
        DateOnly date,
        decimal rate) =>
        host.InScopeAsync(async sp =>
        {
            var trade = sp.GetRequiredService<ITradeDocumentService>();

            var draft = await trade.CreateAsync(new SaveTradeDocumentRequest(
                TradeDocumentKind.TaxInvoice,
                date,
                world.CustomerLedgerId,
                [new TradeDocumentLineInput(world.ItemId, world.MainGodownId, 1m, rate)]));

            await trade.PostInvoiceAsync(draft.Id);
        });

    /// <summary>What the posted sales documents carry, snapshot-stamped at posting time.</summary>
    private static Task<(long TaxableMinor, long IgstMinor, long CgstMinor, long SgstMinor)>
        PostedSalesSnapshotAsync(SqliteTestHost host) =>
        host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();

            var salesIds = (await unitOfWork.Repository<TradeDocument>().ListAsync())
                .Where(document => document.Kind == TradeDocumentKind.TaxInvoice
                    && document.Status == TradeDocumentStatus.Issued)
                .Select(document => document.Id)
                .ToHashSet();

            var lines = (await unitOfWork.Repository<TradeDocumentLine>().ListAsync())
                .Where(line => salesIds.Contains(line.TradeDocumentId))
                .ToList();

            return (
                lines.Sum(line => line.TaxableValueMinor),
                lines.Sum(line => line.IgstMinor),
                lines.Sum(line => line.CgstMinor),
                lines.Sum(line => line.SgstMinor));
        });

    private static Task PostPurchaseInvoiceAsync(
        SqliteTestHost host,
        GstWorld world,
        string supplierInvoiceNumber,
        decimal rate) =>
        host.InScopeAsync(async sp =>
        {
            var trade = sp.GetRequiredService<ITradeDocumentService>();

            var draft = await trade.CreateAsync(new SaveTradeDocumentRequest(
                TradeDocumentKind.PurchaseInvoice,
                InPeriod,
                world.SupplierLedgerId,
                [new TradeDocumentLineInput(world.ItemId, world.MainGodownId, 1m, rate)],
                PartyReferenceNumber: supplierInvoiceNumber,
                PartyGstin: "24AACCE9487K1ZP"));

            await trade.PostInvoiceAsync(draft.Id);
        });
}
