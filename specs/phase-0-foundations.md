# Phase 0 — Foundations

```yaml
Phase: 0
Title: Foundations
Status: Approved
Source: project.md §6, §7, §8, §10 (Phase 0)
Depends on: nothing
Blocks: all later phases
```

## 1. Goal

Stand up the skeleton every later phase builds on: the solution and its layering, dependency
injection, logging, SQLite + EF Core, the repository / unit-of-work abstraction that makes the
Phase 9 SQL Server migration a configuration change rather than a rewrite, a build+test harness,
and the Claude Code project scaffolding from `project.md` §6–§8.

**Phase 0 writes no business logic.** No vouchers, no stock, no GST, no pricing. If a task here
seems to need any of those, that is scope creep — stop and flag it (`project.md` §9).

## 2. In scope

### 2.1 Solution & layering

`MotorPumpSuite.sln` with four source projects and three test projects:

| Project | TFM | Purpose | May reference |
|---|---|---|---|
| `src/MotorPumpSuite.Domain` | `net8.0` | Entities, value objects, abstractions (`IRepository<T>`, `IUnitOfWork`, `ISqlQueryExecutor`, `IClock`, `ICurrentUser`) | **nothing** |
| `src/MotorPumpSuite.Application` | `net8.0` | Use-cases / services, DTOs, DI registration | Domain |
| `src/MotorPumpSuite.Infrastructure` | `net8.0` | `AppDbContext`, EF configurations, migrations, repository + UoW implementations, Dapper query executor, connection factory | Domain, Application |
| `src/MotorPumpSuite.App` | `net8.0-windows` (WPF) | Generic Host, DI composition root, Serilog, shell window | all three |
| `tests/MotorPumpSuite.Domain.Tests` | `net8.0` | | Domain |
| `tests/MotorPumpSuite.Application.Tests` | `net8.0` | | Application, Domain |
| `tests/MotorPumpSuite.Infrastructure.Tests` | `net8.0` | migration + round-trip tests against a real temp SQLite file | Infrastructure, Domain |

**Dependency rule, enforced by an architecture test:** Domain references no other project and no
EF Core; Application does not reference EF Core; only Infrastructure references
`Microsoft.EntityFrameworkCore.*`.

### 2.2 Persistence abstraction (the Phase 9 insurance policy)

- `IRepository<TEntity>` — `GetByIdAsync`, `ListAsync(ISpecification)`, `AddAsync`, `Update`, `Remove`, `AnyAsync`, `CountAsync`.
- `IUnitOfWork` — `Repository<T>()`, `SaveChangesAsync`, `BeginTransactionAsync`. One transaction spans
  ledger + stock writes so Phase 3's atomic-posting checkpoint is achievable.
- `ISqlQueryExecutor` — the Dapper seam for read-only reporting queries. It takes a *named* SQL
  statement resolved per provider, **never** an inline dialect-specific string from a calling layer,
  so Phase 9 can swap SQLite SQL for T-SQL without touching report code.
- `IDbConnectionFactory` — hides `SqliteConnection` vs `SqlConnection` from every consumer.
- No `DbContext` type leaves the Infrastructure assembly. No `using Microsoft.EntityFrameworkCore`
  outside it.

### 2.3 Foundation entities

Exactly one real entity in Phase 0: **`Company`** (`Id`, `Name`, `LegalName`, `Gstin`, `StateCode`,
`FinancialYearStartMonth`, `BaseCurrencyCode`, audit stamps). It is the anchor of the
multi-company-ready schema (`project.md` §4.1) and the subject of the round-trip checkpoint test.
Nothing else — the chart of accounts and every voucher type belong to Phase 1.

