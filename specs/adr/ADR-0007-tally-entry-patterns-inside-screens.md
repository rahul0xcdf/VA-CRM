# ADR-0007 — Tally entry patterns inside screens: lookups, live line grids, and voucher keys

```yaml
Status: Accepted
Date: 2026-09-18
Decided by: Project owner (directive: "let's do the per screen rework and finish it off completely.
             And for the next phases it should follow this")
Supersedes: nothing
Amends: ADR-0006 — completes the batch it deferred
Related: ADR-0005 (superseded shell), project.md §1 (inspiration, not clone)
Binding on: every screen built from Phase 7 onward
```

## Context

ADR-0006 rebuilt the shell: five bands, a Gateway that is entered and escaped from, accelerator
letters, one central key map, two generated bars. It explicitly did **not** rework the inside of any
screen, and listed what was left:

> **Not delivered:** the per-screen rework — dense keyboard entry grids, voucher-type switching on
> F4–F9 inside voucher entry, create-on-the-fly with `Alt+C`, pop-up list selectors on lookup fields,
> `Ctrl+Enter` drill-down in reports.

Measured before starting this batch, the gap was worse than that list suggests:

- **The voucher-type keys were declared and dead.** `AppShortcuts` held F4–F9, Alt+F5–F9, Ctrl+F8–F9,
  `Alt+C`, `Ctrl+Enter` and `Ctrl+D`. Grepping the whole solution for any use of them outside the
  declaration file returned nothing. The shell routed six gestures; the other thirty were documentation.
- **Every lookup was a `ComboBox`.** Ledger, item, customer, godown. A `ComboBox` matches on first
  letter only, so reaching "Vinayaka Motors" meant typing V, getting whichever V came first, and
  taking the mouse. Phase 2's checkpoint C2.6 requires the item master to stay usable at 5,000 SKUs.
- **Every line grid was a `DataGrid`** whose cells enter edit mode on click — a mouse gesture per
  field — and whose Enter key moves *down* a column rather than across a line.
- **`IScreenShortcuts` documented live bars and nothing implemented them.** `ShellViewModel` composed
  the bars once, on navigation, and never subscribed to the screen. Since the shell also *routes*
  keys by walking those bars, a screen that changed state kept firing the commands it had at birth.
- **Two master screens did not load the selected row into their editor** (stock groups, godowns), so
  selecting a row and pressing Save renamed whatever the editor still held.

## Decision

### 1. Lookups are a control, and the ranking rule is testable on its own

`Presentation/Controls/LookupBox.cs` is a templated control: a text box with a list under the caret,
filtering on every keystroke, `Alt+C` on the drop-down's footer to create the master that turns out
to be missing.

The ranking rule lives in `LookupFilter`, a plain static class, because the control needs a window
and an STA thread to exist at all while the rule an operator actually feels — which ledger is under
the highlight after three letters — is arithmetic over strings. **Prefix matches come before interior
matches**, each keeping the caller's order. Prefix-first is what makes blind typing safe: typing "ca"
for Cash must not put "Petty Cash" under the highlight because it happens to contain the letters.

The same separation applies to `EntryGridNavigator` versus `EntryGrid`, and it is the general rule:
**if a behaviour has a rule, the rule goes somewhere a unit test can reach without a window.**

### 2. A voucher line is a row of live fields, not a grid of cells you click into

`EntryGrid` is an attached behaviour on an `ItemsControl`. Enter walks across the fields of a line
and wraps onto the next, adding a line when it runs out, so a whole voucher is typed without the hand
leaving the keyboard or a line ever being added on purpose. A behaviour rather than code-behind
because six screens need it and six copies would drift.

The remove-line button is deliberately **not** an entry stop. Stopping the caret on it would put a
destructive action one stray Enter away in the middle of typing.

### 3. The focused lookup gets first refusal on every keystroke

