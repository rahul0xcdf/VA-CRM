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
