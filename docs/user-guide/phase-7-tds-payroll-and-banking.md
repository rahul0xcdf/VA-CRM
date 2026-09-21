# Phase 7 — TDS, payroll and banking

For the person using the software. Seven screens, in two groups on the Home menu: **TDS & Banking**
and **Payroll**.

> **Nothing is set up for you.** No section, no rate, no threshold, no PF percentage, no
> professional tax slab. That is deliberate. Every one of those figures changes by government
> notification, and a figure the software filled in for you is a figure nobody owns when it moves.
> You enter what your notification and your accountant say, once, and the software uses it from then
> on.

---

## Part 1 — TDS and TCS

### Setting up a section (TDS & Banking → TDS/TCS sections)

You only do this for the sections you actually deduct under. For most trading businesses that is
one or two.

1. **Kind** — TDS if you are withholding it from what you pay, TCS if you are adding it to what you
   charge.
2. **Code** — type it exactly as it is written and filed.
3. **Name** — in your own words. "Payments to contractors" is better than the statutory wording,
   because you are the one who will read it.
4. **2025 payment code** — leave blank for now. When the Income Tax Act 2025 renumbering takes
   effect on 1 April 2026, the new numeric payment code goes here. You will not need a software
   update for it: you add the new sections as ordinary rows and retire the old ones, and both work
   side by side across the changeover.
5. **Save section**.

Then attach a rate. A section with no rate deducts nothing — the software will tell you so rather
than silently letting it slide.

6. **Payee class** — most sections charge everyone the same; leave it on *Any*. Where the statute
   charges an individual or HUF one rate and everyone else another, add two rows, one per class.
7. **Rate %** — type it the way the notification writes it: `1`, `2`, `0.1`.
8. **No-PAN %** — the higher rate where the payee has given you no PAN. Leave blank if the section
   has no such uplift.
9. **Per payment** and **Per year** — the two thresholds. Leave one blank if the section has only
   the other. Leave *both* blank to deduct from the first rupee.
10. **From** — the date this rate starts. **Add rate**.

**Changing a rate later** is the same three fields and a new date. The old rate closes itself the
day before, so payments made under it keep their own figures and an old return stays reproducible.
You will see both rows in the list, one with an end date and one marked "current". That is correct
and it is the point.

### The two thresholds, and why there are two

Say a section deducts when a single payment exceeds ₹30,000 **or** when the year's payments to that
contractor exceed ₹1,00,000 — whichever comes first. Both go in. What then happens:

- One ₹50,000 bill in April — deducted on, because it is over the per-payment limit.
- Three ₹40,000 bills — nothing on the first two. The third carries the year past ₹1,00,000, so the
  software deducts on **all ₹1,20,000** on that third bill, catching up on the two it let through.
  That is what the statute requires, and it is the part most software gets wrong.
- The fourth bill is taxed on itself alone. The catch-up happens once.

### Recording who you deduct from (TDS & Banking → TDS/TCS setup)

Before you can deduct from a party, the software needs two facts about them.

1. Pick the **party** ledger.
2. **Class of person** — individual/HUF, or anything else (company, firm, LLP).
3. **PAN** — if you hold one. **If you do not, leave it blank and mean it**: no PAN on file is what
   triggers the higher rate, so a made-up entry here under-deducts.
4. **Deduct from this party** — untick for a party you have decided is outside deduction. It is an
   explicit switch so that turning it off is a recorded decision rather than a gap in setup.
5. **They deduct on their purchases** — see below.
6. **Save payee**.

### 194Q and 206C(1H) — the mutual exclusion

Both apply only above a turnover limit. **You declare whether they apply; the software will not
guess.** Turnover as the books add it up is not necessarily turnover as the statute defines it, and
an obligation that switches itself on without telling you is worse than one you declared.

On the same screen, tick **we deduct on purchases of goods** or **we collect on sales of goods**, and
pick the section each resolves through. With both unticked, neither ever runs.

The statute makes the two mutually exclusive on one transaction: where the buyer deducts, the seller
does not also collect. The seller cannot see the buyer's books — the only thing you have is the
buyer's written declaration. So when a customer gives you one, tick **they deduct on their
purchases** on their payee profile, and the software stops collecting from them. Until they do, the
default is to collect, because over-collecting is refundable through their return while
under-collecting is your liability with interest.

### Lower and nil deduction certificates

A Section 197 certificate is per party *and* per section, with its own validity dates. Record the
number, the authorised rate (zero for a nil certificate) and the window. From then on:

- Payments inside the window are deducted at the certificate rate.
- A payment the day after expiry goes back to the full rate, with nobody having to remember to
  switch anything off.
- A nil certificate still records a deduction — of zero, with the certificate number against it.
  That has to appear on the return, and it is a different fact from "nobody set this up".
- If the certificate is capped at an amount, the software splits the payment: the first part at the
  certificate rate, the balance at the normal one.

### Checking and paying (TDS & Banking → TDS/TCS register)

Pick a period. The list shows every deduction with the rate **that was applied at the time** — not
the rate the master holds today, so an old month reads back the way it was filed.

Press **Ctrl+Enter** for the challan summary: what to deposit, per section, with how many parties
you deducted from. That is the figure you pay.

