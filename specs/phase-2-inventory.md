# Phase 2 — Inventory & Stock Management

```yaml
Phase: 2
Title: Inventory & Stock Management
Status: Draft
Source: project.md §2, §4.2, §10 (Phase 2)
Depends on: Phase 1
Blocks: Phases 3, 4, 8
```

> **Draft.** Do not implement until `Status: Approved`.

## 1. Deliverables (project.md §10)

Stock groups, item master with motor/pump fields, godowns, batch + serial tracking,
valuation method, reorder alerts, stock aging.

## 2. Scope notes

- **One unified stock ledger** (`open-questions.md` #1). Every module — quotation, order, delivery,
  invoice, purchase, transfer — reads and writes stock through this single ledger. No module keeps
  its own stock table or cached quantity.
- **Item master, motor/pump fields (`project.md` §2, confirm via `open-questions.md` #5):**
  Brand/Make, Model No., HP/KW rating, Phase (1-Ph/3-Ph), Voltage, Frequency, Type (submersible /
  centrifugal / monoblock / openwell / …), Discharge, Head, Warranty months.
  **HSN/SAC is a per-item configurable field, never hardcoded** — codes and rates change.
- **Units:** base unit plus alternate/compound units with a conversion factor, for the case where the
  sourcing unit differs from the selling unit.
- **Batch tracking** and **serial-number tracking per unit** — a serial is the warranty-traceable
  identity of one physical motor/pump. A serial has a lifecycle: received → in stock → sold →
  (optionally) returned. A serial can be in stock exactly once.
- **Valuation:** FIFO or weighted average, configurable globally with a per-item override.
- **Godowns/warehouses** with inter-godown transfer.
- **Reorder level + low-stock alerts; stock aging buckets.**
- **Simple BOM/assembly** (pump + motor sold as a set) — basic only in v1: a kit item whose
  components deplete on sale. No multi-level manufacturing, no work orders, no routing.
- **Search:** high SKU count means item lookup must filter by brand / model / HP, not just name or
  code. Barcode support exists but must never be *required* — barcode-free billing is the default.

## 3. Checkpoint criteria (project.md §10)

| # | Criterion |
|---|---|
| C2.1 | Stock valuation reconciles to the inventory GL control account |
| C2.2 | Selling a serial-tracked item correctly records which unit serial was sold |

Additional:

| # | Criterion |
|---|---|
| C2.3 | The same serial cannot be sold twice; attempting it is rejected |
| C2.4 | FIFO and weighted-average valuations are each verified against a hand-computed fixture |
| C2.5 | An inter-godown transfer changes location without changing total stock value |
| C2.6 | Item search by brand + HP returns correct results over a ≥5,000-SKU fixture within the UI budget |

## 4. Open questions to close before approval

- Confirm/extend the item attribute list — `open-questions.md` #5.
- Which items are serial-tracked — all, or by threshold? `open-questions.md` #6.
- Default valuation method: FIFO or weighted average?
- Negative stock: block, warn, or allow?
