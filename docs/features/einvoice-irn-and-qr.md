# E-invoice: IRN and signed QR

**Phase 6 · E-Invoicing & E-Way Bill** (`specs/phase-6-einvoice-eway.md` §2)

## Feature

Registers posted tax invoices with NIC's Invoice Registration Portal and records what comes back:
the **IRN**, the portal's acknowledgement number and date, the signed invoice, and the **signed QR**
that gets printed. Cancellation inside the 24-hour window is supported; after it closes, a credit
note is the only route and the screen says so.

The IRN is **SHA-256 over `SupplierGSTIN + FY + DocType + DocNumber`, concatenated in that order
with no separators**, rendered as 64 lowercase hex characters. NIC's own published example is the
regression fixture: `01AAAAA9999A19N` + `2019-20` + `INV` + `ABC01234` hashes the string
`01AAAAA9999A19N2019-20INVABC01234`. The draft spec listed those four inputs in a different order;
that was corrected at approval, because the order changes the hash and a wrong one produces a
valid-looking IRN the portal simply disagrees with.

The signed QR carries ten fields: `SellerGstin`, `BuyerGstin`, `DocNo`, `DocTyp`, `DocDt`,
`TotInvVal`, `ItemCnt`, `MainHsnCode`, `Irn`, `IrnDt`. `MainHsnCode` is the HSN of the line with the
**highest taxable value** — not the first line. What is printed is always the JWS blob NIC signed;
`EinvoiceQrBuilder` assembles the same ten fields from our books only so the two can be compared.

## User workflow (E-invoicing → E-invoice (IRN))

1. Enter the NIC credentials once on **E-invoice settings** (see below).
2. Pick a posted tax invoice from the list. The right pane shows its registration, if any.
3. **Preview payload** shows what would be sent, the exact IRN hash input, and the computed IRN —
   without contacting the portal. Useful before going live, and the fastest way to diagnose a refusal
   afterwards.
4. **Get IRN** (Ctrl+A) registers it. Pressing it twice is safe: the second press reports the
   existing registration rather than creating a second one.
5. To cancel, fill the reason code and press **Cancel IRN**. The button is disabled once the
   24-hour window shown beside it has closed.

## Resilience

This is the part of the phase with real design content, and it is worth understanding before
changing anything.

The registration row — with its locally computed IRN — is committed **before** the portal is called.
That looks backwards until the failure case is considered: if the row were written after a successful
response, a call that timed out *after* the portal had registered the invoice would leave no record of
an IRN that now exists, and the obvious next move would be to submit again and collect a duplicate
rejection. Writing first means the IRN is always recoverable.

A call therefore has three outcomes, not two: accepted, refused, and **no answer**. A refusal means
the document is wrong and retrying it will fail identically. Silence means the invoice may well be
registered. So an unresolved attempt leaves the row `Pending`, and the next attempt calls
`GetByIrn` first — if the portal already holds that IRN, no second submission happens at all.

A unique index on `(CompanyId, Irn)` backs this up in the schema rather than leaving the guarantee
resting on a code path nobody re-reads.

## Credentials

Stored DPAPI-encrypted, `CurrentUser` scope, with application-specific entropy mixed in. Machine
scope would let any account on the box decrypt them; user scope ties them to the Windows login that
entered them. **Consequence to know about:** running the app as a different Windows user, or
restoring the database on another machine, means re-entering all four — the settings screen says so
and the decrypt failure message explains it rather than surfacing "the data is invalid".

Nothing displays a stored credential, not even masked, and no DTO carries one. Every request and
response body is redacted at the client boundary before it reaches a logger or the database, by two
passes: credential-ish JSON keys, and exact known secret values. Either pass alone leaks — the first
misses a secret quoted back inside an error message, the second misses a secret we never held in a
variable.

## Schema additions

`Company` gained `AddressLine1`, `AddressLine2`, `City`, `Pincode`; `Customer` gained `City` and
`Pincode`. The IRP needs a structured address and Phase 3 satisfied Rule 46 with a free-text template
header, from which no pincode can be recovered. All nullable, so existing books stay valid; instead
`EinvoiceService` refuses to submit an incomplete address and names the missing field. Recorded in
spec §4b.

## Developer notes

- `IrnFormula`, `GstFinancialYear`, `EinvoiceRegistration`, `EinvoiceApiCall`, `EinvoiceSettings` —
  `src/VinayakaAgencies.Domain/Einvoicing/`. All pure or self-contained; no network, so the IRN is
  testable without credentials.
- `GstFinancialYear` is the one date rule **not** driven by `Company.FinancialYearStartMonth`. The
  GST year is 1 April–31 March by statute; feeding a company-specific cycle into an IRN would produce
  a hash the portal computes differently.
- `IIrpClient` (`Application/Einvoicing/`) is the port; `NicIrpClient` (`Infrastructure/Einvoicing/`)
  implements it. No method throws for a refusal or a timeout — both are ordinary answers carried on
  the result. A GSP would be a second implementation, not a rewrite.
- NIC's handshake is RSA over the auth payload, then AES for everything after.
  **AES-ECB is NIC's requirement, not a choice made here** — see the note in `IrpCryptography`.
- `IrpSessionCache` is a singleton because `AddHttpClient` registers typed clients as transient; a
  cache in a client field would re-authenticate every call against a rate-limited endpoint.
- Endpoint paths and the NIC certificate live in `NicIrpOptions` as configuration, because NIC bumps
  the version segment and rotates the certificate.

## Verification

- C6.4 (IRN correctness): `tests/…Domain.Tests/Einvoicing/IrnFormulaTests.cs` against NIC's published
  example, plus `GstFinancialYearTests` for the year-boundary cases.
- C6.5 (no duplicate IRN on retry): `tests/…Infrastructure.Tests/Einvoicing/EinvoiceWorkflowTests.cs`
  — both a timeout that registered and one that did not.
- C6.6 (credentials never at rest in the clear): same file, including a scan of the whole SQLite file
  as text; `tests/…Application.Tests/Einvoicing/SensitiveTextTests.cs` for the scrubber itself.
- **C6.1 is not met.** The signed QR printing on an invoice needs a real portal response. NIC sandbox
  credentials are still outstanding (`specs/open-questions.md` #10). See
  `specs/evidence/phase-6-evidence.md`.
