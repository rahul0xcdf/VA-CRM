# Banking: payment instruments & cheque printing

**Phase 7a · TDS/TCS, Payroll, Banking** (`specs/phase-7-tds-payroll-banking.md` §2, §4a D4)

## Feature

Every payment and receipt records **how it actually settled** — the cheque, the UTR, the UPI
transaction id — and a cheque can be printed onto a real leaf from a layout the owner adjusts
themselves.

- **Nine settlement modes**: cash, cheque, demand draft, NEFT, RTGS, IMPS, UPI, card, bank transfer.
  What each mode requires is enforced in the domain, not the screen: a cheque needs its number (it
  is what the bank, the reconciliation and the printed leaf all identify it by), every electronic
  rail needs the reference the bank issued, and cash needs neither and must not be made to invent
  one. Changing a cheque to a NEFT drops the stale cheque number rather than leaving it behind to
  reconcile against an instrument that was never written.
- **Cheque printing reuses the Phase 3 print-template engine** (§4a D4) — there is no second
  template system. A cheque layout is a `PrintTemplate` of kind `Cheque` whose layout carries
  positioned fields in millimetres. The owner prints onto a real cheque, measures what is out, edits
  the numbers and saves; that appends a template version like any other and **nothing is rebuilt**
  (C7.8).
- **One adjustable CTS-2010 layout is seeded and no bank is named.** CTS-2010 standardised Indian
  cheque dimensions, so one layout covers most banks with millimetre tweaks. Where a bank genuinely
  differs it is another template row resolved by priority — the same shape as the two seeded
  tax-invoice templates.
- **The amount in words is produced once, in the domain.** It is the legally operative figure —
  where words and figures disagree on a cheque, the words are what the bank honours — so it is
  arithmetic with a rule, tested without a window. Indian grouping throughout: ₹12,34,567 is
  "Twelve Lakh Thirty Four Thousand Five Hundred Sixty Seven", never "One Million".
- Correcting a mistyped cheque number **does not supersede the voucher**. The number is a fact about
  the paper, not about the accounting entry; the instrument is audit-stamped, so the correction is
  edit-logged with before and after values without writing a new revision of journal lines that did
  not change.

## User workflow

1. **TDS & Banking → Payments & receipts** — pick the party and the bank account, enter the amount,
   choose the mode. The identifier fields that appear are the ones that mode actually has. The
   amount in words appears as you type, exactly as it will print.
2. `F5` / `F6` switch between payment and receipt (TallyPrime's own keys); `Ctrl+A` posts.
3. The register below lists the period's settlements. Select a cheque and press `Ctrl+P` — the
   preview opens through the default cheque layout and the row is marked printed, so a re-print is
   a deliberate act.
4. **Masters → Print templates** — pick the `Cheque` kind to adjust field positions against a real
   leaf. Print at 100% scale; do not fit to page.

## Developer notes

- Domain: `PaymentMode`, `PaymentInstrument`, `IndianAmountInWords` (`Domain/Banking/`).
- Application: `IBankingService` (`Application/Banking/`), and `ChequeLayoutDefaults` for the field
  palette and the seeded CTS-2010 measurements.
- Presentation: `ChequePrinter` builds a `FixedDocument` on a `Canvas` — absolute positioning, not
  the flowing layout an invoice uses, because a cheque does not grow with its content. The page is
  otherwise blank: the leaf is pre-printed, so a border or background would print over the bank's
  own artwork.
- `PrintTemplateLayout` gained an optional `Fields` list. It is nullable because templates written
  before Phase 7 have no such property in their stored JSON; `PlacedFields` normalises it so a
  renderer needs no null check and every existing revision stays readable exactly as saved.
- **`PrintTemplate.DocumentKind` changed type** from `TradeDocumentKind` to the new
  `PrintDocumentKind`. A cheque is not a trade document, and widening the trade enum would have put
  a bank instrument inside an enum documented as "the seven documents of the sales and purchase
  pipelines". The column stores enum *names* and the seven trade members keep theirs, so **no data
  migration** was needed. `TradeDocumentPrinting.ToPrintKind()` is the seam.
- The instrument hangs off the voucher, not the revision — see the remarks on `PaymentInstrument`.

## Verification

- `IndianAmountInWordsTests` — 32 cases: the irregular teens, lakh and crore grouping, "One Hundred
  Crore" above 10^9, paise, the rounding that carries into the rupee, and the 3-then-2 digit
  grouping produced from an explicit format rather than an `en-IN` culture lookup that may not be
  installed.
- `BankingWorkflowTests` — payment and receipt posting balanced against the journal, the four
  electronic modes keeping their reference and dropping any instrument number, a missing reference
  refused, the seeded cheque template resolving as default, **C7.8** (repositioning a layout appends
  a version and takes effect immediately, with version 1 still readable), cheque data wording,
  print marking, instrument correction leaving the journal untouched, and the register.
