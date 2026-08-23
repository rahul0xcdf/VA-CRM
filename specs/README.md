# /specs — phase specifications

One file per phase, derived from [`../project.md`](../project.md) §10.

## Rules

1. A phase's spec is written **before** its implementation begins.
2. Implementation is blocked until the spec's frontmatter says `Status: Approved`.
   (The `PreToolUse` hook that once enforced this was removed at the owner's instruction;
   the rule itself stands and is honoured by the working agreement.)
3. `CURRENT_PHASE` holds a single integer — the phase currently in flight. Read it, then that
   phase's spec, before touching anything.
4. Checkpoint criteria are copied verbatim from `project.md` §10 and may be **extended**, never weakened.
5. At the end of a phase, `qa-agent` writes `evidence/phase-N-evidence.md`. The project owner
   reviews that before `CURRENT_PHASE` is advanced.

## Status values

| Value | Meaning |
|---|---|
| `Draft` | Being written. Implementation blocked. |
| `In Review` | Awaiting project-owner approval. Implementation blocked. |
| `Approved` | Implementation unblocked for this phase only. |
| `Complete` | Checkpoint met, evidence filed and reviewed. |

## Files

| File | Phase | Status |
|---|---|---|
| `phase-0-foundations.md` | 0 | Approved |
| `phase-1-accounting-core.md` | 1 | Draft |
| `phase-2-inventory.md` | 2 | Draft |
| `phase-3-sales-purchase-pipeline.md` | 3 | Draft |
| `phase-4-customer-pricing.md` | 4 | Draft |
| `phase-5-gst-returns.md` | 5 | Draft |
| `phase-6-einvoice-eway.md` | 6 | Draft |
| `phase-7-tds-payroll-banking.md` | 7 | Draft |
| `phase-8-interoperability.md` | 8 | Draft |
| `phase-9-multiuser.md` | 9 | Draft |
| `phase-10-hardening.md` | 10 | Draft |

- `adr/` — architecture decision records for anything that changes a locked decision in `project.md` §3.
- `evidence/` — per-phase QA evidence reports.
- `session-log.md` — optional session notes; not written by any tooling since the PreCompact hook
  was removed.
- `open-questions.md` — `project.md` §11 assumptions, tracked until confirmed.
