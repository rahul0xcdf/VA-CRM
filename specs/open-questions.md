# Open Questions — must be resolved before Phase 1 implementation begins

From `project.md` §11 and §2. Phase 0 does **not** depend on any of these; Phase 1 onward does.
Update the Answer column and set Status to `Confirmed` as the project owner responds.

| # | Question | Working assumption (used if not corrected) | Blocks | Status |
|---|---|---|---|---|
| 1 | "One stock management" — is this a single centralised, real-time stock ledger that every module (quotation, order, invoice, purchase, transfer) reads and writes through, with no parallel stock-tracking system? | Yes — one unified stock ledger. | Phase 2 | **Open** |
| 2 | Exact customer categories. Placeholder: Retailer / Dealer / Wholesaler / Institutional. | Placeholder list, replaceable as master data (not an enum in code). | Phase 4 | **Open** |
| 3 | Is margin computed on **cost**, on **sale price / MRP**, or must both be supported per item-category pair? | Support both; a per-price-list-line flag selects the basis. | Phase 4 | **Open** |
| 4 | Is multi-user / concurrent billing a day-one launch requirement? If yes, Phase 9 must be pulled forward. | No — single-user through Phase 8, multi-user at Phase 9. | Phase 9 (and the DB decision in Phase 0) | **Open** |
| 5 | Motor/pump item attributes — confirm or extend: Brand/Make, Model No., HP/KW, Phase (1-Ph/3-Ph), Voltage/Frequency, Type (submersible/centrifugal/monoblock/openwell), Discharge, Head, Warranty months, HSN/SAC (per item, configurable). | Use the §2 list as-is; all attributes are nullable and extensible without a schema change where practical. | Phase 2 | **Open** |
| 6 | Which serial-tracked categories? All motors/pumps, or only above a value/HP threshold? | All items flagged `IsSerialTracked` on the item master; default on for motors/pumps. | Phase 2 | **Open** |
| 7 | Product name / branding. Solution and root namespace are currently `MotorPumpSuite`. | `MotorPumpSuite` — a rename is a find/replace plus a solution rename, cheap to do before Phase 1. | Cosmetic | **Open** |
| 8 | Company GSTIN(s), state code, and whether more than one GSTIN/branch bills from this install. | Single company, single GSTIN at launch; schema is multi-company-ready. | Phase 5 | **Open** |
| 9 | Financial year start — assumed 1 April (Indian standard). | 1 April. | Phase 1 | **Open** |
| 10 | NIC e-invoice sandbox credentials (client ID, secret, GSTIN, username, password) — needed to test Phase 6. | Not yet supplied; Phase 6 cannot reach its checkpoint without them. | Phase 6 | **Open** |
