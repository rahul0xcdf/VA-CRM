# Tally entry patterns inside screens

The inside of a screen, settled: lookup fields that filter as you type and create the master you
cannot find, voucher lines that are typed straight through without a mouse, function keys that switch
the voucher type, and `Ctrl+Enter` drill-down on the reports that have somewhere to go.

ADR-0006 rebuilt the shell around those screens and deliberately left their internals alone.
**ADR-0007** is this batch, and it is binding on every screen built from Phase 7 onward.

## User workflow

### Typing a voucher

1. Open **Voucher entry** and press **Load lists**.
2. Press `F4`–`F9` to choose the voucher type. Only types your company actually has are offered, and
   the current one is highlighted on the right-hand bar. Lines you have already typed are kept.
3. In the **Ledger** field, type any part of the name. The list filters on every keystroke and matches
   anywhere in the name; names that *start* with what you typed are listed first.
4. `Enter` takes the highlighted ledger and moves to **Debit**. `Enter` again moves to **Credit**,
   then **Line narration**, then onto the next line — adding one when you run off the end.
5. Watch **Difference** in the totals band. `Ctrl+A` posts, and stays disabled until it reads zero.

`Ctrl+D` removes the line the caret is in. `Alt+X` clears the form. `Alt+D` cancels a posted voucher
— a different act, deliberately on a different key.

### Creating a master you find is missing

Press `Alt+C` while in a lookup field. A band opens at the foot of the screen, pre-named with what you
typed, with your half-typed voucher still visible above it.

- `Ctrl+A` saves the master **and puts it straight onto the line you were filling**.
- `Alt+X` abandons it. The voucher is untouched either way.

Available on the ledger field (Voucher entry) and the item field (Stock vouchers, Sales &amp; purchase).

### Reading a report

`Ctrl+Enter` on the highlighted row opens a pane underneath. The pane is closed until you press the
key. See `docs/user-guide/keyboard-shortcuts.md` for what each report drills into.

### Master screens

The same four keys everywhere: `Alt+C` new, `Ctrl+A` save, `Alt+D` delete, `Alt+X` put the editor
back to the selected record. Where a record cannot be deleted — customers, HSN/SAC codes — `Alt+D`
deactivates instead and the bar says "Deactivate", not "Delete".

## Developer notes

| Piece | Where | What it is |
|---|---|---|
| `LookupBox` | `Presentation/Controls/LookupBox.cs` | Templated control: text box + filtered pop-up list + `Alt+C`. Replaces `ComboBox` on every master-picking field |
| `LookupFilter` | `Presentation/Controls/LookupFilter.cs` | The ranking rule, as a plain static class so it is testable without a window |
| `EntryGrid` | `Presentation/Controls/EntryGrid.cs` | Attached behaviour on an `ItemsControl`: Enter walks the line, appends a row at the end, reports the focused line back through `CurrentItem` |
| `EntryGridNavigator` | `Presentation/Controls/EntryGridNavigator.cs` | The Enter-key arithmetic, separated for the same reason |
| `VoucherTypeKeyMap` | `Presentation/Keyboard/VoucherTypeKeyMap.cs` | Which key means which voucher / document / stock purpose |
| `Entry.xaml` | `Presentation/Themes/Entry.xaml` | The `LookupBox` template and the entry-line styles |

### Building a new screen

1. Implement `IScreenShortcuts`. Take gestures from `AppShortcuts` — never construct an `AppShortcut`,
   and never bind a key anywhere but your own bars, because those bars are the only place
   `ShellWindow` looks.
2. Declare a key **only** where it works. A permanently disabled bar entry teaches the operator that
   the bar lies.
3. Raise `PropertyChanged` for `ButtonBar` / `ActionBar` / `StatusBarHints` whenever a bar's content
   changes. The shell re-reads them; because it also routes keys from them, a stale bar fires the
   wrong command rather than merely looking wrong.
4. Any field that picks a master is a `LookupBox`. Any grid of enterable lines is an `ItemsControl`
   with `EntryGrid.Enable="True"`, not an editable `DataGrid` — a `DataGrid` is still right for a
   read-only list.
5. Lines carry the selected **object**, not its id.
6. If a behaviour has a rule, put the rule somewhere a unit test can reach without a window.

### Gotcha

`VinayakaAgencies.App.Presentation.Keyboard` shadows WPF's `Keyboard` class inside this assembly.
Write `System.Windows.Input.Keyboard` out in full where you need it.

## Verification

`tests/VinayakaAgencies.App.Tests` — `LookupFilterTests`, `EntryGridNavigatorTests`,
`VoucherTypeKeyMapTests`, `VoucherEntryShortcutTests`, the ADR-0007 additions to
`ShellViewModelTests`, and `ScreenViewLoadTests.LookupBox_ResolvesItsDefaultStyleFromTheApplicationDictionaries`.

Full suite green at 787 tests, 0 warnings under warnings-as-errors. Smoke: the application starts,
reaches "Shell window opened" and logs no error row.

**Not yet done:** owner walkthrough and screenshots. See ADR-0007's verification section for what each
test pins down.
