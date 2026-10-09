# Phase 9 — Multi-user / LAN

```yaml
Phase: 9
Title: Multi-user / LAN
Status: Approved
Source: project.md §4.8, §5, §10 (Phase 9)
Depends on: Phases 1–8
```

> **Approved 9 Oct 2026** by the project owner, with the four §4 questions closed as recorded in
> §4a. `open-questions.md` #4 is closed by circumstance rather than by answer: Phases 1–8 shipped
> single-user, so the pull-forward this spec warned about never came due.

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

## 4. Open questions — closed 9 Oct 2026

- ~~Day-one concurrency requirement — `open-questions.md` #4.~~ → closed by circumstance; see the
  approval note above
- ~~How many concurrent billing counters/users?~~ → D1
- ~~Windows auth or application accounts? Domain-joined machines?~~ → D2
- ~~Which machine hosts SQL Server Express, and who backs it up?~~ → D3
- Role set at launch (raised at approval, since C9.2/C9.5 cannot be written without it) → D4

## 4a. Decisions (9 Oct 2026)

### D1 — Two to three counters: a billing counter plus the office

Small enough that connection pooling needs no special handling and the Express edition's 1 GB RAM
ceiling is not a design constraint. Two things still have to be built properly rather than
approximated:

**Voucher numbering must allocate server-side.** §2 already forbids read-max-then-increment, and at
two counters that is not a theoretical concern — two operators starting an invoice within the same
second is an ordinary Tuesday. The allocation has to happen inside the database, under the same
transaction that writes the voucher, so the number a counter is handed cannot be handed to anyone
else. Note the interaction with `open-questions.md` #11, already **Confirmed**: gaps are *blocked*,
which rules out the easy answer of a SQL Server `SEQUENCE` (sequences deliberately lose numbers on
rollback and on cache eviction). The allocator is a per-series counter row taken under an update lock.

**Every mutable entity carries a concurrency token.** A lost update at two counters is quiet and
permanent: two people open the same customer, both save, the second silently erases the first. The
token makes that an error the operator sees. SQL Server gets `rowversion`; SQLite has no equivalent,
so the token is a domain-managed `Guid`/counter column that both providers can enforce, which keeps
C9.4's "unchanged test suites" honest.

### D2 — Application accounts, not Windows authentication

Username and password held by the application, hashed. The deciding reason is the audit trail rather
than convenience: non-negotiable #3 requires every create/alter/delete to name a user, and that field
is only meaningful if the identity is per-person. A small shop on a workgroup typically has one
Windows login per machine shared by whoever is sitting at it, under which Windows authentication
would record the *machine's* habitual user for every action — technically a name, practically
useless, and worse than useless in a dispute because it looks authoritative.

Consequences: a login step at startup, a users table with hashed credentials (no reversible storage,
and the DPAPI credential protector from Phase 6 is **not** the right tool here — it protects
secrets this application must replay, whereas a password must never be recoverable), and
`ICurrentUser` becomes a real per-session identity instead of the Phase 0 stub. No domain
requirement, which also means nothing to configure at install beyond the first account.

### D3 — SQL Server Express on the main billing PC

No extra hardware. Two operational facts follow and belong in writing now rather than being
discovered at go-live:

- **That PC must be on whenever anyone else wants to bill.** The other counters are clients; if the
  host is off or asleep, they cannot invoice at all. Power settings on that machine become a
  deployment step, not a preference.
- **Whoever runs that PC is the custodian of the backup.** Phase 10 owns backup/restore, but the
  decision of *where the database lives* is what makes a person responsible for it, and that person
  should know.

Nothing in the code depends on this: the provider and connection string stay configuration
(`Database:Provider`), exactly as §2 requires, and both providers remain buildable.

### D4 — Two roles at launch: Owner and Operator

Owner has full access. Operator bills and takes receipts, and is refused price override, voucher
cancellation and configuration.

Roles are **data rows, not an enum** — the same stance non-negotiable #6 takes on tax
configuration — so adding an Accountant role later is master data rather than a rebuild. What is
*not* deferred is the enforcement point: permissions are checked in the service layer (§2), so
C9.5 holds for a role that does not exist yet.

This supersedes the Phase 4 `Sales.AllowPriceOverride` setting, which that phase recorded as a
stand-in "until Phase 9 roles exist". The setting stays readable so an existing install does not
change behaviour on upgrade, but the permission is now the authority.

## 4b. Changes beyond spec

Recorded here as they are made, per the working agreement.
