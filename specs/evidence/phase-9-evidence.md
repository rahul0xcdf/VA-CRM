# Phase 9 Evidence Report — Multi-user / LAN

Spec: `specs/phase-9-multiuser.md` (Approved 9 Oct 2026). Filed 9 Oct 2026.

> **Read §4 first.** Four of the five checkpoints are met; **C9.4 is not met and the SQL Server
> Express provider is not written.** That was a deliberate stop, not an omission, and §4 says why.

---

## 1. Checkpoint results

| # | Criterion | Result | Evidence |
|---|---|---|---|
| C9.1 | Two clients transact concurrently without data corruption or lost updates | **Partially met** | The mechanism is proved: `ConcurrencyTokenTests` — a token on every mutable entity and on nothing else, rotating on every write; two scopes editing the row they both loaded end with the second refused and **the first write surviving**; and a scope saving the same entity twice is not mistaken for a conflict. Two scopes on one SQLite file is a real second connection with its own change tracker. Not proved as written — "two clients" against SQL Server Express needs an instance (#24) |
| C9.2 | Permission rules are enforced server-side, not just hidden in the UI | **Met** | `PermissionEnforcementTests` (19) and `Phase9ScreenTests` (11). Every `IUserAdminService` member, voucher post/alter/cancel, and the sales price override are gated in the service. The Users screen is reachable from the Alt+G palette whoever is signed in, so the Gateway hiding it was never the protection |
| C9.3 | Concurrent invoice creation from two clients produces no duplicate or skipped voucher numbers | **Partially met** | `VoucherNumberAllocationTests` — consecutive numbers with no gap, per-financial-year counting, a genuine eight-way race on one database file issuing no number twice, and the stale-counter case a single-user build could never show. Not proved as written, same reason as C9.1 |
| C9.4 | All Phase 1–8 test suites pass unchanged against SQL Server Express | **Not met** | The provider is not written. §4 below, and `phase-9-multiuser.md` §4c |
| C9.5 | A permission-denied action is refused when invoked directly at the service layer, bypassing the UI | **Met** | `CancelAsync_OperatorSignedIn_IsRefusedByTheServiceNotTheScreen` — an Operator calls `IVoucherPostingService.CancelAsync` straight off the container with no window in the process. The voucher id is deliberately one that does not exist, so a `PermissionDeniedException` rather than a "no such voucher" proves the gate runs *before* the service reads anything |

---

## 2. Test run

```
.\build.ps1
=== Done (build + test succeeded) ===

VinayakaAgencies.App.Tests             190 passed, 0 failed
VinayakaAgencies.Application.Tests     114 passed, 0 failed
VinayakaAgencies.Domain.Tests          485 passed, 0 failed
VinayakaAgencies.Infrastructure.Tests  332 passed, 0 failed
                                     -----
                                     1,121 passed, 0 failed, 0 skipped
```

Build: 0 warnings, 0 errors. Warnings are errors in this solution, so that is a hard gate rather
than a tidy result.

**71 of those tests are new**, in seven classes:

| Class | Tests | Carries |
|---|---|---|
| `PermissionEnforcementTests` | 19 | C9.2, C9.5 |
| `Pbkdf2PasswordHasherTests` | 13 | §4a D2 |
| `Phase9ScreenTests` | 11 | C9.2 from the screen's side |
| `ConcurrencyTokenTests` | 8 | C9.1 |
| `VoucherNumberAllocationTests` | 8 | C9.3 |
| `GatedMigrationTests` | 6 | §2's gated upgrade |
| `SqlServerPortabilityTests` | 3 | §2's "swap, not a rewrite" question |
| `ScreenViewLoadTests` (+1) | 1 | every window constructs |
| `ApplicationServiceCollectionExtensionsTests` (+2) | 2 | the `ICurrentUser` registration change |

### The Phase 1–8 suites are unchanged

Three tests of theirs needed editing and each one was a genuine consequence rather than a tolerance:

- `NoBusinessLogicGuardTests.EntityTypesInDomain_MatchExactlyThePhase0Through9ApprovedSet` — the
  approved entity set is a fixed list on purpose ("a new entry here is a spec change, not a test
  update to follow the code"), so `AppUser`, `AppRole` and `RolePermissionGrant` were added to it
  with the spec reference.
- `ApplicationServiceCollectionExtensionsTests` — asserted that `ICurrentUser` resolves to
  `EnvironmentCurrentUser`. §4a D2 changes exactly that, so the test was rewritten to assert the new
  shape, plus the property that matters: with nobody signed in, `ICurrentUser` still names somebody.
- `Model_ConfiguresEveryRelationshipAsRestrict` — see §5.

Everything else passes untouched, which is the point of the bootstrap rule in §3.

---

## 3. The bootstrap rule, and why it is in the evidence

**An install with no accounts grants every permission to everybody.**

This is the single most important thing for the owner to understand about this phase, so it is
asserted explicitly (`HasPermissionAsync_NoAccountsExist_GrantsEverything`) rather than left as an
accident of the implementation.

It exists because enforcement has to begin somewhere: refusing everything before the first account
exists would mean the first account could never be created. It is also why 1,050 Phase 1–8 tests are
unaffected — their databases have no users, so they behave exactly as before.

It is equally the one state in which none of this protects anything. Until the owner creates the
first account at the sign-in window, Phase 9's security is inert. That is not a defect, but it is
a fact the walkthrough should confirm out loud.

---

## 4. C9.4 — not met, and why the provider was not written

The spec's headline deliverable is "migrate to SQL Server Express". It is not done. This was a
decision, and the owner should weigh it.

**It cannot be verified here at all.** `open-questions.md` #24: this machine has no SQL Server, no
LocalDB and no Docker. Writing roughly seven hundred lines of T-SQL that cannot be executed, plus a
migration set that cannot be applied, and filing it as implemented is what non-negotiable #7 exists
to prevent. Phase 6 set the precedent in the other direction and it was the right one: everything
above `IIrpClient` was built and the sandbox was flagged.

**Done properly it also needs a structural change.** EF's `MigrationsAssembly` points at an
assembly, so a second provider's migrations need their own project. That is a solution-level change
the owner should approve rather than find already made.

### What was delivered instead

§2 asks a question as well as setting a task: the swap "should be a provider swap plus a migration
set, not a rewrite. If it turns out to be a rewrite, that is a Phase 0 defect worth recording as an
ADR." That question is now answered, and the answer is a tested fact rather than an expectation.

`SqlServerPortabilityTests` measures the SQLite-only surface of every reporting statement:

| Construct | Occurrences | T-SQL equivalent |
|---|---|---|
| `\|\|` string concatenation | 4 (two `LIKE` clauses) | the `+` operator, or `CONCAT()` |
| `LIMIT` | 2 | `OFFSET 0 ROWS FETCH NEXT n ROWS ONLY`, which needs an `ORDER BY` |
| `substr()` | 1 | `SUBSTRING()` |
| `length()` | 1 | `LEN()` |

**Eight occurrences across four construct kinds, in roughly seven hundred lines of SQL.** Everything
else — the CTEs, the `COALESCE`s, the `CAST`s, the date comparisons against `yyyy-MM-dd` text — is
ANSI and ports unchanged.

The test also asserts that none of the constructs *without* a mechanical equivalent are present
(`strftime`, `julianday`, `IFNULL`, `GROUP_CONCAT`, `printf`, `typeof`, `datetime`, `GLOB`), because
one of those appearing would change the shape of the port rather than its size. And it asserts the
SQL is still over five hundred lines, because "eight divergences" is only reassuring in proportion.

**ADR-0001's dialect layer did what it was written for. This is a swap, not a rewrite, and there is
no Phase 0 defect to record.**

### What closes C9.4

1. A SQL Server Express instance — §4a D3 names the main billing PC as its eventual home.
2. A second migrations project, and an initial migration generated against it.
3. The four constructs above translated.
4. The Phase 1–8 suites run on it.

C9.1 and C9.3 are only fully proved there too, since SQLite's locking is not SQL Server's. The
*detection* in both is provider-independent by construction, which is exactly why the concurrency
token is a domain-managed `Guid` rather than a `rowversion`.

---

## 5. Defects found and fixed during the phase

### 5.1 A spurious lost-update on every detached save (introduced and caught in this phase)

The concurrency token was written as an EF **shadow property** first, which looked right: no entity
class had to be edited, the model guard passed, and 1,084 tests were green.

It broke every service that reads with `AsNoTracking` and then calls `Update` on the detached
instance. A no-tracking read carries no shadow values, so re-attaching sent an all-zero token as the
original value, matched no row, and turned an ordinary save into a lost-update report against
nobody.

`GstRateScheduleTests` caught it — the regression test Phase 7 added after finding the supersession
bug. The token became a real property on `IConcurrencyStamped`, and the reasoning is recorded on the
interface member so the next person does not retry the shadow version.

**Worth noting for its own sake:** this is the second time that test has earned its place.

### 5.2 A cascade delete, caught by the model guard

`AppRole` → `RolePermissionGrant` was first mapped `Cascade`, with a comment arguing it was a
justified exception. `Model_ConfiguresEveryRelationshipAsRestrict` disagreed — spec §5 states the
rule "without exception" — and the guard was right. The relationship is `Restrict` like every other
one, `ReplaceGrants` hands back the rows it dropped so the caller can delete them, and
`DeleteRoleAsync` removes a role's grants before the role.

### 5.3 Two startup defects that 1,117 green tests could not see

The schema-upgrade window referenced a brush called `BorderBrush`, which the theme does not define —
it is `ControlBorderBrush` — and a careless edit had left a stray `/>` in its button markup. Both
compiled. Both passed the entire suite. The product died at startup, before any screen could be
reached.

The cause is structural rather than careless: `ScreenViewLoadTests` sweeps the module catalogue,
which is every screen and **no window**. The startup windows — splash, first-run company, schema
upgrade, sign-in — are in no module, so nothing was ever constructing them.

`EveryWindowInTheApplication_LoadsWithTheApplicationResources` now constructs every `Window` in the
assembly with the real merged dictionaries. It fails on both of those defects.

This is the same lesson as the 5 Oct 2026 batch, in a place that batch did not reach: running the
product is what found it, and the guard is what makes it stay found.

### 5.4 A misleading migration history, corrected before it shipped

The first cut created `RolePermissionGrants` with a cascade in one migration and corrected it to
`Restrict` in a later one. The end state was right and the history read wrong. Nothing had applied it
anywhere but throwaway test databases, so the Phase 9 migration was regenerated as a single file.

---

## 6. Smoke run

Run against the real `%LOCALAPPDATA%\VinayakaAgencies\motorpump.db`, via
`dotnet run --project src\VinayakaAgencies.App`, reading `log-20261009.txt`.

**1. Stale schema, startup migration off.** Log: `Startup migration is disabled; the schema is
assumed to be current.` → the two `__EFMigrationsHistory` queries → and then nothing, because the
upgrade window is modal and waiting. The gate works; the product does not open the books.

**2. Declining.** Not exercised — it needs a click. The code path closes the application; its
unit-level behaviour is covered by `GatedMigrationTests`.

**3. After the upgrade.** With the migration applied, the same launch produced 374 log lines, no
errors, through every seed in order and ending at:

```
INSERT INTO "RolePermissionGrants" ("Id", "CreatedAtUtc", "CreatedBy", "Permission", "RoleId")
VALUES (@p0, @p1, @p2, @p3, @p4);
...
SELECT EXISTS (
    SELECT 1
    FROM "AppUsers" AS "a")
```

That last query is `AnyAccountsExistAsync`, called by the sign-in window deciding which of its three
states to show. So: upgrade gate honoured, all seeds run, Owner and Operator grants written, and the
product waiting at the login prompt.

**One detail worth reading off that line.** The `CreatedBy` parameter has `Size = 29`. That is the
`(no sign-in) MACHINE\user` form — `SessionCurrentUser` doing exactly what it claims, because
provisioning runs before anybody could have signed in and `ICurrentUser` must never resolve to
blank. The prefix is what keeps a pre-sign-in stamp distinguishable from an application account of
the same name in the one record that is supposed to settle who did what.

**4. Signing in, and the two screens.** Not exercised interactively — modal windows need a click, and
this environment has no operator. Covered by `Phase9ScreenTests` against the real composition, and
by `EveryScreenAgainstARealDatabaseTests`, which resolves and loads all 43 screens (41 before this
phase, plus Users and Roles) against a real migrated database.

---

## 7. Outstanding before Phase 9 sign-off

1. **A SQL Server Express instance** — `open-questions.md` #24. Closes C9.4, and completes C9.1/C9.3.
2. **Owner walkthrough** of the sign-in window, the schema-upgrade window, and the Users and Roles
   screens. **Screenshots** under `specs/evidence/images/`.
3. **Owner confirmation of the Operator role's grants.** The table in
   `docs/features/users-roles-and-permissions.md` is a literal reading of §4a D4. Roles are data, so
   it is a starting point rather than a ruling — but the starting point is what an install ships
   with.
4. **Owner confirmation of the password policy** — eight characters minimum, no composition rule.
   One constant and a message; stated rather than specified.
5. **The bootstrap rule, said out loud** (§3). Until the first account exists, this phase enforces
   nothing. The owner should decide at walkthrough whether that is acceptable as a shipping state or
   whether first-run should insist.