`ShellWindow.OnPreviewKeyDown` claims Escape, Enter and the rest on the way down the tree, which is
right — a `DataGrid` would otherwise swallow them. But inside a field with an open drop-down the
global meanings are wrong: Escape closes the list rather than leaving the screen, and Enter takes the
highlighted master. Rather than scatter exceptions through the shell, the focused lookup publishes
itself as `LookupBox.Focused` and the shell offers it each keystroke first through `TryHandleKey`.

`TryHandleKey` returns **false** for anything the field has no use for, so those keys still reach the
shell. A field that swallowed keys silently would make Escape stop working at random points in a
voucher.

### 4. Bars are live, because the shell routes keys from them

`ShellViewModel` now subscribes to the active screen's `PropertyChanged` and recomposes when
`ButtonBar`, `ActionBar` or `StatusBarHints` changes. This is not cosmetic. Voucher entry advertises
one key per voucher type and marks the current one; a bar frozen at construction would show the keys
of whichever type loaded first and go on showing them after F5 was pressed — and because the bar is
the routing table, the wrong bar means the wrong command fires.

`ShortcutButton` gained `CommandParameter` and `IsCurrent`. The parameter is what lets eight voucher
keys share one command instead of existing as eight commands closing over eight constants.

### 5. Sub-screens own Accept while they are open

When the create-a-ledger band is open, `Ctrl+A` saves the ledger and `Alt+X` abandons it; the voucher
underneath is untouched and stays visible. This is Tally's behaviour, and the alternative is an
operator finishing a master and posting a half-typed voucher instead.

A band at the foot of the page, never a modal dialog: an operator who cannot see their work while a
dialog sits over it does not trust that it survived.

### 6. One key changed meaning on voucher entry

`Alt+X` used to cancel the **posted** voucher. In Tally `Alt+X` abandons what is being typed and
`Alt+D` deletes the record, and those are very different acts to have behind the reflex key —
cancelling a posted voucher moves figures out of every statement. `Alt+X` now clears the form;
`Alt+D` cancels the voucher.

### 7. A key is advertised where it works, and nowhere else

ADR-0006 established that the bars only show keys that do something. This batch applies it per
screen and per state:

- F4–F9 appear on voucher entry only for voucher types the company actually has, and a deactivated
  type loses its key.
- Godowns and account groups get no `Alt+D`, because neither service offers a delete.
- Customers and HSN/SAC codes get `Alt+D` = **deactivate**, because that is the destructive act they
  do have — a customer is 1:1 with a ledger every posting path writes through, and an HSN code that
  has priced an invoice cannot be removed without orphaning the tax snapshot on it.
- `Ctrl+P` and `Ctrl+E` appear on the customer statement alone, because it is the only screen that
  has both.
- A quotation gets **no** dedicated key: TallyPrime publishes none, so inventing one would teach a
  key that does not transfer and would spend a gesture a later phase may need. F10 covers it.

### 8. One idea per gesture, across screens

`Alt+F8` selects the delivery challan on Sales & Purchase and the stock **issue** on the stock
voucher; `Alt+F9` selects the goods receipt and the stock **receipt**. This is not a second meaning:
on both screens `Alt+F8` is "goods going out" and `Alt+F9` is "goods coming in". ADR-0006's rule
forbids a gesture meaning two *different* things depending on where the eye is; keeping one idea on
one key across screens is what the rule exists to protect.

### 9. Reports drill on `Ctrl+Enter`, and only where there is somewhere to go

Trial balance, profit &amp; loss, balance sheet, day book and the edit log each open a pane under
themselves. The pane is collapsed until the key is pressed, so the report has the whole page for its
numbers until the operator asks a question of one.

The trial balance previously drilled on **selection change**, which ran a ledger-statement query per
arrow-key press — the report got slower the further down it anybody looked. Moving it to `Ctrl+Enter`
is both Tally's key and the fix.

Reports with no drill target — number gaps, diagnostics, outstanding orders, stock reports — get no
`Ctrl+Enter`, for the same reason the bars stay honest everywhere else.

## Consequences

