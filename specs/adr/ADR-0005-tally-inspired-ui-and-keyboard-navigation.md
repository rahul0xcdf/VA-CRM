# ADR-0005 — Tally Prime-inspired presentation layer with keyboard-first navigation

```yaml
Status: Accepted
Date: 2026-08-23
Decided by: Project owner (directive: "UI similar to Tally Prime, but clean, with similar shortcuts")
Supersedes: nothing
Amends: nothing — operationalises project.md §1 "match TallyPrime/Marg on workflows; inspiration, not clone" at the presentation layer
Related: specs/phase-5-gst-returns.md §2 (batch addition), ADR-0004 (print templates as data)
```

## Context

Phases 0–4 delivered fifteen functional screens whose visuals grew ad hoc: each view carries its
own styling choices, navigation is mouse-first, and data-entry forms ignore the keyboard habits of
the shop-floor users this suite is built for. The owner has judged the result below standard and
directed a presentation-layer rework: **familiar to Tally Prime users, but clean**, with similar
keyboard shortcuts.

Two constraints shape the answer:

- `project.md` §1: match TallyPrime/Marg on *workflows*; **do not copy their UI or code**. We take
  the interaction grammar — menu-driven home, keyboard-first entry, drill-down with an Escape
  back-stack — not their pixels, trade dress, or trademarks.
- The suite hand-rolls its presentation stack deliberately (Phase 3 chose a built-in FlowDocument
  print engine over FastReport-for-everything). Adding a commercial WPF control toolkit would add a
  licence surface the project has consistently avoided.

## Decision

1. **Original visual language, Tally-familiar structure.** A persistent top header showing company,
   GSTIN and active financial year/period; a left home menu of numbered modules (the Tally
   "gateway" idea, called **Home** in our UI copy — never Tally trademarks); dense, information-rich
   content pages; a bottom status bar showing mode, period, and the shortcuts available on the
   focused screen. Light theme, flat and clean, high contrast for grid-heavy accounting screens.
2. **Keyboard-first navigation.** Every mouse action has a key equivalent, following Tally muscle
   memory where non-infringing: F-keys select voucher types in voucher contexts, `Ctrl+A` accepts/saves,
   `Ctrl+Q` cancels/closes, `Alt+G` opens a Go To command palette, `Esc` walks back the navigation
   stack one level, arrow/Enter drive grids and masters lists. The full map is documented centrally
   (`docs/user-guide/`) so keys stay consistent as screens are added.
3. **Shared presentation infrastructure.** One set of resource dictionaries, control styles and
   templates under `src/VinayakaAgencies.App/Presentation`, consumed by a single shell window that
   hosts pages through the existing navigation service. **No new NuGet UI dependencies.**
4. **Incremental migration, foundation first.** The Phase 5 batch delivers the foundation plus all
   new GST screens built on it, and migrates existing screens onto the shared theme. ViewModel
   behaviour is unchanged except for the added keyboard commands; navigation registrations keep
   existing view names.
5. **Printing is untouched.** Invoice/report output flows through the existing template engine;
   this ADR concerns the app chrome and data screens only.

## Consequences

- Ad-hoc per-view styling is retired as each view migrates; a screen is not "migrated" until it uses
  only the shared dictionaries and registers its shortcuts in the central map.
- Keyboard-only operation becomes the supported norm; the mouse path remains fully functional.
- Trade-dress risk is managed by original layout, wording and palette; no Tally marks, screenshots,
  or copied resources enter the repo.
- Future screens cost less: module page, master list and voucher-entry patterns exist as reusable
  styles/templates after this batch.
