# VinayakaAgencies

Windows-native (.NET 8 + WPF) accounting, billing and inventory suite for **Vinayaka Agencies**, a
motor & pump trading business. Tally- and Marg-class in scope, with India GST/TDS compliance as a
core requirement rather than an add-on.

> The solution and root namespace are `VinayakaAgencies`; "Vinayaka Agencies" is the trading name
> shown on screen. The rename from `MotorPumpSuite` was made by the project owner immediately
> before the Phase 1 migration was generated - see
> [`specs/open-questions.md`](specs/open-questions.md) #7.

## Where to start

| File | What it is |
|---|---|
| [`project.md`](project.md) | The master specification - the source of truth |
| [`CLAUDE.md`](CLAUDE.md) | The working contract: stack, layering, conventions, non-negotiables |
| [`specs/`](specs/) | One spec per phase, plus ADRs, open questions and evidence reports |
| [`specs/CURRENT_PHASE`](specs/CURRENT_PHASE) | The phase currently in flight |

Work proceeds **one phase at a time**. No implementation begins on a phase until its spec is marked
`Status: Approved`, and a `PreToolUse` hook enforces that on `src/` and `tests/`.

## Build and test

```powershell
.\build.ps1                 # restore + build + test  (what the Stop hook runs)
.\build.ps1 -NoTest         # restore + build only
dotnet run --project src\VinayakaAgencies.App
```

Warnings are build breaks (`TreatWarningsAsErrors=true`). Test results and coverage land in
`artifacts/`.

## Layout

```
src/VinayakaAgencies.Domain          entities + abstractions, references nothing
src/VinayakaAgencies.Application     services, DTOs, use-cases; depends on Domain only
src/VinayakaAgencies.Infrastructure  EF Core, repositories, unit of work, Dapper, migrations
src/VinayakaAgencies.App             WPF host: DI, Serilog, shell
tests/                               mirrors src
```

`Domain <- Application <- Infrastructure <- App`. Only Infrastructure knows a database exists,
which is what keeps the Phase 9 SQLite to SQL Server Express move a configuration change rather
than a rewrite. See [`specs/adr/ADR-0001`](specs/adr/ADR-0001-layering-and-persistence-abstraction.md).

## Status

Phase 0 (Foundations) is implemented; see
[`specs/evidence/phase-0-evidence.md`](specs/evidence/phase-0-evidence.md) for the checkpoint
evidence. Phase 1 (Accounting Core) is approved and in flight.
