# ADR-0006 — Tally-shaped shell: Gateway, shortcut bars, and a central keyboard map

```yaml
Status: Accepted
Date: 2026-09-17
Decided by: Project owner (directive: "the UI really sucks — I want my software similar to TallyPrime,
             all those shortcuts and all should be similar. Fix the UI completely.")
Supersedes: nothing
Amends: ADR-0005 — keeps its principles, replaces the shell it produced
Related: project.md §1 (inspiration, not clone)
```

## Context

ADR-0005 set out to deliver a "Tally Prime-inspired presentation layer with keyboard-first
navigation". What it actually produced was a themed WPF application: a 113-line shell hosting a
permanent `ListBox` sidebar, and three shortcuts — `Alt+G`, `Ctrl+A`, `Ctrl+Q`. The principles in
ADR-0005 were right; the implementation did not carry them out. The owner judged the result
unusable and asked for the structure and the shortcuts to match TallyPrime properly.

Measured before starting, the gap was:

- **No top menu bar.** Tally's Go To / Switch To / Company / Print / Export row did not exist.
- **No Gateway.** A sidebar listing thirty screens was permanently on display; there was no menu to
  enter, act in, and escape from.
- **No right-hand button bar** and **no bottom action bar** — the two surfaces on which Tally
  advertises what the current screen's keys do.
- **No accelerator letters.** Nothing could be reached with a single keypress.
- **3 of ~80 shortcuts**, and one of the three was wrong: `Ctrl+Q` was bound to "cancel", where in
  Tally it exits the screen and `Alt+X` cancels.

## Decision

### 1. The shell is five bands, and the Gateway is a place you leave

Top menu, company bar, body, action bar, status line. The body shows **either** the Gateway menu or
one screen — never both. `Esc` walks back toward the Gateway and **stops there**; it never exits the
application, because an accounting operator presses Esc reflexively and an Esc that could close the
program over a half-typed voucher is a trap. Leaving is an explicit act.

This is the substantive change from the sidebar. A permanently visible list of thirty screens asks
the operator to read before every action. A menu that is entered, acted on and escaped from lets them
learn a path once and then run it blind, which is how an experienced Tally user actually works.

### 2. Accelerator letters are allocated, shown, and claimable

Each Gateway entry answers to one letter, highlighted inside the word — "C**h**art of Accounts" is
reached with H because C belongs to "Create". Allocation walks each label left to right taking the
first free letter, and is unique across the **whole** Gateway rather than per section: the same key
must not mean two things depending on where the eye happens to be.

Screens may **claim** a letter explicitly. Computed letters are positional, so inserting one screen
into a module would otherwise silently re-letter everything below it and move a key an operator had
memorised. Anything likely to be memorised should claim.

### 3. One keyboard map, in code, tested

`AppShortcuts` holds every gesture with the label the chrome displays. Not per-view `InputBinding`s:
the same gesture must never mean two things on two screens, and a central table can be read end to
end and checked, which thirty scattered views cannot. A test asserts no duplicate gesture, so a key
gaining a second meaning breaks the build instead of surprising somebody mid-voucher.

The gestures follow TallyPrime's published shortcuts. **Keys are an interface convention, not an
appearance** — adopting them is what `project.md` §1 means by matching on workflow.

### 4. Bars are generated from the commands they invoke

`IScreenShortcuts` lets a screen contribute accept/cancel commands and entries for either bar. The
shell composes both from its own globals plus the screen's. Screens never draw chrome, so a key
cannot be advertised in one place and bound in another. Only keys that **work** are rendered:
`F2`, `F3`, `F11` and `F12` are reserved in the map for later phases and are deliberately absent from
the bars until they do something, because a button that does nothing teaches an operator that the bar
lies.

### 5. Global keys are routed on preview, in one place

All global handling is in `ShellWindow.OnPreviewKeyDown`. Preview is the point: a `DataGrid` or
`TextBox` will swallow Escape, Ctrl+A or the arrows before a bubbling handler sees them. This is
code-behind, which the conventions otherwise forbid; it qualifies as interaction logic because every
branch either forwards to a command or opens a window, and no decision about what a screen does is
taken there.

### 6. The visual language stays ours

`project.md` §1 is explicit: match TallyPrime on workflows, **do not copy their UI or code**. We take
the interaction grammar — menu-driven home, accelerator letters, keyboard-first entry, an Escape
back-stack, contextual key bars — and none of the pixels. The palette, wording and layout proportions
are original. No Tally marks, screenshots or resources are in the repository.

## Consequences

- `Ctrl+Q` now **exits the screen** and `Alt+X` cancels, correcting the earlier binding. Anyone who
  learned the old meaning has to relearn one key; leaving it wrong would have meant diverging from
  Tally forever.
- The shell no longer selects a screen at startup, so `ShellViewModel` tests that asserted "opens on
  the first screen" were rewritten to assert "opens on the Gateway".
- Screens inherit the chrome for free. Reworking each screen's *internals* to Tally's dense
  entry patterns is deliberately **not** in this batch — see below.
- Accelerator allocation depends on screen order within a module, so adding a screen can re-letter
  its neighbours until they claim letters.

## Scope of this batch

Shell and patterns only, at the owner's direction. Delivered: the five bands, the Gateway with
accelerators, the key map, both bars, the `IScreenShortcuts` contract, and migration of every screen
onto the new chrome.

**Not delivered:** the per-screen rework — dense keyboard entry grids, voucher-type switching on
F4–F9 inside voucher entry, create-on-the-fly with `Alt+C`, pop-up list selectors on lookup fields,
`Ctrl+Enter` drill-down in reports. Those need the screens themselves reworked and are the next
batch.

## Incidental defect found and fixed

Twenty-four view files and one SQL statements file contained **double-encoded text**: an em-dash had
been UTF-8 encoded, the bytes re-read as CP1252, and the result encoded again, so screens displayed
`Ledgers â€" grouped by...`. Six distinct corrupt sequences were identified by codepoint and reversed
(`—`, `…`, `₹`, `↓`, `×`, `§`). This predates this batch and was visible on most screens.
