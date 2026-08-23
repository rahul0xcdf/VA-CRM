using Microsoft.Extensions.DependencyInjection;
using VinayakaAgencies.Application.Accounting;
using VinayakaAgencies.Application.Companies;
using VinayakaAgencies.Application.Inventory;
using VinayakaAgencies.Application.Reporting;
using VinayakaAgencies.Application.Trade;
using VinayakaAgencies.Domain.Abstractions;
using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;
using VinayakaAgencies.Domain.Entities;
using VinayakaAgencies.Domain.Inventory;
using VinayakaAgencies.Domain.Trade;
using VinayakaAgencies.Infrastructure.Tests.TestSupport;

namespace VinayakaAgencies.Infrastructure.Tests.Trade;

/// <summary>
/// The Phase 3 vertical slice exercised end to end against a real SQLite file: a quotation's zero
/// effect (C3.5), conversion without re-keying (C3.1), atomic invoice posting (C3.3), the
/// outstanding report's pending quantity across partial deliveries and reversals (C3.4), and the
/// purchase mirror (phase-3-sales-purchase-pipeline.md §3).
/// </summary>
/// <remarks>
/// Everything runs through the public application services over the real DI composition, each
/// operation in its own DI scope exactly as the shell would drive it. Invoices post through
/// <see cref="IVoucherPostingService"/> internally, so every posting here also proves the journal
/// voucher and the stock ledger commit together or not at all.
/// </remarks>
public sealed class TradeWorkflowTests
{
    private static readonly DateOnly InFy2026 = new(2026, 8, 20);

    // ---------------------------------------------------------------- C3.5 — quotation neutrality

    [Fact]
    public async Task Create_Quotation_LeavesTrialBalanceAndStockLedgerUntouched()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var pump = await CreateItemAsync(host, "Cisno Pump 1 HP", world);
        await ReceiveStockAsync(host, world, [(pump.Id, world.MainGodownId, 10m, 180m)]);

        var before = await TrialBalanceAsync(host);
        var stockRowsBefore = await StockLedgerCountAsync(host);

        var quotation = await TradeAsync(host, trade => trade.CreateAsync(new SaveTradeDocumentRequest(
            TradeDocumentKind.Quotation,
            InFy2026,
            world.CustomerLedgerId,
            [new TradeDocumentLineInput(pump.Id, null, 4m, 250m, DiscountPercent: 10m)],
            Narration: "monsoon enquiry",
            PartyReferenceNumber: "RM-114",
            PartyReferenceDate: new DateOnly(2026, 8, 18))));

        // Numbered like any sibling document, but nothing else about it touches the books.
        quotation.Number.ShouldBe("QTN/0001");
        quotation.Status.ShouldBe(TradeDocumentStatus.Draft);

        (await TrialBalanceAsync(host)).ShouldBe(before);
        (await StockLedgerCountAsync(host)).ShouldBe(stockRowsBefore);

        // And it is freely editable — an estimate is only ever a draft.
        var revised = await TradeAsync(host, trade => trade.UpdateDraftAsync(quotation.Id,
            new SaveTradeDocumentRequest(
                TradeDocumentKind.Quotation,
                InFy2026,
                world.CustomerLedgerId,
                [new TradeDocumentLineInput(pump.Id, null, 5m, 240m)])));

