namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// One selectable entry in the shell's navigation pane: its owning group's name and its screen.
/// </summary>
public sealed record NavigationItem(string Group, ScreenDescriptor Screen);