- **Screens carry objects, not ids.** A voucher line holds a `LedgerSummary`, a stock line holds a
  `GodownSummary`, a document line holds a `GodownSummary`. A lookup selects an object, and keeping
  the id would mean resolving the name back on every render to show what the row already knows.
- **Two screens changed behaviour beyond their keys**: stock groups and godowns now load the selected
  row into the editor. Alt+X meaning "put the editor back to the selected record" is incoherent
  otherwise, and both screens were quietly renaming the wrong row before.
- `StockVoucherViewModel` and `TradeDocumentsViewModel` keep their **server-side** item search. The
  lookup types into that search and ranks the hundred rows it returns; loading 5,000 items into the
  client to filter them would trade C2.6 for a worse problem.
- The `Presentation.Keyboard` namespace shadows WPF's `Keyboard` class inside this assembly, so
  `System.Windows.Input.Keyboard` must be written out where it is used.

## What this binds for later phases

Every screen built from Phase 7 onward is one of four shapes — master list, voucher, report, settings
form — and each shape now has a settled inside:

| Shape | Accept (Ctrl+A) | Cancel (Alt+X) | Other keys |
|---|---|---|---|
| Master list | Save; a create when nothing is selected, an update when something is | Reload the selected record | `Alt+C` new, `Alt+D` delete **or** deactivate — whichever the service actually offers |
| Voucher | Post | Clear the entry | Voucher-type keys from `VoucherTypeKeyMap`, `Alt+C` create the missing master, `Ctrl+D` remove the line |
| Report | — (nothing to accept) | — | `Ctrl+Enter` drill, **only** where there is a drill; `Ctrl+P` / `Ctrl+E` only where they exist |
| Settings | Save | Discard and re-read | — |

And four rules that are not negotiable per screen:

1. **Any field that picks a master is a `LookupBox`**, never a `ComboBox`.
2. **Any grid of enterable lines is an `ItemsControl` with `EntryGrid.Enable`**, never an editable
   `DataGrid`. A `DataGrid` is still right for a read-only list.
3. **Gestures come from `AppShortcuts`.** A screen never constructs an `AppShortcut`, and never binds
   a key anywhere but its own bars — which is the only place `ShellWindow` looks.
4. **A key is declared only where it works.** A permanently disabled bar entry teaches the operator
   that the bar lies.

## Verification

`tests/VinayakaAgencies.App.Tests`, 118 tests, all green with the rest of the suite (787 total,
0 warnings under warnings-as-errors):

- `LookupFilterTests` — prefix-before-interior, source order preserved, case-insensitive, exact match
  beats a longer candidate, ambiguity resolves to nothing rather than guessing.
- `EntryGridNavigatorTests` — Enter walks a two-line voucher's eight cells in reading order and then
  asks for a third line; out-of-range input holds position rather than throwing.
- `VoucherTypeKeyMapTests` — every assignment pinned against TallyPrime's published keys; quotation
  has none; every mapped gesture is one `AppShortcuts` declares; `Alt+C` is pinned against the value
  `LookupBox` hard-codes, so the widget and the key policy cannot drift.
- `VoucherEntryShortcutTests` — the button bar carries one key per voucher type the company has and
  omits deactivated ones, marks the current type, keeps typed lines across a type switch, hands
  Accept to the create-a-ledger band, and puts a ledger created with `Alt+C` straight onto the line
  the caret was in.
- `ShellViewModelTests` — bars are re-read when a screen raises `PropertyChanged`, stop following a
  screen once it is navigated away from, and `ActiveAcceptCommand` follows a screen into a sub-screen.
- `ScreenViewLoadTests` — every registered view still constructs against the real dictionaries, and
  the `LookupBox` default style resolves. Both were confirmed to fail when the style was removed; a
  `LookupBox` with no style constructs happily and renders as an empty rectangle, so nothing else
  would have caught it.

Smoke: the application starts, reaches "Shell window opened", and logs no `[ERR]` or `[FTL]` row.
