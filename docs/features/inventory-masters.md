# Feature — Inventory Masters (Stock Groups, Godowns, Units, Items, Kits)

**Status: implemented and verified, Phase 2.**

## What & why

The Phase 2 masters as approved (`specs/phase-2-inventory.md` §1–2a): a name-and-code item master
(the motor/pump attribute list was dropped by owner decision, `open-questions.md` #5), stock groups,
godowns with a seeded system "Main Location", units of measure with base + alternate + conversion
factor, per-item serial/batch tracking flags, valuation override and reorder level — plus the basic
kit (BOM) whose components deplete on issue. HSN/SAC deliberately moves to Phase 5 as data-driven
config; nothing tax-related is hardcoded.

## User workflow

Inventory → **Items / Stock groups / Godowns**:

1. Create units first ("NOS" is seeded), then stock groups, then items.
2. An item needs at least a name and base unit. Alternate unit requires a conversion factor
   (> 0) — 1 alternate unit = factor × base units. Quantities in the ledger are always base units.
3. Serial-tracked items take whole quantities only and cannot also be batch-tracked.
4. Kit items are never received — set their components on the Items screen; issuing the kit
   depletes each component by quantity × kit quantity.

## Developer notes

- All writes go through `IUnitOfWork`; every create/change is edit-logged automatically because it
  is mapped (no opt-outs anywhere).
- Uniqueness mirrors the accounting masters: `(CompanyId, NameNormalised)` unique indexes built on
  `NameNormaliser`, so "Cash" vs "cash" collides identically on SQLite and SQL Server.
- Hard delete follows §4.6: an item with ledger history or kit references refuses delete (deactivate
  instead); a kit's own component rows are removed explicitly in the same save when the kit itself
  is deleted (the model has no cascades — every FK is Restrict).
- Quantities persist as scaled integers in minor units of 1/10,000 (ADR-0003 discipline extended to
  stock): `Item.ReorderLevelMinor`, `StockVoucherLine.QuantityMinor`, `ItemKitComponent.QuantityMinor`,
  `Item.ConversionFactorMinor`, ledger `QuantityInMinor/OutMinor`. Decimal properties are computed
  projections, ignored by EF — the model guard test enforces this.
- Idempotent inventory seed: `ICompanyProvisioningService.EnsureInventorySeedAsync` adds, per
  company and only if missing: the system "Stock Journal" voucher type (Kind=Journal) + SJ/ series,
  "Main Location", "NOS", and the FIFO settings row. Called at startup after migrations.

## Verification

- Domain guards: `ItemTests`, `SerialNumberTests`, `StockVoucherTests`, `StockLedgerEntryTests`.
- Workflow: `InventoryWorkflowTests.Post_KitIssue_DepletesComponentsAtTheirOwnCosts`,
  `Validate_ReceiptRepeatingAnExistingSerial_IsAnErrorNotAWarning`.
