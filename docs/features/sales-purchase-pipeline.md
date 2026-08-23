# Feature — Sales & Purchase Pipeline

**Status: implemented, Phase 3** (spec `specs/phase-3-sales-purchase-pipeline.md`, Approved 22 Aug 2026).

## What & why

The quotation → sales order → delivery challan → tax invoice chain and its purchase mirror
(purchase order → goods receipt → purchase invoice). Documents convert without re-keying; the two
invoice kinds post to accounts **and** inventory atomically through the Phase 1 journal engine;
orders report what is still pending (C3.4).

## User workflow

Sales & Purchase → **Documents**:

1. Pick a document kind, date and party ledger, add item lines (godowns appear only on kinds that
   move stock), optional party reference ("your order no.") and narration. Every kind except the
   invoices is numbered immediately from its series (`QTN/`, `SO/`, `DC/`, `PO/`, `GRN/`); a draft
   invoice carries no number until it posts — its `INV/`/`BILL/` number comes from the journal
   engine at posting, so one document consumes exactly one number.
2. **Convert** carries a document forward: quotation → sales order or tax invoice; order →
   challan or invoice; challan → invoice; purchase order → goods receipt or purchase invoice;
   goods receipt → purchase invoice. Items, quantities, rates, discounts, terms and the party all
   travel (C3.1); godowns fall back to the default godown when jumping straight to an invoice;
   both documents keep the link for drill-down either way.
3. **Post** an invoice draft: validation reports blocking errors and confirmable warnings
   (negative stock); posting writes the journal voucher, the mirror stock voucher, stock-ledger
   rows and serial lifecycle inside one transaction (C3.3).
4. **Cancel** a posted invoice reverses it through the journal engine — cancelling revision,
   reversing stock rows, sold serials return — in one transaction; the number stays spent.

Sales & Purchase → **Order outstanding**: ordered minus carried into successors, per line, with
pending value. An order leaves the report only when fully consumed; cancelling an invoice puts its
quantity back and reopens a fulfilled order.

## Developer notes

- Exactly two kinds post (`TradeDocument.IsStockMoving`): TaxInvoice and PurchaseInvoice. Every
  other kind is data only — a quotation touches no ledger row and no stock row by construction
  (C3.5) — so Phase 2's stock-value-to-control reconciliation survives untouched.
- Stock moves exactly once per direction: a posted invoice appends a **mirror** `StockVoucher`
  (purpose Issue/Receipt) in the same transaction, and ledger rows hang off it, keeping every
  Phase 2 foreign-key path (valuation replay, serial receipts/sales, C2.1 reconciliation) working
  unchanged. Challans and goods receipts move no stock (§4a decisions 1–2).
- Accounting legs: sale = Dr party / Cr Sales at billed total + Dr COGS / Cr Stock-in-Hand at FIFO
  cost (either half drops out if zero); purchase = Dr Stock-in-Hand at what was paid + the
  difference against Purchases + Cr supplier.
- Fulfilment tracking: consumption of a source = quantities on its **direct** non-cancelled
  successors' current lines. Quotations/challans/GRNs close (Converted) when their successor is
  raised; **orders close only when a successor invoice posts**, walking up the `ConvertedFrom`
  chain (order → challan → invoice closes both). Cancelling a posted invoice walks back down,
  reopening any ancestor whose balance returned. Closing at conversion time was tried and removed:
  trimming a converted draft — the normal partial-delivery flow — stranded the balance off the
  report.
- Kits expand into component issues at posting; kit components cannot be serial-tracked (invoice
  those components directly with their serials instead).

## Verification

`tests/VinayakaAgencies.Infrastructure.Tests/Trade/TradeWorkflowTests.cs`:

- C3.5 `Create_Quotation_LeavesTrialBalanceAndStockLedgerUntouched`
- C3.1 `Convert_QuotationToOrderToInvoice_CarriesDataWithoutRekeying`
- C3.3 `PostInvoice_SerialUnavailableAtPost_IsRefusedAndNothingPersists` (+ the ambient-transaction
  rollback proofs in `UnitOfWorkTransactionTests`)
- C3.4 `Convert_PartialDelivery_KeepsPendingQuantityOnOutstandingReport`,
  `CancelPostedInvoice_ReopensAFulfilledOrder_RestoringItsPendingQuantity`
- Purchase mirror `PurchaseChain_GoodsReceiptThenInvoice_ReceivesStockOnceAtWhatWasPaid`;
  reversal `CancelPostedInvoice_ReversesJournalStockAndSerials_InOneTransaction`;
  warn gate `PostInvoice_BeyondOnHand_WarnsThenPostsOnlyWhenConfirmed`.