        revised.Lines.Single().Quantity.ShouldBe(5m);
        (await TrialBalanceAsync(host)).ShouldBe(before);
    }

    // ---------------------------------------------------------------- C3.1 — conversion chain

    [Fact]
    public async Task Convert_QuotationToOrderToInvoice_CarriesDataWithoutRekeying()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var pump = await CreateItemAsync(host, "Cisno Pump 1 HP", world);
        var cable = await CreateItemAsync(host, "Cable Set", world);
        await ReceiveStockAsync(
            host,
            world,
            [(pump.Id, world.MainGodownId, 10m, 180m), (cable.Id, world.MainGodownId, 50m, 25m)]);

        var quotation = await TradeAsync(host, trade => trade.CreateAsync(new SaveTradeDocumentRequest(
            TradeDocumentKind.Quotation,
            InFy2026,
            world.CustomerLedgerId,
            [
                new TradeDocumentLineInput(pump.Id, null, 2m, 250m, DiscountPercent: 5m),
                new TradeDocumentLineInput(cable.Id, null, 10m, 40m),
            ],
            Narration: "quotation for the lift works",
            PartyReferenceNumber: "RM-118")));

        var order = await TradeAsync(host, trade => trade.ConvertAsync(new ConvertTradeDocumentRequest(
            quotation.Id, TradeDocumentKind.SalesOrder, InFy2026.AddDays(1))));

        var invoiceDraft = await TradeAsync(host, trade => trade.ConvertAsync(new ConvertTradeDocumentRequest(
            order.Id, TradeDocumentKind.TaxInvoice, InFy2026.AddDays(2), DefaultGodownId: world.MainGodownId)));

        // Every line travelled verbatim — items, quantities, rates, discounts, terms — with no
        // re-keying; godowns appeared only where the target kind moves stock.
        var carriedPump = invoiceDraft.Lines.Single(line => line.ItemName == "Cisno Pump 1 HP");
        carriedPump.Quantity.ShouldBe(2m);
        carriedPump.Rate.ShouldBe(250m);
        carriedPump.DiscountPercent.ShouldBe(5m);
        carriedPump.GodownId.ShouldBe(world.MainGodownId);
        invoiceDraft.Lines.Single(line => line.ItemName == "Cable Set").Rate.ShouldBe(40m);
        invoiceDraft.Narration.ShouldBe("quotation for the lift works");
        invoiceDraft.PartyReferenceNumber.ShouldBe("RM-118");
        invoiceDraft.PartyLedgerId.ShouldBe(quotation.PartyLedgerId);

        // Provenance reads both ways off the stored links, and the fully-carried quotation closed
        // at conversion (re-read: the earlier summaries predate these events).
        order.ConvertedFromNumber.ShouldBe("QTN/0001");
        invoiceDraft.ConvertedFromNumber.ShouldBe("SO/0001");
        order.Number.ShouldBe("SO/0001");
        (await TradeAsync(host, trade => trade.GetAsync(quotation.Id)))
            !.Status.ShouldBe(TradeDocumentStatus.Converted);

        // A draft invoice carries no number: its INV/ number is claimed by the journal engine at
        // posting, so one document consumes exactly one number from its series.
        invoiceDraft.Number.ShouldBeEmpty();

        var posted = await TradeAsync(host, trade => trade.PostInvoiceAsync(invoiceDraft.Id));

        posted.DocumentNumber.ShouldBe("INV/0001");
        posted.PostedVoucherNumber.ShouldBe("INV/0001");

        // The four legs landed. Billed: 2 × ₹250 less 5% = ₹475, plus 10 × ₹40 = ₹400 → ₹875 on
        // the party against Sales. Costed FIFO: 2 × ₹180 + 10 × ₹25 = ₹610 from the control
        // account into cost of goods sold.
        var balances = await LedgerBalancesAsync(host, "Ram Motors", "Sales", "Cost of Goods Sold", "Stock-in-Hand");
        balances["Ram Motors"].ShouldBe(875m);
        balances["Sales"].ShouldBe(-875m);
        balances["Cost of Goods Sold"].ShouldBe(610m);
        balances["Stock-in-Hand"].ShouldBe(3050m - 610m);

        // The stock ledger moved exactly once, under the mirror voucher — never through the manual
        // issue path, whose movement kind would double-count the sale.
        var movements = await StockMovementsAsync(host);
        movements.Count(row => row.Kind == StockMovementKind.Sale).ShouldBe(2);
        movements.ShouldNotContain(row => row.Kind == StockMovementKind.Issue);

        var reconciliation = await host.InScopeAsync(sp => sp
            .GetRequiredService<IStockReportService>()
            .ReconcileValuationAsync(InFy2026.AddDays(40)));
        reconciliation.ControlAccountBalanceMinor.ShouldBe(reconciliation.TotalStockValueMinor);
    }

    // ---------------------------------------------------------------- C3.3 — atomic refusal

    [Fact]
    public async Task PostInvoice_SerialUnavailableAtPost_IsRefusedAndNothingPersists()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var pump = await CreateSerialItemAsync(host, "Submersible Pump", world);
        await ReceiveWithSerialsAsync(host, world, pump.Id, ["SN-A"]);

        var draft = await TradeAsync(host, trade => trade.CreateAsync(new SaveTradeDocumentRequest(
            TradeDocumentKind.TaxInvoice,
            InFy2026,
            world.CustomerLedgerId,
            [new TradeDocumentLineInput(pump.Id, world.MainGodownId, 1m, 900m, SerialNumbers: ["SN-A"])])));

        var validated = await TradeAsync(host, trade => trade.ValidateInvoiceAsync(draft.Id));
        validated.HasErrors.ShouldBeFalse();

        // Between validation and posting the unit is sold on the manual stock-voucher path — the
        // race the posting service must survive without leaving either side half-written.
        await host.InScopeAsync(sp => sp.GetRequiredService<IStockVoucherService>()
            .PostAsync(new PostStockVoucherRequest(
                StockVoucherPurpose.Issue,
                InFy2026,
                [
                    new StockVoucherLineRequest(
                        StockMovementDirection.Decrease,
                        pump.Id,
                        world.MainGodownId,
                        1m,
                        SerialNumbers: ["SN-A"]),
                ],
                Narration: "manual counter sale",
                CounterLedgerId: world.CashLedgerId)));

        var refusal = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await TradeAsync(host, trade => trade.PostInvoiceAsync(draft.Id)));

        refusal.Message.ShouldContain("cannot leave stock twice");

        // Nothing persisted: the document is still an unnumbered draft, no sale movement exists,
        // and the Tax Invoice series has not been consumed.
        var reloaded = await TradeAsync(host, trade => trade.GetAsync(draft.Id));
        reloaded!.Status.ShouldBe(TradeDocumentStatus.Draft);
        reloaded.Number.ShouldBeEmpty();
        (await StockMovementsAsync(host)).ShouldNotContain(row => row.Kind == StockMovementKind.Sale);

        // A good invoice then takes INV/0001 — the refused attempt consumed no number.
        await ReceiveWithSerialsAsync(host, world, pump.Id, ["SN-B"]);
        var trimmed = await TradeAsync(host, trade => trade.UpdateDraftAsync(draft.Id,
            new SaveTradeDocumentRequest(
                TradeDocumentKind.TaxInvoice,
                InFy2026,
                world.CustomerLedgerId,
                [new TradeDocumentLineInput(pump.Id, world.MainGodownId, 1m, 900m, SerialNumbers: ["SN-B"])])));

        var posted = await TradeAsync(host, trade => trade.PostInvoiceAsync(trimmed.Id));
        posted.DocumentNumber.ShouldBe("INV/0001");
    }

    // ---------------------------------------------------------------- C3.4 — partial delivery

    [Fact]
    public async Task Convert_PartialDelivery_KeepsPendingQuantityOnOutstandingReport()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var pump = await CreateItemAsync(host, "Openwell Pump", world);
        await ReceiveStockAsync(host, world, [(pump.Id, world.MainGodownId, 20m, 150m)]);

        var order = await TradeAsync(host, trade => trade.CreateAsync(new SaveTradeDocumentRequest(
            TradeDocumentKind.SalesOrder,
            InFy2026,
            world.CustomerLedgerId,
            [new TradeDocumentLineInput(pump.Id, null, 10m, 250m)])));

        order.Number.ShouldBe("SO/0001");
        order.Status.ShouldBe(TradeDocumentStatus.Open);

        // First delivery: convert, trim the draft to 6, post. The order stays open — closing it at
        // conversion would strand the balance the moment the draft was trimmed.
        var firstDraft = await TradeAsync(host, trade => trade.ConvertAsync(new ConvertTradeDocumentRequest(
            order.Id, TradeDocumentKind.TaxInvoice, InFy2026.AddDays(1), DefaultGodownId: world.MainGodownId)));
        var trimmed = await TradeAsync(host, trade => trade.UpdateDraftAsync(firstDraft.Id,
            new SaveTradeDocumentRequest(
                TradeDocumentKind.TaxInvoice,
                InFy2026.AddDays(1),
                world.CustomerLedgerId,
                [new TradeDocumentLineInput(pump.Id, world.MainGodownId, 6m, 250m)])));
        await TradeAsync(host, trade => trade.PostInvoiceAsync(trimmed.Id));

        var outstandingAfterFirst = await TradeAsync(host, trade => trade.SalesOrderOutstandingAsync());
        outstandingAfterFirst.Count.ShouldBe(1);
        outstandingAfterFirst.Single().PendingQuantity.ShouldBe(4m);
        outstandingAfterFirst.Single().OrderedQuantity.ShouldBe(10m);
        outstandingAfterFirst.Single().PendingValue.ShouldBe(1000m);

        var stillOpen = await TradeAsync(host, trade => trade.GetAsync(order.Id));
        stillOpen!.Status.ShouldBe(TradeDocumentStatus.Open);

        // Second delivery clears the balance and fulfils the order.
        var secondDraft = await TradeAsync(host, trade => trade.ConvertAsync(new ConvertTradeDocumentRequest(
            order.Id, TradeDocumentKind.TaxInvoice, InFy2026.AddDays(3), DefaultGodownId: world.MainGodownId)));
        var trimmedSecond = await TradeAsync(host, trade => trade.UpdateDraftAsync(secondDraft.Id,
            new SaveTradeDocumentRequest(
                TradeDocumentKind.TaxInvoice,
                InFy2026.AddDays(3),
                world.CustomerLedgerId,
                [new TradeDocumentLineInput(pump.Id, world.MainGodownId, 4m, 250m)])));
        await TradeAsync(host, trade => trade.PostInvoiceAsync(trimmedSecond.Id));

        (await TradeAsync(host, trade => trade.SalesOrderOutstandingAsync())).ShouldBeEmpty();

        var fulfilled = await TradeAsync(host, trade => trade.GetAsync(order.Id));
        fulfilled!.Status.ShouldBe(TradeDocumentStatus.Fulfilled);
    }

    [Fact]
    public async Task CancelPostedInvoice_ReopensAFulfilledOrder_RestoringItsPendingQuantity()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var pump = await CreateItemAsync(host, "Monoblock Pump", world);
        await ReceiveStockAsync(host, world, [(pump.Id, world.MainGodownId, 10m, 200m)]);

        var order = await TradeAsync(host, trade => trade.CreateAsync(new SaveTradeDocumentRequest(
            TradeDocumentKind.SalesOrder,
            InFy2026,
            world.CustomerLedgerId,
            [new TradeDocumentLineInput(pump.Id, null, 10m, 300m)])));

        async Task<Guid> DeliverAndPostAsync(int quantity, int dayOffset)
        {
            var draft = await TradeAsync(host, trade => trade.ConvertAsync(new ConvertTradeDocumentRequest(
                order.Id, TradeDocumentKind.TaxInvoice, InFy2026.AddDays(dayOffset),
                DefaultGodownId: world.MainGodownId)));
            var trimmed = await TradeAsync(host, trade => trade.UpdateDraftAsync(draft.Id,
                new SaveTradeDocumentRequest(
                    TradeDocumentKind.TaxInvoice,
                    InFy2026.AddDays(dayOffset),
                    world.CustomerLedgerId,
                    [new TradeDocumentLineInput(pump.Id, world.MainGodownId, quantity, 300m)])));

            return (await TradeAsync(host, trade => trade.PostInvoiceAsync(trimmed.Id))).DocumentId;
        }

        await DeliverAndPostAsync(6, dayOffset: 1);
        var secondDelivery = await DeliverAndPostAsync(4, dayOffset: 2);

        // Fully delivered, the order left the report.
        (await TradeAsync(host, trade => trade.SalesOrderOutstandingAsync())).ShouldBeEmpty();

        // The second delivery comes back: the order must reopen, not stay silently fulfilled.
        await TradeAsync(host, trade => trade.CancelPostedInvoiceAsync(secondDelivery, "pumps returned undelivered"));

        var reopened = await TradeAsync(host, trade => trade.GetAsync(order.Id));
        reopened!.Status.ShouldBe(TradeDocumentStatus.Open);

        var outstanding = await TradeAsync(host, trade => trade.SalesOrderOutstandingAsync());
        outstanding.Single().PendingQuantity.ShouldBe(4m);
    }

    // ---------------------------------------------------------------- purchase mirror

    [Fact]
    public async Task PurchaseChain_GoodsReceiptThenInvoice_ReceivesStockOnceAtWhatWasPaid()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var pump = await CreateItemAsync(host, "Mono Block Pump", world);

        var order = await TradeAsync(host, trade => trade.CreateAsync(new SaveTradeDocumentRequest(
            TradeDocumentKind.PurchaseOrder,
            InFy2026,
            world.SupplierLedgerId,
            [new TradeDocumentLineInput(pump.Id, null, 8m, 1200m)])));

        order.Number.ShouldBe("PO/0001");

        // The GRN records what physically arrived — data only, no stock leg yet.
        var receipt = await TradeAsync(host, trade => trade.ConvertAsync(new ConvertTradeDocumentRequest(
            order.Id, TradeDocumentKind.GoodsReceipt, InFy2026.AddDays(1), DefaultGodownId: world.MainGodownId)));

        receipt.Number.ShouldBe("GRN/0001");
        (await StockMovementsAsync(host)).ShouldBeEmpty();

        // The purchase invoice posts the one stock leg, at what the bill says was paid.
        var invoiceDraft = await TradeAsync(host, trade => trade.ConvertAsync(new ConvertTradeDocumentRequest(
            receipt.Id, TradeDocumentKind.PurchaseInvoice, InFy2026.AddDays(2))));

        var posted = await TradeAsync(host, trade => trade.PostInvoiceAsync(invoiceDraft.Id));
        posted.DocumentNumber.ShouldBe("BILL/0001");
        posted.TotalNetValue.ShouldBe(9600m);

        var movements = await StockMovementsAsync(host);
        movements.Count.ShouldBe(1);
        movements.Single().Kind.ShouldBe(StockMovementKind.Purchase);
        movements.Single().SignedQuantity.ShouldBe(8m);

        // The goods land at cost (8 × ₹1,200) on the control account and the supplier is owed the
        // full bill; with an all-goods bill nothing reaches Purchases, which therefore shows no
        // trial-balance row at all.
        var balances = await LedgerBalancesAsync(host, "Stock-in-Hand", "Cisno Pumps Ltd", "Purchases");
        balances["Stock-in-Hand"].ShouldBe(9600m);
        balances["Cisno Pumps Ltd"].ShouldBe(-9600m);
        balances["Purchases"].ShouldBe(0m);

        var reconciliation = await host.InScopeAsync(sp => sp
            .GetRequiredService<IStockReportService>()
            .ReconcileValuationAsync(InFy2026.AddDays(40)));
        reconciliation.ControlAccountBalanceMinor.ShouldBe(reconciliation.TotalStockValueMinor);

        // The fully consumed chain closed quietly through the ancestor walk: GRN converted, PO
        // fulfilled, and the PO outstanding report empty. Both states are re-read fresh — the
        // summaries above were captured before this posting happened.
        var closedReceipt = await TradeAsync(host, trade => trade.GetAsync(receipt.Id));
        closedReceipt!.Status.ShouldBe(TradeDocumentStatus.Converted);
        (await TradeAsync(host, trade => trade.PurchaseOrderOutstandingAsync())).ShouldBeEmpty();

        var closedOrder = await TradeAsync(host, trade => trade.GetAsync(order.Id));
        closedOrder!.Status.ShouldBe(TradeDocumentStatus.Fulfilled);
    }

    // ---------------------------------------------------------------- cancellation reversal

    [Fact]
    public async Task CancelPostedInvoice_ReversesJournalStockAndSerials_InOneTransaction()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var pump = await CreateSerialItemAsync(host, "Submersible Pump 5 HP", world);
        await ReceiveWithSerialsAsync(host, world, pump.Id, ["SN-100"]);

        var draft = await TradeAsync(host, trade => trade.CreateAsync(new SaveTradeDocumentRequest(
            TradeDocumentKind.TaxInvoice,
            InFy2026,
            world.CustomerLedgerId,
            [new TradeDocumentLineInput(pump.Id, world.MainGodownId, 1m, 8500m, SerialNumbers: ["SN-100"])])));
        var posted = await TradeAsync(host, trade => trade.PostInvoiceAsync(draft.Id));

        var cancelled = await TradeAsync(host, trade =>
            trade.CancelPostedInvoiceAsync(posted.DocumentId, "customer returned the unit"));

        cancelled.Status.ShouldBe(TradeDocumentStatus.Cancelled);
        cancelled.Number.ShouldBe("INV/0001"); // the number stays spent

        (await SerialStatusAsync(host, pump.Id, "SN-100")).ShouldBe(SerialNumberStatus.InStock);

        var movements = await StockMovementsAsync(host);
        movements.Count(row => row.Kind == StockMovementKind.SaleCancellation).ShouldBe(1);
        movements.Count(row => row.Kind == StockMovementKind.Sale).ShouldBe(1);

        // The books are back where they started: party, sales and COGS flat, control holding only
        // the original ₹8500 receipt.
        var balances = await LedgerBalancesAsync(host, "Ram Motors", "Sales", "Cost of Goods Sold", "Stock-in-Hand");
        balances["Ram Motors"].ShouldBe(0m);
        balances["Sales"].ShouldBe(0m);
        balances["Cost of Goods Sold"].ShouldBe(0m);
        balances["Stock-in-Hand"].ShouldBe(8500m);

        // A cancelled invoice cannot post twice, nor be casually deleted.
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await TradeAsync(host, trade => trade.PostInvoiceAsync(posted.DocumentId)));
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await TradeAsync(host, trade => trade.DeleteAsync(posted.DocumentId, "second look")));
    }

    // ---------------------------------------------------------------- negative stock gate

    [Fact]
    public async Task PostInvoice_BeyondOnHand_WarnsThenPostsOnlyWhenConfirmed()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var pump = await CreateItemAsync(host, "Centrifugal Pump", world);
        await ReceiveStockAsync(host, world, [(pump.Id, world.MainGodownId, 4m, 90m)]);

        var draft = await TradeAsync(host, trade => trade.CreateAsync(new SaveTradeDocumentRequest(
            TradeDocumentKind.TaxInvoice,
            InFy2026,
            world.CustomerLedgerId,
            [new TradeDocumentLineInput(pump.Id, world.MainGodownId, 9m, 140m)])));

        var validation = await TradeAsync(host, trade => trade.ValidateInvoiceAsync(draft.Id));
        validation.HasErrors.ShouldBeFalse();
        validation.HasWarnings.ShouldBeTrue();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await TradeAsync<object>(host, async trade =>
            {
                await trade.PostInvoiceAsync(draft.Id);
                return null!;
            }));

        var posted = await TradeAsync(host, trade =>
            trade.PostInvoiceAsync(draft.Id, confirmNegativeStockWarnings: true));

        posted.TotalNetValue.ShouldBe(1260m);

        // The five units beyond stock carried out at the last known cost (₹90) rather than
        // inventing one (#20 policy): 9 × ₹90 = ₹810 expensed against four units' worth held.
        posted.CostOfGoodsSoldValueMinor.ShouldBe(810_0000L);
    }

    // ---------------------------------------------------------------- delete rules

    [Fact]
    public async Task Delete_ConvertedSourceAndSourceWithSuccessors_AreRefused_DraftIsAllowed()
    {
        await using var host = new SqliteTestHost();
        await host.MigrateAsync();
        var world = await ArrangeAsync(host);

        var pump = await CreateItemAsync(host, "Domestic Pump", world);

        var spare = await TradeAsync(host, trade => trade.CreateAsync(new SaveTradeDocumentRequest(
            TradeDocumentKind.Quotation,
            InFy2026,
            world.CustomerLedgerId,
            [new TradeDocumentLineInput(pump.Id, null, 1m, 99m)])));

        await TradeAsync(host, trade => trade.DeleteAsync(spare.Id, "operator changed their mind"));
        (await TradeAsync(host, trade => trade.GetAsync(spare.Id))).ShouldBeNull();

        var source = await TradeAsync(host, trade => trade.CreateAsync(new SaveTradeDocumentRequest(
            TradeDocumentKind.Quotation,
            InFy2026,
            world.CustomerLedgerId,
            [new TradeDocumentLineInput(pump.Id, null, 1m, 99m)])));

        var successorOrder = await TradeAsync(host, trade => trade.ConvertAsync(
            new ConvertTradeDocumentRequest(source.Id, TradeDocumentKind.SalesOrder, InFy2026.AddDays(1))));

        await TradeAsync(host, trade => trade.ConvertAsync(new ConvertTradeDocumentRequest(
            successorOrder.Id, TradeDocumentKind.TaxInvoice, InFy2026.AddDays(2),
            DefaultGodownId: world.MainGodownId)));

        // The converted quotation is closed history — it refuses as non-editable.
        var statusRefusal = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await TradeAsync(host, trade => trade.DeleteAsync(source.Id, "tidy up")));

        statusRefusal.Message.ShouldContain("cannot be deleted");

        // The order now carries a live successor of its own: withdraw that one first.
        var lineageRefusal = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await TradeAsync(host, trade => trade.DeleteAsync(successorOrder.Id, "tidy up")));

        lineageRefusal.Message.ShouldContain("carried forward into another document");

        // The tip of the chain — a draft with nothing after it — still deletes freely.
        var documents = await TradeAsync(host, trade => trade.ListAsync(new TradeDocumentFilter
        {
            Kinds = [TradeDocumentKind.TaxInvoice],
            Take = 10,
        }));
        var draftInvoice = documents.Single(row => row.Status == TradeDocumentStatus.Draft);

        await TradeAsync(host, trade => trade.DeleteAsync(draftInvoice.Id, "not needed after all"));

        // With its only successor gone the order can go too.
        await TradeAsync(host, trade => trade.DeleteAsync(successorOrder.Id, "chain withdrawn"));
    }

    // ---------------------------------------------------------------- arrange helpers

    private sealed record TradeWorld(
        Guid CompanyId,
        Guid MainGodownId,
        Guid CashLedgerId,
        Guid CustomerLedgerId,
        Guid SupplierLedgerId);

    /// <summary>Runs one trade operation in its own DI scope, as the shell drives each command.</summary>
    private static Task<T> TradeAsync<T>(SqliteTestHost host, Func<ITradeDocumentService, Task<T>> action) =>
        host.InScopeAsync(sp => action(sp.GetRequiredService<ITradeDocumentService>()));

    private static Task TradeAsync(SqliteTestHost host, Func<ITradeDocumentService, Task> action) =>
        host.InScopeAsync(async sp => await action(sp.GetRequiredService<ITradeDocumentService>())
            .ConfigureAwait(false));

    private static async Task<TradeWorld> ArrangeAsync(SqliteTestHost host)
    {
        var companyId = await AddFullyProvisionedCompanyAsync(host);

        return await host.InScopeAsync(async sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var chartOfAccounts = sp.GetRequiredService<IChartOfAccountsService>();

            var godown = (await unitOfWork.Repository<Domain.Inventory.Godown>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId);
            var cash = (await unitOfWork.Repository<Ledger>().ListAsync())
                .Single(candidate => candidate.CompanyId == companyId && candidate.Name == "Cash");

            var groups = await unitOfWork.Repository<AccountGroup>().ListAsync();
            var debtorsGroup = groups.Single(group =>
                group.CompanyId == companyId && group.Name == "Sundry Debtors");
            var creditorsGroup = groups.Single(group =>
                group.CompanyId == companyId && group.Name == "Sundry Creditors");

            var customer = await chartOfAccounts.CreateLedgerAsync(
                new CreateLedgerRequest("Ram Motors", debtorsGroup.Id));
            var supplier = await chartOfAccounts.CreateLedgerAsync(
                new CreateLedgerRequest("Cisno Pumps Ltd", creditorsGroup.Id));

            return new TradeWorld(companyId, godown.Id, cash.Id, customer.Id, supplier.Id);
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

        return companyId;
    }

    private sealed record ItemProxy(Guid Id, string Name);

    private static Task<ItemProxy> CreateItemAsync(SqliteTestHost host, string name, TradeWorld world) =>
        host.InScopeAsync(async sp =>
        {
            var masters = sp.GetRequiredService<IInventoryMasterService>();
            var unit = (await masters.ListUnitsAsync()).Single(unit => unit.Code == "NOS");

            var saved = await masters.SaveItemAsync(new SaveItemRequest(Id: null, name, unit.Id));

            return new ItemProxy(saved.Id, saved.Name);
        });

    private static Task<ItemProxy> CreateSerialItemAsync(SqliteTestHost host, string name, TradeWorld world) =>
        host.InScopeAsync(async sp =>
        {
            var masters = sp.GetRequiredService<IInventoryMasterService>();
            var unit = (await masters.ListUnitsAsync()).Single(unit => unit.Code == "NOS");

            var saved = await masters.SaveItemAsync(new SaveItemRequest(
                Id: null,
                name,
                unit.Id,
                IsSerialTracked: true));

            return new ItemProxy(saved.Id, saved.Name);
        });

    private static Task<PostedStockVoucherSummary> ReceiveStockAsync(
        SqliteTestHost host,
        TradeWorld world,
        IReadOnlyList<(Guid ItemId, Guid GodownId, decimal Quantity, decimal Cost)> lines) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IStockVoucherService>()
            .PostAsync(new PostStockVoucherRequest(
                StockVoucherPurpose.Receipt,
                InFy2026,
                lines
                    .Select(line => new StockVoucherLineRequest(
                        StockMovementDirection.Increase,
                        line.ItemId,
                        line.GodownId,
                        line.Quantity,
                        UnitCost: line.Cost))
                    .ToList(),
                Narration: "opening purchases",
                CounterLedgerId: world.CashLedgerId)));

    private static Task<PostedStockVoucherSummary> ReceiveWithSerialsAsync(
        SqliteTestHost host,
        TradeWorld world,
        Guid itemId,
        IReadOnlyList<string> serials) =>
        host.InScopeAsync(sp => sp.GetRequiredService<IStockVoucherService>()
            .PostAsync(new PostStockVoucherRequest(
                StockVoucherPurpose.Receipt,
                InFy2026,
                [
                    new StockVoucherLineRequest(
                        StockMovementDirection.Increase,
                        itemId,
                        world.MainGodownId,
                        serials.Count,
                        UnitCost: 8500m,
                        SerialNumbers: serials),
                ],
                Narration: "serial goods in",
                CounterLedgerId: world.CashLedgerId)));

    // ---------------------------------------------------------------- read helpers

    /// <summary>
    /// Named ledgers' net balances from the trial balance. A ledger with no movement yet has no
    /// trial-balance row, which reads as a flat zero here — the same answer an operator gets.
    /// </summary>
    private static Task<IReadOnlyDictionary<string, decimal>> LedgerBalancesAsync(
        SqliteTestHost host,
        params string[] ledgerNames) =>
        host.InScopeAsync<IReadOnlyDictionary<string, decimal>>(async sp =>
        {
            var balances = await sp.GetRequiredService<IAccountingReportService>()
                .GetTrialBalanceAsync(InFy2026.AddDays(40));

            return ledgerNames.ToDictionary(
                name => name,
                name => balances.Rows
                    .Where(candidate => candidate.LedgerName == name)
                    .Sum(row => row.Debit - row.Credit));
        });

    private static Task<IReadOnlyList<(string LedgerName, decimal Debit, decimal Credit)>> TrialBalanceAsync(
        SqliteTestHost host) =>
        host.InScopeAsync<IReadOnlyList<(string LedgerName, decimal Debit, decimal Credit)>>(async sp =>
        {
            var balances = await sp.GetRequiredService<IAccountingReportService>()
                .GetTrialBalanceAsync(InFy2026.AddDays(40));

            return balances.Rows
                .Select(row => (row.LedgerName, row.Debit, row.Credit))
                .OrderBy(row => row.LedgerName, StringComparer.Ordinal)
                .ToList();
        });

    private static Task<int> StockLedgerCountAsync(SqliteTestHost host) =>
        host.InScopeAsync(async sp =>
        {
            var entries = await sp.GetRequiredService<IUnitOfWork>()
                .Repository<StockLedgerEntry>()
                .ListAsync();

            return entries.Count;
        });

    private static Task<IReadOnlyList<(StockMovementKind Kind, decimal SignedQuantity)>> StockMovementsAsync(
        SqliteTestHost host) =>
        host.InScopeAsync<IReadOnlyList<(StockMovementKind Kind, decimal SignedQuantity)>>(async sp =>
        {
            var entries = await sp.GetRequiredService<IUnitOfWork>()
                .Repository<StockLedgerEntry>()
                .ListAsync();

            return entries
                .Select(entry => (entry.Kind, entry.SignedQuantity))
                .OrderBy(row => row.Kind)
                .ThenBy(row => row.SignedQuantity)
                .ToList();
        });

    private static Task<SerialNumberStatus> SerialStatusAsync(
        SqliteTestHost host,
        Guid itemId,
        string valueNormalised) =>
        host.InScopeAsync(async sp =>
        {
            var serials = await sp.GetRequiredService<IUnitOfWork>()
                .Repository<SerialNumber>()
                .ListAsync();

            return serials
                .Single(serial => serial.ItemId == itemId && serial.ValueNormalised == valueNormalised)
                .Status;
        });
}
