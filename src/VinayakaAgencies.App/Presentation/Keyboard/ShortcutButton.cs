using System.Windows.Input;

namespace VinayakaAgencies.App.Presentation.Keyboard;

/// <summary>
/// One entry on the right-hand button bar or the bottom action bar (ADR-0006).
/// </summary>
/// <remarks>
/// <para>
/// The bars are the discoverability half of a keyboard-first design. An operator who does not yet know
/// the keys reads them off the bar and clicks; one who does never looks. Both bars are therefore driven
/// from the same objects that carry the commands, so a button can never advertise a key that is not
/// wired, or a command the screen no longer has.
/// </para>
/// <para>
/// <see cref="Command"/> is nullable so a screen can show a key it does not currently offer — greyed
/// rather than missing. A button that vanishes makes the bar jump and leaves the operator wondering
/// where it went; a greyed one answers the question.
/// </para>
/// </remarks>
/// <param name="Shortcut">The gesture, which also supplies the label shown.</param>
/// <param name="Label">What the action is called, e.g. "Accept".</param>
/// <param name="Command">The command to run, or null when unavailable here.</param>
public sealed record ShortcutButton(AppShortcut Shortcut, string Label, ICommand? Command = null)
{
    /// <summary>The gesture as written on the bar.</summary>
    public string Gesture => Shortcut.Display;

    /// <summary>False when the screen is showing the key but cannot act on it right now.</summary>
    public bool IsAvailable => Command is not null;
}
