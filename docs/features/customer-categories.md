# Customer categories

**Phase 4 · Customer & Pricing Engine** (`specs/phase-4-customer-pricing.md` §2)

## Feature

Every customer belongs to exactly one customer category, and each category carries the price
list its customers price from. Categories are **master data rows**, not a code enum
(`open-questions.md` #2) — freely renamable, extendable and deactivatable at any time.

On first run the provisioner seeds four categories: **Retailer / Dealer / Wholesaler /
Institutional** (approved as the placeholder set, 23 Aug 2026). They may be renamed or added to
from the screen immediately; nothing in code depends on them.

## User workflow (Masters → Customer categories)

1. Open **Customer categories**. The list shows name, description, how many customers sit in the
   category, and whether it is active.
2. **New**: type a name (+ optional description), press **Save category**.
3. **Rename/re-describe**: select the row, edit, press **Save category**.
4. **Deactivate**: hides the category from new-customer pickers without disturbing history;
   reactivatable from the same screen with *Include inactive* ticked.
5. **Delete** is refused while any customer or price line references the category — deactivate
   instead, exactly like every other master the books reference.

## Developer notes

- Entity `CustomerCategory` (`Domain/Customers/`); unique normalised name per company, same
  filtered-index style as `Ledger`.
- Service `ICustomerCategoryService` (`Application/Customers/CustomerCategoryService.cs`);
  delete-refusal throws `InvalidOperationException`, which the viewmodel surfaces in Status.
- Seeding: `CompanyProvisioningService.EnsureCustomerSeedAsync` (idempotent AnyAsync-then-add),
  constants in `Application/Customers/CustomerSeedData.cs`; wired at startup next to the pipeline
  seed.
- All create/alter of categories lands in the audit trail automatically via
  `AppDbContext.SaveChangesAsync` capture.

## Verification

- Domain guard tests cover constructor validation; `NoBusinessLogicGuardTests` pins the approved
  entity set including `CustomerCategory`.
- Workflow tests: deactivation refused while referenced; four seeded categories present on a
  fresh company (`tests/VinayakaAgencies.Infrastructure.Tests/Customers/`).
