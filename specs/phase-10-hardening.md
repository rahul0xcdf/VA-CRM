# Phase 10 — Hardening & Packaging

```yaml
Phase: 10
Title: Hardening & Packaging
Status: Draft
Source: project.md §10 (Phase 10)
Depends on: Phases 0–9
```

> **Draft.** Do not implement until `Status: Approved`.

## 1. Deliverables (project.md §10)

Backup/restore, data-at-rest protection, performance/indexing pass, Windows installer.

## 2. Scope notes

- **Backup/restore:** scheduled and on-demand; the backup covers the database **and** the FastReport
  templates, logos and configuration — a restore that loses the customised invoice template is not a
  restore. Backups are verified (restore into a scratch DB and check integrity), not merely written.
- **Data at rest:** encrypted credentials (NIC/GSTN), and a decision recorded on whether the company
  database itself is encrypted, with the recovery consequences spelled out.
- **Performance/indexing pass** against a realistic dataset — the ≥5,000-SKU catalogue from Phase 2
  plus several years of vouchers. Target the screens that are used hundreds of times a day: item
  lookup during billing, customer outstanding, day book.
- **Windows installer** (MSI or MSIX): prerequisites, per-machine install, upgrade path that migrates
  an existing database without data loss, and clean uninstall that does **not** delete company data.
- Crash reporting / log collection the owner can actually send when something goes wrong.

## 3. Checkpoint criteria (project.md §10)

| # | Criterion |
|---|---|
| C10.1 | A full backup/restore cycle round-trips without data loss |
| C10.2 | The installer runs clean on a fresh Windows machine |

Additional:

| # | Criterion |
|---|---|
| C10.3 | An upgrade install over the previous version preserves all company data and applies pending migrations |
| C10.4 | Uninstall leaves company data and backups intact |
| C10.5 | Named hot screens meet their response budget on the realistic dataset |
| C10.6 | A restore from backup reproduces a byte-identical trial balance and stock summary |

## 4. Open questions to close before approval

- Backup destination: local disk, network share, external drive? Retention policy?
- Is off-site/cloud backup wanted, given cloud access is otherwise out of scope for v1?
- Response-time budgets for the hot screens — what feels acceptable at the counter?
- Signed installer? (Unsigned installers trigger SmartScreen warnings on fresh machines.)
