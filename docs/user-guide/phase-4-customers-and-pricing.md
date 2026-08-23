# Phase 4 — Customers & Pricing

How to run the Phase 4 features from the screen: set up customer categories, customers and
their prices, bill with automatic pricing, and read what a customer owes you.

---

## 1. Customer categories (one-time setup)

**Masters → Customer categories**

Every customer belongs to exactly one category, and the category decides what they pay. Four
categories are ready on first run: **Retailer, Dealer, Wholesaler, Institutional**. Rename them
or add your own at any time — nothing is fixed.

- To add one: type a name (+ optional description) → **Save category**.
- A category in use cannot be deleted; **Deactivate** it instead (it disappears from new-customer
  pickers, history is untouched). Tick *Include inactive* to find and reactivate it later.

## 2. Prices per category

**Masters → Category price lists**

1. Pick a category at the top.
2. For each item choose how that category pays:
   - **FixedPrice** — a plain rupee selling price;
   - **MarginOnCost** — add a percent to the item's current cost;
   - **MarginOnSalePrice** — take a percent *off* the item's default sale price.
3. Type the rupee amount or percent in the value cell → **Add line** → **Save prices**.

The item's *default sale price* lives on the item master (Items screen) — it is both the MRP
basis for margin lines and the fallback price when no category line exists for an item.

## 3. Customers

**Masters → Customers**

Fill name + category (everything else optional): contact person, phone, email, GSTIN,
place-of-supply state code, billing/shipping addresses, credit limit (0 = no limit) and agreed
credit period in days. Saving a new customer quietly opens their **Sundry Debtors ledger** with
the same name — invoices post against it as always.

**Special prices**: after saving a customer, give them a personal price on specific items —
pick item + basis + value → **Add special price** → **Save special prices**. A special price
always wins over the category list for that customer.

## 4. Billing with automatic pricing

**Sales & Purchase → Documents**

1. Start a new quotation / sales order / tax invoice and pick the party ledger.
   Every staged line instantly fills with the right price — the **Priced from** column tells you
   which tier won: *customer*, *category*, or *item default*. New lines price themselves too.
2. Need a different rate? Edit the cell — the label turns amber to show it is an override, and
   the original auto-applied price is kept on the record for the edit log.
3. Saving a sales order or invoice checks the customer's credit limit first:
   - policy **Warn** asks "Continue anyway?" when the sale would cross the limit;
   - policy **Block** refuses outright.
4. Convert documents as usual — converted lines keep their quoted prices; nothing reprices
   behind your back.

### The override switch

**Masters → Sales settings**

- *Allow price overrides* ON (default): operators may edit auto-applied prices; every override
  is logged.
- OFF: rate cells lock on every sale screen, and even other tools cannot save a sale line whose
  price moved off the applied one.

## 5. What does a customer owe me?

**Reports → Customer statement**

Pick the customer and date range → **Load statement**:

- The statement lists every invoice (debit) and receipt (credit) with a running balance.
- **Open bills** shows each unpaid invoice with billed / paid-so-far / outstanding. Receipts
  pay the **oldest bill first** — the same habit as Tally.
- **Print / preview** gives an A4 statement; **Export CSV** writes the rows to a spreadsheet.

## Where things live on disk

| You did | It went here |
|---|---|
| Created a category/customer/price | Masters tables — every change lands in the edit log automatically |
| Overrode a price | The document line keeps applied price + override flag, visible in Audit |
| Loaded a statement | Read straight from the books — no stored balance can drift |

---

Developer-level notes per feature: [`docs/features/`](../features/) — see
`customer-categories.md`, `customer-master.md`, `customer-pricing-engine.md`,
`customer-statement-outstanding.md`.
