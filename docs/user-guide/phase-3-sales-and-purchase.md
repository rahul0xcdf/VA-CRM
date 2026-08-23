# User Guide — Sales & Purchase (Phase 3)

*How to run your day-to-day selling and buying in Vinayaka Agencies: quotations, orders,
deliveries, invoices — and printing them.*

This guide is written for the person using the software, not the programmer. Developer notes live
in [`docs/features/`](../features/) if you ever need them.

---

## 1. First launch

The first time the app starts it asks for your **company details** — legal name, trade name and
the GST state code. These appear on every printed document and filed return, so they are asked
for once and never guessed. After you save:

- the chart of accounts is created automatically,
- numbering series are set up for every document kind:
  `QTN/` quotation · `SO/` sales order · `DC/` delivery challan · `INV/` tax invoice ·
  `PO/` purchase order · `GRN/` goods receipt · `BILL/` purchase invoice,
- two ready-made **tax invoice print designs** are installed ("Detailed" and "Compact").

You only do this once.

## 2. Before your first sale

Three things must exist (all from earlier screens):

| What | Where | Example |
|---|---|---|
| The customer's ledger | Accounting → Ledgers, under **Sundry Debtors** | "Ram Motors" |
| The items you sell | Inventory → Items | "Cisno Pump 1 HP" |
| Stock in the godown | Inventory → Stock vouchers (Receipt) | 10 pumps @ ₹180 |

A quotation needs only the customer ledger and items — no stock needed yet.

## 3. Selling — the normal flow

**Sales & Purchase → Documents**

### Step by step: quotation → order → invoice

1. **New document**, pick kind **Quotation**, pick the date and the customer.
2. Type in **Find item**, press **Search**, pick the item, enter quantity and rate
   (discount % optional). Press **Add line**. Repeat for more items.
3. Optional: **Your ref** = the customer's own reference number ("your order no."), printed back
   on the document; **Narration** = a free note for yourselves.
4. **Save draft.** The quotation gets its number immediately — e.g. `QTN/0001`.
   A quotation affects nothing in the books and no stock — it is a piece of paper, nothing more.
5. When the customer says yes: load the quotation (**Load selected ↓**), choose
   **Convert into: Sales Order**, **Convert**. Everything — items, rates, discounts, terms —
   carries over; you re-key nothing. The order gets `SO/0001`.
6. When delivering: convert the same way into **Tax Invoice**. Because an invoice moves stock, it
   needs a godown — pick one under **godown:** before converting. The invoice draft carries no
   number yet; that is deliberate.
7. Check the draft, press **Validate invoice** to see anything wrong or worth knowing, then
   **Post invoice (atomic)**. Only now does it get its number (`INV/0001`) and move the world:
   stock leaves the godown and the books record the sale — together, completely, or not at all.
8. **Print / preview** shows the invoice as it will print (see §6).

> **In a hurry?** Skip the quotation and order entirely — raise a Tax Invoice directly from
> scratch and post it. Every stage is optional except the invoice itself.

### Partial deliveries

Customer orders 10, you have only 6?

1. Convert the order into a Tax Invoice (it arrives carrying all 10).
2. Edit the line down to 6 and **Save draft**, then post as usual.
3. Open **Order outstanding**: the order shows *ordered 10, pending 4*. It stays there until the
   last unit goes out. Convert again later, trim to 4, post — done; the order closes itself as
   *fulfilled*.

### Serial-numbered and kit items

- **Serial-tracked items** (pumps sold as identifiable units): when adding the line you list the
  exact serial numbers going out — one per unit, from the godown named on the line. A serial can
  leave once ever; the books remember exactly which unit went to which invoice.
- **Kits** (e.g. "pump set" made of pump + cable): sell the kit line; the components leave stock
  individually at their own costs automatically. If a component is itself serial-tracked, sell
  that component directly instead — the system will not guess which unit to take.

### Something went wrong after posting?

Load the issued invoice and press **Reverse issued invoice**. The books and stock go back exactly
as they were, serials return home, and the invoice keeps its number (spent numbers are never
reused). If the invoice came from an order, whatever it had delivered comes back as *pending* on
the outstanding report.

## 4. Buying — the mirror flow

Same screen, purchase kinds: **Purchase Order → Goods Receipt → Purchase Invoice**.

- The **goods receipt** (`GRN/…`) records what physically arrived. It moves nothing — it is a
  note against the order so you know what's still expected.
- The **purchase invoice** (`BILL/…`) is what actually receives the goods: stock enters the
  godown at what you paid, and the supplier's ledger is credited with the full bill.
  Serials received here become available to sell; batch-tracked goods must carry their batch.

## 5. Order outstanding report

**Sales & Purchase → Order outstanding** — one row per line still awaiting delivery/receipt:

> Order no. · Date · Party · Item · Ordered · **Pending** · Rate · Pending value

Tick **Show purchase orders instead** for the buying side. An order disappears from this report
only when everything ordered has gone out (or come in); reversing an invoice puts its quantity
back.

## 6. Printing — make documents look yours

**Sales & Purchase → Print templates**

Your documents print from editable designs. Two are installed for tax invoices; you can edit
either:

1. Pick the template in the list, **Load selected ↓**.
2. Change what you like: title, logo image path, header lines (address / GSTIN / bank details,
   one per line), footer note, terms & conditions, which columns the item grid shows, and the
   **copy labels** (`Original for Recipient;Duplicate for Transporter;Triplicate for Supplier`
   by default — each prints as its own page).
3. **Save as new version.** Your old design stays in the version history below; nothing is lost.
4. **Activate / retire** chooses whether a design is in the running for the default.

Whichever active template has the **lowest priority number** prints by default for that document
kind (today: "Detailed", priority 10). To make another one default, retire the current favourite
or create a new template with a lower priority. No rebuild, no developer — changes show the next
time you print.

## 7. What happens inside (the short honest version)

| You do | Stock | Books |
|---|---|---|
| Quotation | nothing | nothing |
| Sales/Purchase order | nothing | nothing |
| Delivery challan / goods receipt | nothing (paper trail only) | nothing |
| **Tax invoice posts** | goods leave the godown at cost | customer debited, Sales credited, Cost of Goods Sold debited, Stock-in-Hand credited |
| **Purchase invoice posts** | goods enter the godown at cost | Stock-in-Hand debited, supplier credited |

Stock therefore moves exactly twice per transaction — once in, once out — and the stock value on
the books always matches the physical ledger. If a sale would take stock below zero the app warns
you and asks for confirmation before continuing.

Every create, change and delete of any document lands in the audit log with who and when. That
cannot be switched off.

## 8. Good-to-know rules

- Numbers are handed out in order per document kind and **never reused** — gaps mean something
  was cancelled, not lost.
- Draft invoices (not yet posted) can be edited freely. Posted ones cannot — reverse and re-do.
- Quotations/orders/challans/goods receipts can be deleted while they have had no effect; once
  converted, delete the successor first.
- A document that would name a godown on a quotation/order refuses — those kinds don't touch
  stock, so a godown would be a lie.
- Dates follow the Indian financial year for numbering resets (April–March).
