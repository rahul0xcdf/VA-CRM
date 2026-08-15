# Vinayaka Agencies — Accounting, Billing & Inventory for a Motor & Pump Trading Business

Windows-native (.NET 8 + WPF) accounting / billing / inventory suite, Tally- and Marg-class,
with India GST/TDS compliance as a core requirement.

**The master specification is [`project.md`](project.md). This file is the working contract; `project.md` is the source of truth.**

## Non-negotiables

1. **Spec gate.** No code is written for Phase N until `/specs/phase-N-*.md` is marked `Status: Approved`.
   A `PreToolUse` hook enforces this on `src/` and `tests/`.
2. **One phase per batch.** Work only inside the current phase's scope (`/specs/CURRENT_PHASE`).
   If a task needs something from a later phase, **stop and flag it** — do not silently expand scope.
3. **Audit trail cannot be disabled.** Every create/alter/delete of a voucher or master is logged with
   user, timestamp, and before/after values. No user, including admin, may switch it off (MCA Rule 3(1)).
4. **Ledger balances are always derived**, never stored as an independently-mutable column.
5. **All DB access goes through the repository / unit-of-work abstraction** — never a bare `DbContext`
   outside `VinayakaAgencies.Infrastructure`. Phase 9 migrates SQLite → SQL Server Express; that must not be a rewrite.
6. **Nothing tax-related is hardcoded.** HSN/SAC codes, GST rates, TDS/TCS sections, rates and thresholds
   are data-driven config rows.
7. **Evidence, not assertion.** A phase is done when `qa-agent` produces test output / API responses /
   screenshots — not when an agent says "done".

## Tech stack

| Layer | Choice |
|---|---|
| Runtime / UI | .NET 8, C# 12, WPF, MVVM (CommunityToolkit.Mvvm) |
| DB (Phase 1–8) | SQLite (single file) |
| DB (Phase 9+) | SQL Server Express |
| Writes / migrations | EF Core 8 |
| Reporting queries | Dapper (via `ISqlQueryExecutor`) |
| Reports / printing | FastReport .NET WPF (runtime designer — end users edit templates) |
| Excel | ClosedXML (MIT). **Never EPPlus** — paid licence since v5. |
| Tally interop | Tally XML "Data Interchange" (file) → later XML-over-HTTP, port 9000. **Never TallyODBC.** |
| Logging | Serilog (console + rolling file under `%LOCALAPPDATA%\VinayakaAgencies\logs`) |
| Tests | xUnit + **Shouldly**. **Never FluentAssertions** — paid licence since v8. |

## Layout

```
src/VinayakaAgencies.Domain          entities + abstractions, zero external deps
src/VinayakaAgencies.Application     services, DTOs, use-cases; depends on Domain only
src/VinayakaAgencies.Infrastructure  EF Core, DbContext, repositories, UoW, Dapper, migrations
src/VinayakaAgencies.App             WPF host: DI, Serilog, navigation shell, views/viewmodels
tests/…                            mirrors src
specs/                             one file per phase + ADRs + session-log.md
.claude/agents/                    8 subagents (see project.md §7)
.claude/hooks/                     PowerShell hook scripts
```

**Dependency rule:** `Domain ← Application ← Infrastructure ← App`. Domain references nothing.
Application never references EF Core. Only Infrastructure knows the database exists.

## Commands

```powershell
.\build.ps1                 # restore + build + test  (what the Stop hook runs)
.\build.ps1 -NoTest         # restore + build only
dotnet build VinayakaAgencies.sln -c Debug
dotnet test  VinayakaAgencies.sln
dotnet run --project src\VinayakaAgencies.App
```

EF Core migrations (always target the Infrastructure project, start from the App project):

```powershell
dotnet ef migrations add <Name> -p src\VinayakaAgencies.Infrastructure -s src\VinayakaAgencies.App -o Persistence\Migrations
dotnet ef database update  -p src\VinayakaAgencies.Infrastructure -s src\VinayakaAgencies.App
```

## Conventions

- **Nullable enabled, warnings as errors.** Treat a warning as a build break; do not suppress without an ADR.
- **Money is `decimal(18,4)`; quantity is `decimal(18,4)`.** Never `double`/`float` for either.
- **Dates:** store UTC (`DateTimeOffset`) for audit/system timestamps; store the business date
  (voucher date, invoice date) as a `DateOnly` — Indian financial-year logic depends on the local calendar date.
- **Entities** derive from `Entity` (GUID `Id`, generated client-side so Tally export GUIDs are stable).
- **Async all the way** for I/O; suffix `Async`; pass `CancellationToken` as the last parameter.
- **One public type per file**, file name == type name.
- **No `#region`.** No commented-out code. No `TODO` without a spec reference (`// TODO(phase-3): …`).
- Test naming: `Method_Scenario_ExpectedResult`.
- ViewModels are `ObservableObject`; no code-behind logic beyond `InitializeComponent()`.

## Working agreement

- Read `/specs/CURRENT_PHASE`, then that phase's spec, before touching anything.
- Prefer editing an existing file over creating a new one; prefer extending a spec over inventing behaviour.
- Schema or public-contract changes need `architect-agent` review before implementation.
- `compliance-agent` runs at the frontier model tier. Do not downgrade it.
- End of every phase: `qa-agent` produces `/specs/evidence/phase-N-evidence.md`.
