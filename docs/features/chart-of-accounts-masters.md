# Feature — Chart of Accounts Masters (groups & ledgers)

**Status: implemented and verified, Phase 1.**

## What & why

The chart of accounts is the skeleton every posting hangs on. This feature lets the owner maintain
the two master types from spec §4: **account groups** (classification nodes, seeded 15 primary +
13 sub-groups) and **ledgers** (postable accounts). System rows are visible but locked against
deletion/re-parenting; deactivation is always available and preserves history.

## User workflow

1. Open **Masters → Account groups**. Press *Refresh* — groups appear in tree order (indentation =
   depth), system ones flagged.
2. To add a group: type a name, pick its parent, press **Add group**. Nature is inherited from the
   parent, never chosen.
3. To rename: select a row, type the new name, press **Rename selected**.
4. Open **Masters → Ledgers**, press *Refresh*. Search by name/alias or filter by group;
   double-click a row to load it into the editor. (The *Refresh* control was missing entirely until
   Oct 2026 — `RefreshCommand` was bound to nothing, so the list could never populate and the empty
   state misleadingly read "No ledger matches this filter". See `phase-1-evidence.md` §6.5.)
5. Create with **New** (needs a unique name within the company), change with **Save changes**,
   retire with **Deactivate**, restore with **Reactivate**, remove with **Delete** — deletion only
   succeeds when nothing was ever posted to the ledger.
6. Refusals (duplicate name, locked system row, posted-to ledger) appear in the status line.

## Developer notes

- Services: `IChartOfAccountsService` (`ChartOfAccountsService.cs`), contracts in
  `Application/Accounting/ChartOfAccountsContracts.cs`.
- Domain rules live in `AccountGroup` / `Ledger` entities; the service adds company scoping via
  `ICompanyContext`, uniqueness checks, and the "no journal lines" delete guard.
- Every write goes through `IUnitOfWork.SaveChangesAsync`, so each change lands in the edit log with
  full before/after images (spec §11).
- Bank ledgers are *derived* (`ListBankLedgersAsync`) from group paths of `Bank Accounts` /
  `Bank OD A/c` — never stored. Those group names are matched **through `NameNormaliser`**; a
  display-cased ordinal comparison matches nothing and silently empties the list.
- **`MoveGroupAsync` is the only sanctioned way to re-parent a group**, and it rebuilds the
  materialised path of the moved node and every descendant in the same unit of work. `MoveTo` and
  `SetMaterialisedPath` on the entity do *not* do this on their own — both say so, and for a long
  time nothing honoured them. Never call `MoveTo` directly from a new caller; the path is what
  every balance roll-up range-scans, so a stale subtree rolls up under its old parent with no error
  anywhere. A move across natures is refused outright: it would relocate balances between the P&L
  and the balance sheet without a voucher being posted.
- No screen re-parents a group. Phase 1 specifies no such UI; the service method exists so the
  invariant (C1.14) has an implementation to be asserted against.

## Verification

- `CompanyProvisioningTests` — seed tables match spec §4.3/§4.4/§4.5 line by line.
- Workflow tests exercise create/rename/deactivate/delete paths end to end against SQLite
  (`PostingWorkflowTests.Post_ToInactiveOrDerivedLedger_IsRefused`,
  `EditLog_ListForEntity_ShowsBeforeAndAfterImagesOfALedgerChange`).
- Manual smoke run: app starts, screens populate, duplicate-name refusal surfaces in status.