---

## Part 2 — Payments, receipts and cheques

### Entering a payment (TDS & Banking → Payments & receipts)

1. Date, **party**, and the **bank or cash account** it moves through.
2. **Amount** — the words appear underneath as you type, exactly as they will print on a cheque.
3. **Mode**. Only the fields that mode actually has will appear: a cheque or DD asks for its number,
   every electronic transfer asks for the reference the bank gave you (UTR, UPI transaction id), and
   cash asks for neither.
4. **Instrument date** — the date written on the cheque, which is often not today. A post-dated
   cheque is the ordinary case, and this is the date reconciliation works from.
5. `F5` for a payment, `F6` for a receipt. `Ctrl+A` posts.

If you change a cheque to a NEFT after typing the cheque number, the number is dropped. That is
intentional — a leftover number would reconcile against a cheque you never wrote.

### Printing a cheque

Select the row and press **Ctrl+P**. A preview opens; print it **at 100% scale, not "fit to page"**,
with a blank cheque in the printer. The row is then marked printed, so a second print is something
you choose rather than something you do twice by accident.

**The first cheque will be slightly out.** Every bank's leaf is a little different. Print one onto
plain paper, hold it against a real cheque, and see what has moved.

To fix it: **Masters → Print templates**, pick the **Cheque** kind, and adjust the millimetre
positions. Save. The next cheque prints with the new positions — there is no software update
involved, and the previous version of the layout stays on file. Repeat until it lands. You will
usually get there in two goes.

You can also turn the **A/C Payee** crossing off, for banks whose leaves come pre-crossed, without
losing its measured position.

---

## Part 3 — Payroll

### Deciding what applies (Payroll → Payroll setup)

Start here, and start honestly.

**Provident Fund**, **Employees' State Insurance** and **Professional Tax** each have their own
tick box, and all three start off. PF is mandatory above twenty employees and ESI above ten — but
those numbers are themselves statutory and have changed, so the software does not count your staff
and decide. You tick what applies.

**With all three off, payroll still works.** You can run salary vouchers, pay people and post to the
books; there is simply no statutory line on anybody's slip, no rate table consulted, and nothing on
the remittance report. If you are below the thresholds today, that is exactly right, and switching
one on later is this tick box and nothing else.

Then:

1. **Pay heads** — one per line that can appear on a slip. Name it, say whether it is an earning, a
   deduction or an **employer contribution**, choose how it is computed, and pick the ledger it
   posts to.
   - Call your basic pay head **Basic**. Any head computed as a percentage of basic needs to know
     which one that is.
   - An employer contribution is the company's own share — the employer half of PF. It never comes
     out of the employee's pay, and it must not be entered as a deduction.
   - Set the basis to **Statutory** for PF, ESI and PT heads, and pick the statute. Those heads take
     no figure from you; the rate and slab rows produce them.
2. **Contribution rate** — per statute, with its dates.
   - **Wage cap** is a ceiling on the *base*: someone earning above it contributes on the capped
     amount, not on nothing. This is how PF works.
   - **Exclude above** takes the employee *out of the scheme entirely* above that wage. This is how
     ESI works.
   - Most statutes use one or the other. Leave the one that does not apply blank.
3. **Professional tax slabs** — one row per band: the wage it starts at, the wage it stops below,
   and the flat monthly tax inside it. Leave "below" blank on the top band. Note the bands are
   *from-inclusive, below-exclusive*, so `15,000 to 25,000` then `25,000 and above` is entered as
   `15000 / 25000` and `25000 / blank`, and a wage of exactly ₹25,000 falls in the second.

### The people (Payroll → Employees)

Code, name, joining date, and the statutory identifiers you hold. **PAN, UAN and ESI number can all
be left blank** — a new joiner genuinely has none of them on their first payday, and the software
will not refuse a correct entry for it.

When someone leaves, set their **last day** rather than deactivating them. Their final part-month
still has to be paid, their PF still has to be remitted, and their old pay slips still have to be
readable. They drop out of the run automatically from the month after.

Then the **salary structure**: pick a pay head, enter an amount *or* a percentage, and the date it
starts from. **Add line.**

A raise is a new line from its own date. The previous one closes itself the day before, so last
month's pay slip does not change retrospectively. The grid shows the whole history, which is what
you want when somebody asks why March was different.

### Running the month (Payroll → Payroll run)

1. Pick the **month ending** date and press **Compute**. Nothing is posted yet.
2. Read the figures. Select an employee to see their slip line by line on the right.
3. **Read the warnings** if any appear. The one that matters most is "PF is switched on but no
   contribution rate is in force for this month" — that produces zero either way, and the only
   question is whether you find out now or when the remittance is short.
4. **Ctrl+Enter** for the challan summary: what to remit per statute, employee share and employer
   share separately, and the total.
5. **Post** when the figures are right. That writes the salary voucher and its journal entry
   together.

A month can only be posted once. If you need to redo it, cancel the posted voucher first.

---

## What is *not* here

**Form 24Q and Form 16 are not built.** Those are returns and certificates, they need the TDS
return machinery finished first, and they are deferred on the record to a later phase. You can
produce every figure they need from the register and the challan summary in the meantime.
