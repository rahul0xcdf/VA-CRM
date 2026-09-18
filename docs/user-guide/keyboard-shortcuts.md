# Keyboard shortcut map

ADR-0006 makes keyboard-first operation the supported norm: every mouse action has a key
equivalent. ADR-0007 wired it inside the screens — voucher-type switching, lookup fields, live line
grids and report drill-down. This is the central map. The keys follow TallyPrime's published
shortcuts so that existing muscle memory carries over.

**The map lives in code**, at `src/VinayakaAgencies.App/Presentation/Keyboard/AppShortcut.cs`. A test
asserts no two actions share a gesture, so this document and the application cannot drift apart
without the build failing. When a screen adds a shortcut, it goes in that file and in this table.

## Getting around

| Key | Action |
|---|---|
| **highlighted letter** | Open that Gateway entry. Each entry's accelerator is shown highlighted inside its name — `Ledgers` is **L**, `Chart of Accounts` is C**h**art because C is already taken |
| `Alt+G` | **Go To** — type to filter every screen by name, `↑`/`↓` to move, `Enter` to open |
| `Ctrl+G` | **Switch To** — jump to a related screen without losing your place |
| `Esc` | **Back** one level. From a screen it returns toward the Gateway, and at the Gateway it does nothing — Esc never closes the application |
| `Ctrl+Q` | **Exit** the current screen |
| `F1` | **Help** |

## Doing things

| Key | Action |
|---|---|
| `Ctrl+A` | **Accept** — save what the screen is editing |
| `Alt+X` | **Cancel** — abandon what you are typing. It never touches anything already saved |
| `Alt+D` | **Delete** the current master or voucher. On screens where the record cannot be deleted — customers, HSN/SAC codes — this **deactivates** it instead, and the bar says so |
| `Ctrl+D` | **Remove line** from a voucher |
| `Alt+C` | **Create a master on the fly**, without leaving the field you are filling |
| `Alt+F` | **Find** a master or transaction |
| `Ctrl+P` | **Print** the current item |
| `Ctrl+E` | **Export** the current item |

> **`Ctrl+A` means Accept, not select-all.** That is Tally's convention and it is deliberate here too,
> so text boxes do not offer select-all on `Ctrl+A` — use shift-arrows or the mouse.

## Reports and lists

| Key | Action |
|---|---|
| `Ctrl+Enter` | Drill into the highlighted row |
| `Alt+Enter` | Expand or collapse the highlighted group |
| `Page Up` | Previous record |
| `Page Down` | Next record |
| `↑` / `↓` | Move the row selection |

`Ctrl+Enter` opens a pane under the report. It stays closed until you press the key, so the report
keeps the whole page until you ask a question of it.

| Report | `Ctrl+Enter` shows |
|---|---|
| Trial balance | The ledger's statement for the financial year, up to the as-on date |
| Profit &amp; loss | The ledger's postings over the same period |
| Balance sheet | The ledger's postings this financial year; anything earlier is the opening balance |
| Day book | Every line of the highlighted row's voucher — the day book lists lines, so a payment shows as two rows pages apart |
| Edit log | The highlighted row's before and after images in full, untruncated |
| GST rate schedules | That code's full rate history |

Reports with nothing to drill into — number gaps, diagnostics, outstanding orders, stock reports —
deliberately do not show the key.

## Voucher types

Reserved on the bare function keys, exactly as in Tally. `F10` opens the full list.

| Key | Voucher | | Key | Voucher |
|---|---|---|---|---|
| `F4` | Contra | | `Alt+F5` | Debit note |
| `F5` | Payment | | `Alt+F6` | Credit note |
| `F6` | Receipt | | `Alt+F7` | Stock journal |
| `F7` | Journal | | `Alt+F8` | Delivery note |
| `F8` | Sales | | `Alt+F9` | Receipt note |
| `F9` | Purchase | | `Ctrl+F8` | Sales order |
| `F10` | Other vouchers | | `Ctrl+F9` | Purchase order |

On **Voucher entry** these switch the voucher being typed. Only the types your company actually has
are shown, and the one you are in is highlighted on the bar. **Your typed lines are kept** — pressing
F6 after starting on F5 changes the voucher type and nothing else.

On **Sales &amp; purchase**, `F8`/`F9` raise a sales or purchase invoice, `Ctrl+F8`/`Ctrl+F9` an order,
`Alt+F8`/`Alt+F9` a delivery note or receipt note, and `F10` a quotation (Tally publishes no key for
one). The kind can only be changed while staging a **new** document — a saved one already has its
number.

On **Stock vouchers**, `Alt+F9` is a receipt, `Alt+F8` an issue, `Alt+F7` a transfer and `F10` an
opening balance. `Alt+F8` and `Alt+F9` mean the same thing here as on Sales &amp; purchase: goods out
and goods in.

## Filling a voucher without the mouse

| Key | In a line grid |
|---|---|
| `Enter` | Move to the next field; at the end of a line, to the start of the next; at the end of the last line, **add a line and go to it** |
| `Tab` / `Shift+Tab` | Move between fields without adding anything |
| `Ctrl+D` | Remove the line the caret is in |

| Key | In a lookup field (ledger, item, customer, godown) |
|---|---|
| *type anything* | Filters as you type, matching **anywhere** in the name — "motors" finds "Vinayaka Motors" |
| `↓` / `↑` | Move down and up the list |
| `Page Down` / `Page Up` | Jump ten rows |
| `Enter` | Take the highlighted row **and move to the next field** |
| `Esc` | Close the list, keeping what was already selected |
| `Alt+C` | **Create the master you cannot find**, pre-named with what you typed |

Names that start with what you typed are listed before names that merely contain it, so typing three
letters and pressing `Enter` is safe without looking.

`Alt+C` opens a small band at the foot of the screen — never a dialog — with your half-typed voucher
still visible above it. `Ctrl+A` saves the new master and puts it straight onto the line you were
filling; `Alt+X` abandons it and leaves the voucher untouched.

## Where the keys are shown

Two surfaces advertise what is available, so nothing has to be memorised first:

- The **right-hand button bar** shows the contextual keys for the screen you are on.
- The **bottom action bar** shows Accept, Cancel, Exit and anything else the screen offers.

Both are generated from the same objects that carry the commands, so a bar can never show a key that
is not wired up. A key the screen cannot currently act on appears **dimmed** rather than vanishing —
a button that disappears leaves you wondering where it went.

The bars follow the screen's state. On voucher entry the voucher type you are in is highlighted; when
`Alt+C` opens the create-a-master band, `Ctrl+A` and `Alt+X` switch to saving and abandoning **that**,
and the bar changes to say so.

## Reserved, not yet active

These are claimed in the key map so nothing else takes them, but they are not wired yet and are
deliberately **not** shown on the bars until they work.

| Key | Reserved for | Phase |
|---|---|---|
| `F2` | Change the working date | later |
| `Alt+F2` | Change the reporting period | later |
| `F3` | Company details | later |
| `Alt+K` | Company menu | later |
| `F11` | Company features | later |
| `F12` | Screen configuration | later |
