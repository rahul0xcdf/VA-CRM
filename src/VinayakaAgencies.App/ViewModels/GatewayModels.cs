using VinayakaAgencies.App.Navigation;
using VinayakaAgencies.App.Presentation.Keyboard;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// One entry on the Gateway menu: a screen, plus the accelerator letter it answers to (ADR-0006).
/// </summary>
/// <param name="Hotkey">The label split around its accelerator, ready to render.</param>
/// <param name="Target">The screen this entry opens.</param>
public sealed record GatewayItem(MenuHotkey Hotkey, NavigationItem Target)
{
    public string Header => Target.Screen.Header;
}

/// <summary>
/// A titled block of the Gateway menu — one per module (ADR-0006).
/// </summary>
/// <remarks>
/// Sections group visually but do <em>not</em> scope accelerators: the whole Gateway is one menu, so a
/// letter is unique across every section. Scoping per section would mean the same key doing different
/// things depending on where the eye happened to be, which is precisely the confusion accelerators
/// exist to avoid.
/// </remarks>
/// <param name="Title">Section heading, e.g. "MASTERS".</param>
/// <param name="Items">Entries in display order.</param>
public sealed record GatewaySection(string Title, IReadOnlyList<GatewayItem> Items);
