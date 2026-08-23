# Feature — Voucher Numbering & Gap Report

**Status: implemented and verified, Phase 1.**

## What & why

Decision D2 made real: per-type series (`PMT/0001`…) whose counter resets each financial year,
gaps blocked (cancelled vouchers keep their numbers), manual overrides permitted but recorded and
never silently reused — plus the audit report that surfaces whatever gaps do arise (§7, §8.7).

## User workflow

Posting side needs nothing special: numbers allocate automatically on save. To override, type the
number (e.g. `PMT/0050`) into **Manual number** before posting — later automatic numbers continue
at `0051`.

Gap report:
1. Open **Audit → Number gaps**, press **Load lists**.
2. Pick a series and financial year, press **Show gaps**.
3. Empty list = every number accounted for; listed numbers are missing on purpose-visible terms
   (usually a manual skip). They are never backfilled.

## Developer notes

- `VoucherNumberAllocator` claims via the `(series, financial year)` high-water mark inside the
  posting transaction; manual overrides validate format through `VoucherNumberParser` (shared with
  the gap report), refuse duplicates against current revisions, then advance the mark.
- Uniqueness is ultimately enforced by the filtered unique index on
  `(CompanyId, VoucherTypeId, SeriesId, FinancialYearId, NumberNormalised) WHERE SupersededByRevisionId IS NULL`.
- Gap SQL returns issued numbers only; sequence positions are recovered in the service so prefix/
  suffix logic stays out of the dialect layer.
- Concurrency deferred to Phase 9 (single-user phases 1–8), as recorded in the spec.

## Verification

- `Post_ManualNumberOverride_AdvancesHighWaterAndIsRecorded`, 
  `Cancel_PostedVoucher_KeepsItsNumberOccupiedAndFlagsTheDayBook`,
  `Post_AcrossAFinancialYearBoundary_RestartsTheCounter` — numbering rules proven end-to-end.
