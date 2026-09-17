# E-way bills

**Phase 6 · E-Invoicing & E-Way Bill** (`specs/phase-6-einvoice-eway.md` §2, §4a)

## Feature

Generates e-way bills **by IRN** against an already-registered invoice, keeps Part-B current as the
vehicle changes en route, cancels within 24 hours, and asks the portal to extend validity.

Part-A is captured from the invoice — party, value, distance. Part-B carries the transport detail:
transporter GSTIN and name, transport document number and date, vehicle number, transport mode
(road / rail / air / ship) and vehicle type (regular / over-dimensional cargo).

## Two rules worth stating plainly

**Which Part-B fields are required depends on the mode.** By road it is the vehicle number — that is
what gets checked at a barrier. For rail, air and ship it is the carrier's own document number and
date. Both the screen and the domain enforce this per mode, so a screen bypassed by a script cannot
produce a half-filled bill. Marking every field mandatory instead would be simpler and wrong.

**Validity depends on vehicle type, not just distance.** One day per 200 km for regular carriage, and
**one day per 20 km for over-dimensional cargo** — a tenth as much. Any part-day counts as a whole
day, and every bill gets at least one, so a 5 km local delivery is valid for a day rather than none.
The spec's scope note mentions only the 200 km figure because that is the ordinary case; the ODC slab
is implemented because Part-B carries a vehicle type, and applying the regular rule to an ODC
consignment would overstate its validity tenfold. These are Rule 138 slabs — a rule, not a rate, so
they are expressed in code rather than as config rows.

## Threshold: shown, never enforced

Whether a bill is *required* is displayed as guidance. The statutory test is on **consignment** value,
and a consignment is not always one invoice — several may travel together. So the software states the
position for the selected document and leaves the decision with the user, rather than refusing to
create a bill somebody has a reason to want.

The threshold itself is a config row (`EinvoiceSettings.EwayBillThresholdMinor`, seeded at ₹50,000),
per non-negotiable #6 — the figure has moved before. The comparison is "exceeds", so a consignment at
exactly the threshold does not need a bill.

## Validity is granted, not calculated

`EwayBillValidity` exists so the expected expiry can be shown *before* generation and asserted in
tests. But once the portal answers, **the portal's expiry is the one stored**. Two sources of truth
for a date a roadside officer reads off the bill is not a disagreement worth having. The same applies
to extensions: the portal grants the new expiry and we record it.

## User workflow (E-invoicing → E-way bills)

1. Register the invoice's e-invoice first — Phase 6 generates bills by IRN.
2. Pick the document. The pane states whether the value obliges a bill.
3. Fill the distance and Part-B. The expected validity updates as you type, which is where the ODC
   slab surprises people.
4. **Generate** (Ctrl+A). The issued number and the portal's expiry appear.
5. **Update Part-B** when the vehicle changes; **Extend validity** with the remaining distance if the
   trip runs long; **Cancel bill** within 24 hours of issue.

## Developer notes

- `EwayBill`, `EwayBillValidity`, `EwayBillThresholdRule`, `TransportMode`, `VehicleType` —
  `src/VinayakaAgencies.Domain/Einvoicing/`.
- Part-A and Part-B share one row because the portal shares the concept: a bill exists with Part-A
  filled and Part-B blank, and Part-B is updated repeatedly afterwards. Splitting them would invent a
  distinction the API does not have.
- `IEwayBillService` / `EwayBillService` (`Application/Einvoicing/`). The bill row is saved before the
  portal call for the same reason e-invoice registrations are — a bill the portal issued and we failed
  to record is worse than a draft row nobody used.
- `(CompanyId, TradeDocumentId)` is unique, so a rejected or cancelled row is reused rather than
  accumulating drafts. `EwayBillNumber` is unique only among rows that have one (filtered index).
- Wire mapping in `NicEwayBillPayload`; NIC's numeric mode codes and `R`/`O` vehicle-type codes live
  there and nowhere else.

## Verification

- Validity slabs, the threshold's "exceeds" boundary, per-mode Part-B rules and the cancellation
  window: `tests/…Domain.Tests/Einvoicing/EwayBillTests.cs`.
- Generate-by-IRN, portal-granted expiry, Part-B update, cancel, extend, and the threshold read from
  config: `tests/…Infrastructure.Tests/Einvoicing/EwayBillWorkflowTests.cs`.
- **C6.2 is not met.** Generating a bill in the sandbox needs real credentials
  (`specs/open-questions.md` #10). Everything up to the wire is verified against a recorded portal;
  see `specs/evidence/phase-6-evidence.md`.
