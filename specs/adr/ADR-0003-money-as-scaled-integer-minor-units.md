# ADR-0003 — Money is persisted as a scaled 64-bit integer in minor units

```yaml
Status: Accepted
Date: 2026-08-14
Decided by: Project owner
Supersedes: nothing
Amends: CLAUDE.md "Money is decimal(18,4)" — as a storage detail only; the domain type is unchanged
Related: ADR-0001 (layering and persistence abstraction), specs/phase-1-accounting-core.md §6
```

## Context

`CLAUDE.md` states that money and quantity are `decimal(18,4)` and never `double`/`float`. That is
the right rule, and Phase 1 is where it collides with a hard property of the Phase 1–8 database.

**SQLite has no decimal type.** EF Core maps `decimal` to a `TEXT` column. Reporting queries run
through Dapper and aggregate in SQL (ADR-0001), and `SUM()` over a `TEXT`-affinity column makes
SQLite apply numeric affinity and aggregate in **REAL — IEEE-754 double**.

This was verified empirically rather than assumed. Three values that are exact in decimal, stored as
TEXT and summed by SQLite:

```
values stored : '0.1', '0.2', '-0.3'   (typeof = text)
SELECT SUM(amt)      -> 5.551115123125783e-17
SELECT typeof(SUM(amt)) -> real
SUM = 0 exactly?     -> False
```

Left alone, every trial balance, P&L and balance sheet in Phases 1–8 is computed in floating point.
The visible symptom is a trial balance that misses zero by a fraction of a paisa. The only ways to
hide that are an epsilon tolerance or a display rounding — both of which mean the books balance
because we rounded them, which is not defensible in software subject to MCA Rule 3(1).

`Company` holds no monetary column, so Phase 0 was unaffected. Phase 1 introduces `JournalLine`,
where the invariant `Σ debit == Σ credit` must hold **exactly**.

## Decision

Money and quantity remain `decimal` in the domain, in every DTO, and in every public contract.
Nothing in the Application or App layers changes.

**Persistence** stores a scaled 64-bit integer in **minor units of 1/10,000** — the same scale as
the declared `decimal(18,4)`:

- SQLite (Phases 1–8): `INTEGER` column via an EF Core value converter.
- SQL Server Express (Phase 9+): native `decimal(19,4)`; the converter is not applied.

Aggregation in SQL is then exact integer arithmetic. The balance check becomes
`Σ AmountMinor == 0` — exact, with no epsilon anywhere in the codebase.

Reporting row DTOs carry `long` minor units and are converted to `decimal` once, in a single
mapping helper, at the boundary where the row leaves the reporting layer.

### Range

`long.MaxValue / 10^4` ≈ 9.22 × 10^14 rupees. For a motor and pump dealership that is not a
practical ceiling; it is roughly a thousand times India's annual GDP.

### Rounding

The converter never rounds. A `decimal` carrying more than four decimal places is a programming
error, not a value to be silently truncated, and the converter throws rather than quietly losing
paise. Rounding is a business decision (GST rounding, invoice round-off) and belongs in the domain
where it can be audited, not in a persistence converter.

## Consequences

**Good**

- The trial-balance and balance-sheet invariants are exact, provable, and testable without tolerance.
- The provider difference stays inside the dialect and configuration layer, exactly where ADR-0001
  already isolates provider differences. Phase 9 drops the converter; no report code changes.
- Integer aggregation is faster than text-to-numeric coercion.

**Bad, and accepted**

- Reporting DTOs are less readable — they carry `long` minor units, not rupees.
- Anyone opening the SQLite file with a generic tool sees `12500000`, not `1250.0000`. This is a
  real inspection cost, mitigated only by it being documented here.
- Every new monetary column must remember the converter. Enforced by convention in
  `AppDbContext.ConfigureConventions` plus a test asserting no `decimal` property maps to a `TEXT`
  column, so it cannot be forgotten silently.

**Rejected alternatives**

| Alternative | Why not |
|---|---|
| Keep `decimal` as TEXT, round on read | The books would balance by rounding. Indefensible under audit. |
| Aggregate in C# instead of SQL | Abandons the Dapper reporting path (ADR-0001) and pulls whole ledgers into memory; a day book over a full year would not scale. |
| Move to SQL Server Express now | Solves it natively but pulls an entire phase forward, adds an install-time dependency, and forces the concurrency work `project.md` deliberately deferred to Phase 9. |
| Store paise (1/100) instead of 1/10,000 | Loses the two extra places that `decimal(18,4)` exists to provide — GST rates and per-unit prices genuinely need them. |

## Compliance

`CLAUDE.md`'s "Money is `decimal(18,4)`" stands as written for every type a developer touches. This
ADR narrows it to a statement about the **domain type and its scale**, and records that the storage
representation under SQLite is a scaled integer of that same scale. `double` and `float` remain
forbidden everywhere, without exception.
