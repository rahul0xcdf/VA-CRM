# Keyboard shortcut map

ADR-0005 makes keyboard-first operation the supported norm: every mouse action has a key
equivalent. This document is the central map — when a screen adds a shortcut, it is registered
here so keys stay consistent.

## Global shortcuts (work everywhere in the shell)

| Key | Action |
|---|---|
| `Alt+G` | Open the **Go To** palette: type to filter every registered screen, `↑`/`↓` to move, `Enter` to open, `Esc` to close |
| `Esc` | Go **back** one level in the navigation history stack (no-op when the stack is empty) |
| `1`–`9` | Jump to the numbered module in the home menu ("1 Masters", "2 Vouchers", …). Ignored while a text box owns the focus |

## Screen shortcuts (`IScreenShortcuts`)

Screens that implement `IScreenShortcuts` get these two keys; the shell's status bar shows the
focused screen's hints when it declares any.

| Key | Action | Screens wired today |
|---|---|---|
| `Ctrl+A` | **Accept / save** the current operation | Voucher Entry → *Post voucher*; Documents → *Save draft* |
| `Ctrl+Q` | **Cancel / close** the current operation | Voucher Entry → *Cancel voucher*; Documents → start a new document (clears staging) |

Note: because `Ctrl+A` means "accept", text boxes do not offer select-all with `Ctrl+A`;
use mouse selection or shift-arrow keys instead.

## Grids and lists

| Key | Action |
|---|---|
| `↑` / `↓` | Move the row selection in DataGrids and the home menu |
| `Enter` | In the Go To palette, open the highlighted screen |

## Reserved for later phases

- **F-keys** are reserved for selecting voucher types in voucher-entry contexts (Tally muscle
  memory) and must not be bound elsewhere.
- Additional per-screen shortcuts belong in this table, not in per-view help text.
