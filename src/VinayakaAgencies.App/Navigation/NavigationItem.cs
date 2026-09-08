namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// One selectable entry in the shell's home menu: its owning module, its screen, the module's
/// 1-based number for digit-key jumps, and the entry's key label ("A", "B", …) shown in the menu.
/// </summary>
/// <remarks>
/// ADR-0005: modules are numbered in display order ("1 Masters", "2 Vouchers", …) and screens
/// inside a module carry letters ("A Ledgers"). The numbers are what the digit keys route to;
/// the letters are presentation only.
/// </remarks>
public sealed record NavigationItem(
    string Group,
    ScreenDescriptor Screen,
    int ModuleNumber,
    string ScreenKey)
{
    /// <summary>The home-menu group caption: "1 Masters", "2 Vouchers", …</summary>
    public string ModuleLabel => $"{ModuleNumber} {Group}";
}
