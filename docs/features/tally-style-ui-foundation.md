# Tally Prime-inspired UI foundation

**Phase 5 batch addition · ADR-0005** (`specs/adr/ADR-0005-tally-inspired-ui-and-keyboard-navigation.md`)

## Feature

One shared presentation layer for every screen: light flat theme (white/near-white surfaces,
navy text, single green accent), persistent header showing company, GSTIN and active FY period,
a numbered left Home menu of modules, and a bottom status bar showing mode, period and the
shortcuts available on the focused screen. Keyboard-first operation following Tally muscle
memory without copying trade dress.

## User workflow

- **Alt+G** opens the Go To palette — type to filter all screens, Enter to jump.
- **Esc** walks back one level in the navigation stack.
- **Ctrl+A** accepts/saves, **Ctrl+Q** cancels/closes on screens that support it.
- **1–9** jump straight to a module in the home menu.
- The full map lives in `docs/user-guide/keyboard-shortcuts.md`.

## Developer notes

- Theme dictionaries: `src/VinayakaAgencies.App/Presentation/Themes/{Colors,Typography,Controls}.xaml`,
  merged in `App.xaml`. Consumable keys are `<Role>Brush` / `FontSize.*`; no view may hard-code
  brushes any more.
- Shell: `Views/ShellWindow.xaml` + `ShellViewModel` (header fields, navigation history stack,
  module-number selection, status-bar hints). Palette window `Views/GoToPaletteWindow.xaml`.
- Screens opt into Ctrl+A/Ctrl+Q by implementing `ViewModels/IScreenShortcuts`.
- All existing views migrated onto the dictionaries; navigation registrations unchanged;
  no new NuGet dependencies.

## Verification

- `ScreenModuleCatalogTests` / `ShellViewModelTests` extended and green; App build clean with
  warnings-as-errors at implementation.

---

## Superseded by ADR-0006 (17 Sep 2026)

The shell this document described — a themed window with a permanent sidebar and three shortcuts —
was judged unusable by the owner and replaced. What ADR-0005 intended is unchanged; what it produced
is gone.

The shell is now five bands (top menu, company bar, body, action bar, status line) with a **Gateway**
menu that is entered and escaped from rather than a sidebar that is always on. Entries carry
accelerator letters shown highlighted inside the word. The keyboard map moved into code at
`Presentation/Keyboard/AppShortcut.cs` and covers TallyPrime's published shortcuts; a test forbids
duplicate gestures.

One binding changed meaning: **`Ctrl+Q` now exits the screen and `Alt+X` cancels**, matching Tally.
The old shell had `Ctrl+Q` cancelling.

See `specs/adr/ADR-0006-tally-shell-gateway-and-keyboard-map.md` and the rewritten
`docs/user-guide/keyboard-shortcuts.md`. Per-screen internals — dense entry grids, F4–F9 voucher
switching, `Alt+C` create-on-the-fly, pop-up selectors — are the next batch, not this one.
