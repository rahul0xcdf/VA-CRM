# Keyboard shortcut map

ADR-0006 makes keyboard-first operation the supported norm: every mouse action has a key
equivalent. This is the central map. The keys follow TallyPrime's published shortcuts so that
existing muscle memory carries over.

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
| `Alt+X` | **Cancel** the current operation |
| `Alt+D` | **Delete** the current master or voucher |
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

## Where the keys are shown

Two surfaces advertise what is available, so nothing has to be memorised first:

- The **right-hand button bar** shows the contextual keys for the screen you are on.
- The **bottom action bar** shows Accept, Cancel, Exit and anything else the screen offers.

Both are generated from the same objects that carry the commands, so a bar can never show a key that
is not wired up. A key the screen cannot currently act on appears **dimmed** rather than vanishing —
a button that disappears leaves you wondering where it went.

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
