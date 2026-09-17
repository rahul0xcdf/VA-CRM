using System.Windows.Input;

namespace VinayakaAgencies.App.Presentation.Keyboard;

/// <summary>
/// One keyboard gesture and the label the chrome shows for it (ADR-0006).
/// </summary>
/// <remarks>
/// The display string is part of the gesture rather than something each bar formats for itself.
/// Tally users read the key off the button bar and press it; if the label and the binding could drift
/// apart, the bar would be teaching the wrong key — which is worse than showing nothing.
/// </remarks>
/// <param name="Key">The key.</param>
/// <param name="Modifiers">Modifiers that must be held.</param>
/// <param name="Display">How the gesture is written on screen, e.g. <c>Alt+F2</c>.</param>
public sealed record AppShortcut(Key Key, ModifierKeys Modifiers, string Display)
{
    /// <summary>True when this gesture matches a key event.</summary>
    public bool Matches(Key key, ModifierKeys modifiers) =>
        Key == key && Modifiers == modifiers;

    public override string ToString() => Display;
}

/// <summary>
/// The application's keyboard map, in one place (ADR-0006).
/// </summary>
/// <remarks>
/// <para>
/// These follow TallyPrime's published shortcuts so that an operator's muscle memory carries over.
/// Keys are an interface convention, not an appearance: adopting them is what `project.md` §1 means by
/// matching on workflow. The visual language stays ours.
/// </para>
/// <para>
/// Everything lives here rather than in per-view XAML <c>InputBinding</c>s for one blunt reason: the
/// same gesture must never mean two things on two screens. A central table can be read end to end and
/// checked; bindings scattered across thirty views cannot. <c>docs/user-guide/keyboard-shortcuts.md</c>
/// is generated from this list's shape and must be updated alongside it.
/// </para>
/// </remarks>
public static class AppShortcuts
{
    // --- Navigation and chrome ---

    /// <summary>Open the Go To palette — jump to any screen.</summary>
    public static readonly AppShortcut GoTo = new(Key.G, ModifierKeys.Alt, "Alt+G");

    /// <summary>Switch the current screen for a related one, keeping the place in the workflow.</summary>
    public static readonly AppShortcut SwitchTo = new(Key.G, ModifierKeys.Control, "Ctrl+G");

    /// <summary>Back one level. At the Gateway this is a no-op rather than an exit.</summary>
    public static readonly AppShortcut Back = new(Key.Escape, ModifierKeys.None, "Esc");

    /// <summary>Context help.</summary>
    public static readonly AppShortcut Help = new(Key.F1, ModifierKeys.None, "F1");

    /// <summary>Change the working date.</summary>
    public static readonly AppShortcut ChangeDate = new(Key.F2, ModifierKeys.None, "F2");

    /// <summary>Change the reporting period.</summary>
    public static readonly AppShortcut ChangePeriod = new(Key.F2, ModifierKeys.Alt, "Alt+F2");

    /// <summary>Company details.</summary>
    public static readonly AppShortcut Company = new(Key.F3, ModifierKeys.None, "F3");

    /// <summary>Company menu.</summary>
    public static readonly AppShortcut CompanyMenu = new(Key.K, ModifierKeys.Alt, "Alt+K");

    /// <summary>Feature settings for the company.</summary>
    public static readonly AppShortcut Features = new(Key.F11, ModifierKeys.None, "F11");

    /// <summary>Configuration for the current screen.</summary>
    public static readonly AppShortcut Configure = new(Key.F12, ModifierKeys.None, "F12");

    // --- Actions ---

    /// <summary>Accept and save. Deliberately shadows select-all, exactly as Tally does.</summary>
    public static readonly AppShortcut Accept = new(Key.A, ModifierKeys.Control, "Ctrl+A");

    /// <summary>Cancel the thing being edited.</summary>
    public static readonly AppShortcut Cancel = new(Key.X, ModifierKeys.Alt, "Alt+X");

    /// <summary>Delete the current master or voucher.</summary>
    public static readonly AppShortcut Delete = new(Key.D, ModifierKeys.Alt, "Alt+D");

