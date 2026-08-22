# Phase 2 — Inventory & Stock Management

```yaml
Phase: 2
Title: Inventory & Stock Management
Status: Approved
Source: project.md §2, §4.2, §10 (Phase 2)
Depends on: Phase 1
Blocks: Phases 3, 4, 8
```

> **Approved 22 Aug 2026** by the project owner in an interactive session, with the amendments
> recorded in §2a and the open questions of §4 closed as noted there.

## 1. Deliverables (project.md §10)

Stock groups, item master, godowns, batch + serial tracking,
valuation method, reorder alerts, stock aging.

## 2. Scope notes

- **One unified stock ledger** (`open-questions.md` #1, confirmed). Every module — quotation, order, delivery,
  invoice, purchase, transfer — reads and writes stock through this single ledger. No module keeps
  its own stock table or cached quantity.
- **Item master (amended — `open-questions.md` #5):** Name plus optional code, stock group, base unit
  with optional alternate unit + conversion factor, tracking flags, valuation override, reorder level.
  The motor/pump attribute list from the draft (Brand/Make, Model No., HP/KW, Phase, Voltage,
  Frequency, Type, Discharge, Head, Warranty months) was **dropped by owner decision** on approval;
  attributes remain addable later as plain nullable columns. HSN/SAC moves to Phase 5 where GST first
  needs it, as data-driven config per non-negotiable #6 — never hardcoded.
- **Units:** base unit plus alternate/compound units with a conversion factor, for the case where the
  sourcing unit differs from the selling unit.
- **Batch tracking** and **serial-number tracking per unit** (`open-questions.md` #6: per-item flag) — a serial is the warranty-traceable
  identity of one physical unit. A serial has a lifecycle: received → in stock → sold →
  (optionally) returned. A serial can be in stock exactly once.
- **Valuation:** FIFO or weighted average, configurable globally (**FIFO default** — `open-questions.md` #19)
  with a per-item override.
- **Godowns/warehouses** with inter-godown transfer.
- **Reorder level + low-stock alerts; stock aging buckets.**
- **Simple BOM/assembly** (pump + motor sold as a set) — basic only in v1: a kit item whose
  components deplete on sale. No multi-level manufacturing, no work orders, no routing.
- **Search:** item lookup filters by name and code over a large SKU fixture (C2.6 as amended).
  Barcode support exists but must never be *required* — barcode-free billing is the default.

### 2a. Amendments on approval (22 Aug 2026)

| What | Draft said | Approved as | Why |
|---|---|---|---|
| Item attributes | Motor/pump field list (§2) | Dropped; name/code-based item master | Owner decision (`open-questions.md` #5) |
| C2.6 | Search by brand + HP | Search by name/code over ≥5,000-SKU fixture within the UI budget | Follows from dropping the attribute fields |
| Serial scope | Default on for motors/pumps | Per-item flag only, no category defaults | Follows from dropping category knowledge |
| Negative stock | Open question | Warn (confirm dialog), hard block only for serial-tracked issues | `open-questions.md` #20 |
| Valuation default | Open question | FIFO global default, weighted average selectable globally + per item | `open-questions.md` #19 |

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
| C2.6 | Item search by name/code returns correct results over a ≥5,000-SKU fixture within the UI budget (amended from brand+HP on approval — see §2a) |

## 4. Open questions — closed at approval (22 Aug 2026)

- Item attribute list: **dropped**; name/code-based item master (`open-questions.md` #5, §2a).
- Serial-tracked items: **per-item flag**, no category defaults (`open-questions.md` #6).
- Default valuation method: **FIFO** (`open-questions.md` #19).
- Negative stock: **warn** with explicit confirmation; serial issues stay hard-blocked (`open-questions.md` #20).