Base type `Entity`: client-generated GUID `Id` (stable GUIDs are required by the Phase 8 Tally
export's "overwrite if GUID exists" behaviour, so they must be assigned at creation, not by the DB).
`IAuditStamped`: `CreatedAtUtc`, `CreatedBy`, `ModifiedAtUtc`, `ModifiedBy` — populated centrally in
`SaveChangesAsync` from `IClock` + `ICurrentUser`. This is *not* the audit trail; the full
before/after edit log is Phase 1, and these stamps are the seam it will hang off.

### 2.4 Host, DI, configuration, logging

- `Microsoft.Extensions.Hosting` generic host in the WPF `App.xaml.cs`; the DI container owns the
  main window and all viewmodels.
- Configuration from `appsettings.json` + `appsettings.Local.json` (git-ignored) + environment
  variables. Connection string resolves to `%LOCALAPPDATA%\MotorPumpSuite\motorpump.db` by default;
  a `Database:Provider` key (`Sqlite` today, `SqlServer` from Phase 9) selects the provider.
- Serilog: console + rolling daily file at `%LOCALAPPDATA%\MotorPumpSuite\logs\log-.txt`,
  31-day retention. Unhandled dispatcher exceptions are logged before the app dies.
- Migrations are applied at startup for SQLite (single-user, Phase 1–8). A note in the Phase 9 spec
  flags that auto-migrate-on-startup must become an explicit, gated step once multiple clients connect.

### 2.5 Build & CI harness

- `build.ps1` — restore, build (`-warnaserror`), test with TRX + coverage output to `artifacts/`.
  This is exactly what the `Stop` hook runs.
- `.github/workflows/ci.yml` — `windows-latest`, .NET 8, restore/build/test, uploads test results.
- `global.json` pinning the SDK feature band so a future SDK does not silently change behaviour.

### 2.6 Claude Code scaffolding (`project.md` §6–§8)

- `CLAUDE.md` at root, under 200 lines.
- `/specs/phase-0-foundations.md` … `/specs/phase-10-hardening.md`, this file `Approved`, the rest `Draft`.
- `/specs/CURRENT_PHASE`, `/specs/open-questions.md`, `/specs/adr/`, `/specs/evidence/`.
- `.claude/agents/*.md` — all 8 subagents from §7, each with `name`/`description`/`tools`/`model`
  frontmatter and a scoped prompt. `compliance-agent` stays at the frontier tier.
- `.claude/settings.json` with all four hooks from §8, backed by scripts in `.claude/hooks/`:
  - `SessionStart` → inject current phase spec path + checkpoint checklist
  - `PreToolUse` on `Write|Edit|MultiEdit` → block `src/` and `tests/` edits unless the current
    phase spec is `Status: Approved` (the spec gate)
  - `Stop` → block session end until build + tests pass and a QA evidence file exists
  - `PreCompact` → dump file list, open TODOs and test commands to `/specs/session-log.md`

## 3. Out of scope (explicitly)

Chart of accounts, ledgers, vouchers, the double-entry engine, the edit-log/audit-trail *feature*,
stock, items, GST, pricing, FastReport, ClosedXML, Tally interop, SQL Server, user accounts and
permissions. Phase 0 adds the NuGet references for none of these.

## 4. Checkpoint criteria

Copied from `project.md` §10 (Phase 0), plus explicit verification steps.

| # | Criterion (from §10) | How it is verified |
|---|---|---|
| C0.1 | Clean build | `.\build.ps1 -NoTest` exits 0 with zero warnings (`TreatWarningsAsErrors=true`) |
| C0.2 | Empty-schema migration runs | `dotnet ef migrations list` shows `InitialCreate`; a test applies it to a fresh temp SQLite file and asserts the `Companies` table exists |
| C0.3 | Trivial round-trip test passes (create + read one entity) | `Company_CanBeRoundTripped_ThroughRepositoryAndUnitOfWork` — insert via `IUnitOfWork`, read back in a *new* context, assert field equality |
| C0.4 | All 8 subagent files exist | `.claude/agents/` contains 8 `.md` files, each with valid `name`/`description`/`tools`/`model` frontmatter; a test asserts the count and frontmatter keys |
| C0.5 | Both hook types exist and fire correctly on a test edit | `.claude/settings.json` declares SessionStart, PreToolUse, Stop, PreCompact. Evidence: spec-gate script run against a `src/` path with the phase spec flipped to `Draft` returns exit 2 and blocks; flipped back to `Approved` returns exit 0 |

Additional Phase 0 criteria:

| # | Criterion | How it is verified |
|---|---|---|
| C0.6 | Layering holds | Architecture test asserts Domain has no project/EF references and Application has no EF Core reference |
| C0.7 | App starts | `dotnet run --project src\MotorPumpSuite.App` opens the shell window, applies migrations, and shows a live company count read through `IUnitOfWork` |
| C0.8 | No business logic leaked in | `src/` contains exactly one entity (`Company`); no voucher/stock/GST types |

## 5. Evidence required

`qa-agent` writes `/specs/evidence/phase-0-evidence.md` containing: full `build.ps1` output,
`dotnet test` summary with per-test names, `dotnet ef migrations list` output, the spec-gate
block/allow transcript, and a screenshot of the running shell window.

## 6. Sign-off

- [ ] Project owner has reviewed the evidence report
- [ ] `project.md` §11 open questions triaged in `/specs/open-questions.md`
- [ ] `Status` of this file changed to `Complete` and `CURRENT_PHASE` advanced to `1`
