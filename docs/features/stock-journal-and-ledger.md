# Feature — Stock Vouchers & the Unified Stock Ledger

**Status: implemented and verified, Phase 2.**

## What & why

The one unified stock ledger (`open-questions.md` #1) and the manual documents that feed it:
opening balances, receipts, issues and inter-godown transfers. Every movement is one document,
one SJ number, and exactly one accounting voucher posted through the Phase 1 journal engine —
stock never reaches the books around it.

## User workflow

Inventory → **Stock vouchers**:

1. Pick a purpose (Opening balance / Receipt / Issue / Transfer), date, and — except for
   transfers — the counter ledger the value comes from or goes to.
2. Find items by typing in the search box, set godown/quantity/cost (receipts need a cost above
   zero; issues are valued automatically), batch and serial numbers where the item demands them.
3. **Validate** shows errors and warnings without posting. Posting refuses on errors; on
   negative-stock warnings it asks for confirmation once and posts only if confirmed
   (`open-questions.md` #20: warn). Serial availability is always a hard block — confirmation
   cannot conjure a unit that was never received (C2.3).

## Developer notes

- `StockVoucherService.PostAsync` runs validate → expand kits → value decreases (FIFO/WA with
  intra-voucher consumption carried forward) → assign transfer-receipt costs → allocate nothing
  itself (the posted voucher's SJ number IS the document's number) → `IVoucherPostingService.PostAsync`
  inside one `IUnitOfWork` transaction → append `StockLedgerEntry` rows with company-scoped
  `GlobalSequence` → create/move/sell serials. A failure midway leaves neither side touched.
- Accounting effect: receipt/issue post per-line Dr/Cr on "Stock-in-Hand" plus one counter-ledger
  line; a transfer posts a self-balancing pair on the control account (net zero — location changes,
  value does not, C2.5), whose receipt side is landed at exactly the issue side's value via exact
  minor-unit distribution.
- Serials live once ever (`(CompanyId, ItemId, ValueNormalised)` unique): created InStock on first
  receipt, `MarkSold` on issue (C2.2 records which unit sold), `MoveTo` on transfer,
  `ReturnToStock` for returns.
- Vouchers are never altered or deleted after posting; mistakes are corrected by reversing vouchers.
- Quantities/values persist as minor-unit integers throughout (ADR-0003); the decimal `Quantity`
  properties are computed projections ignored by EF.

## Verification

- C2.2 `Post_SerialTrackedSale_RecordsExactlyWhichUnitWasSold`;
  C2.3 `Post_SecondSaleOfSameSerial_IsRejectedAndNothingMoves`;
  C2.5 `Transfer_ChangesLocationWithoutChangingTotalValue`;
  warn policy `Post_IssueBeyondOnHand_WarnsThenPostsOnlyWhenConfirmed`.
