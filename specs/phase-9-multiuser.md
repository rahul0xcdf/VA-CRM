# Phase 9 — Multi-user / LAN

```yaml
Phase: 9
Title: Multi-user / LAN
Status: Draft
Source: project.md §4.8, §5, §10 (Phase 9)
Depends on: Phases 1–8
```

> **Draft.** Do not implement until `Status: Approved`.
> **Pull forward only if** concurrent multi-counter billing is a hard day-one requirement
> (`open-questions.md` #4). If the answer to that question is "yes", this phase must move
> before Phase 1 and the Phase 0 database decision is revisited.

## 1. Deliverables (project.md §10)

Migrate to SQL Server Express, connection/concurrency handling, user roles/permissions.

## 2. Scope notes

- **SQLite → SQL Server Express.** This is the moment the Phase 0 repository/unit-of-work abstraction
  pays for itself: the change should be a provider swap plus a migration set, not a rewrite. If it
  turns out to be a rewrite, that is a Phase 0 defect worth recording as an ADR.
- Provider selection stays config-driven (`Database:Provider`). Both providers remain buildable so
  a single-user install can still run on SQLite.
- **Concurrency:** optimistic concurrency tokens on every mutable entity; a lost-update must be
  detected and surfaced, never silently overwritten. Voucher numbering must not produce duplicates
  or gaps under concurrent billing — this needs a server-side allocation strategy, not a client-side
  read-max-then-increment.
- **Users, roles, permissions.** The pricing-override permission (Phase 4) and the audit trail
  (Phase 1) become genuinely load-bearing here — the audit trail's `user` field is only meaningful
  once there is more than one user.
- **Permissions are enforced in the domain/service layer, not by hiding UI.** A hidden button is not
  a permission.
- Auto-migrate-on-startup (acceptable in Phase 0–8 single-user) becomes an explicit, gated,
  single-operator step.

## 3. Checkpoint criteria (project.md §10)

| # | Criterion |
|---|---|
| C9.1 | Two clients transact concurrently without data corruption or lost updates |
| C9.2 | Permission rules are enforced server-side, not just hidden in the UI |

Additional:

| # | Criterion |
|---|---|
| C9.3 | Concurrent invoice creation from two clients produces no duplicate or skipped voucher numbers |
| C9.4 | All Phase 1–8 test suites pass unchanged against SQL Server Express |
| C9.5 | A permission-denied action is refused when invoked directly at the service layer, bypassing the UI |

## 4. Open questions to close before approval

- Day-one concurrency requirement — `open-questions.md` #4.
- How many concurrent billing counters/users?
- Windows auth or application accounts? Domain-joined machines?
- Which machine hosts SQL Server Express, and who backs it up?
