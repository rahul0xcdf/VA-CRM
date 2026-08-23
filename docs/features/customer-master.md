# Customer master

**Phase 4 · Customer & Pricing Engine** (`specs/phase-4-customer-pricing.md` §2)

## Feature

The customer master holds who buys and on what terms: contact details, GSTIN, place of supply,
billing/shipping addresses, a **credit limit** (0 = no limit), an agreed **credit period** in
days, and — decisively for pricing — the one **category** they price from. On top of the
category list a customer may carry per-item **special prices** that win over it (§4a).

Every customer links one-to-one to an ordinary ledger under **Sundry Debtors**, created
automatically with the customer inside the save. Invoices keep posting against ledgers exactly
as Phase 3 did; no downstream report changes. Deactivating a customer never touches their
ledger — receipts against money still owed keep working.

## User workflow (Masters → Customers)

1. Open **Customers**; search by name with the filter box (*"anand"* finds *Anand Traders*).
2. **New**: fill name + category (required); contact/GSTIN/place-of-supply/addresses/credit
   terms optional. Save — a first save reports *"with its own Sundry Debtors ledger"*.
3. **Special prices**: after saving, pick an item, pick a basis (**Fixed price** /
   **Margin on cost** / **Margin on MRP discount**), type rupees or percent, press
   **Add special price**, then **Save special prices**. Removing a saved row deletes it from the
   table on save.
4. Re-categorisation is ordinary maintenance: change the category and save.
5. **Deactivate/reactivate** as with every master.

## Developer notes

- Entity `Customer` (`Domain/Customers/Customer.cs`): unique `LedgerId`, exactly-2-char
  place-of-supply guard, GSTIN upper-invariant, `CreditLimitMinor` long minor units.
- Service `ICustomerService`: create stages the debtor ledger in the caller's UoW transaction;
  `SavedCustomer.CreatedLedger` reports whether one appeared.
- Special prices: entity `CustomerSpecialPrice` (unique per customer+item), service
  `IPriceListService.SetCustomerSpecialPriceAsync` — at most one line per item per customer;
  basis/value pairing guarded in the domain.
- The documents screen maps ledger → customer through this 1:1 link to auto-price lines (see
  [customer-pricing-engine](customer-pricing-engine.md)).

## Verification

- Domain tests: constructor guards, GSTIN normalisation, place-of-supply shape.
- Workflow test: creating a customer auto-creates its Sundry Debtors ledger in one save
  (`tests/VinayakaAgencies.Infrastructure.Tests/Customers/CustomerWorkflowTests.cs`).