    /// <summary>Remove the focused line from a voucher.</summary>
    public static readonly AppShortcut RemoveLine = new(Key.D, ModifierKeys.Control, "Ctrl+D");

    /// <summary>Leave the screen without saving.</summary>
    public static readonly AppShortcut Exit = new(Key.Q, ModifierKeys.Control, "Ctrl+Q");

    /// <summary>Create a master without leaving the field being filled.</summary>
    public static readonly AppShortcut CreateOnTheFly = new(Key.C, ModifierKeys.Alt, "Alt+C");

    /// <summary>Find a master or transaction.</summary>
    public static readonly AppShortcut Find = new(Key.F, ModifierKeys.Alt, "Alt+F");

    /// <summary>Print the current item.</summary>
    public static readonly AppShortcut Print = new(Key.P, ModifierKeys.Control, "Ctrl+P");

    /// <summary>Export the current item.</summary>
    public static readonly AppShortcut Export = new(Key.E, ModifierKeys.Control, "Ctrl+E");

    // --- Reports ---

    /// <summary>Drill into the highlighted row.</summary>
    public static readonly AppShortcut DrillDown = new(Key.Enter, ModifierKeys.Control, "Ctrl+Enter");

    /// <summary>Expand or collapse the highlighted group.</summary>
    public static readonly AppShortcut ToggleGroup = new(Key.Enter, ModifierKeys.Alt, "Alt+Enter");

    /// <summary>Previous record.</summary>
    public static readonly AppShortcut Previous = new(Key.PageUp, ModifierKeys.None, "Page Up");

    /// <summary>Next record.</summary>
    public static readonly AppShortcut Next = new(Key.PageDown, ModifierKeys.None, "Page Down");

    // --- Voucher types (F10 opens the rest) ---

    public static readonly AppShortcut Contra = new(Key.F4, ModifierKeys.None, "F4");
    public static readonly AppShortcut Payment = new(Key.F5, ModifierKeys.None, "F5");
    public static readonly AppShortcut Receipt = new(Key.F6, ModifierKeys.None, "F6");
    public static readonly AppShortcut Journal = new(Key.F7, ModifierKeys.None, "F7");
    public static readonly AppShortcut Sales = new(Key.F8, ModifierKeys.None, "F8");
    public static readonly AppShortcut Purchase = new(Key.F9, ModifierKeys.None, "F9");
    public static readonly AppShortcut DebitNote = new(Key.F5, ModifierKeys.Alt, "Alt+F5");
    public static readonly AppShortcut CreditNote = new(Key.F6, ModifierKeys.Alt, "Alt+F6");
    public static readonly AppShortcut StockJournal = new(Key.F7, ModifierKeys.Alt, "Alt+F7");
    public static readonly AppShortcut DeliveryNote = new(Key.F8, ModifierKeys.Alt, "Alt+F8");
    public static readonly AppShortcut ReceiptNote = new(Key.F9, ModifierKeys.Alt, "Alt+F9");
    public static readonly AppShortcut SalesOrder = new(Key.F8, ModifierKeys.Control, "Ctrl+F8");
    public static readonly AppShortcut PurchaseOrder = new(Key.F9, ModifierKeys.Control, "Ctrl+F9");

    /// <summary>The full list of other voucher types.</summary>
    public static readonly AppShortcut OtherVouchers = new(Key.F10, ModifierKeys.None, "F10");

    /// <summary>
    /// Every gesture above. Used by the duplicate guard and by the shortcut-map test, so a key that
    /// gets a second meaning fails the build rather than surprising an operator.
    /// </summary>
    public static IReadOnlyList<AppShortcut> All { get; } =
    [
        GoTo, SwitchTo, Back, Help, ChangeDate, ChangePeriod, Company, CompanyMenu, Features, Configure,
        Accept, Cancel, Delete, RemoveLine, Exit, CreateOnTheFly, Find, Print, Export,
        DrillDown, ToggleGroup, Previous, Next,
        Contra, Payment, Receipt, Journal, Sales, Purchase,
        DebitNote, CreditNote, StockJournal, DeliveryNote, ReceiptNote,
        SalesOrder, PurchaseOrder, OtherVouchers,
    ];
}
