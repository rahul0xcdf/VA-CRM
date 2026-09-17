using System.Windows.Input;
using VinayakaAgencies.App.Presentation.Keyboard;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// What a screen contributes to the shell's chrome: its accept/cancel commands, the contextual keys
/// on the right button bar, and the actions on the bottom bar (ADR-0006).
/// </summary>
/// <remarks>
/// <para>
/// Every member has a default, so a screen opts into exactly as much as it needs and existing screens
/// keep working untouched. A screen that implements nothing still gets the shell's global keys.
/// </para>
/// <para>
/// The shell reads these once per navigation rather than binding to them continuously. Screens whose
/// bars change with state raise <c>PropertyChanged</c> for the bar property and the shell re-reads —
/// see <see cref="ShellViewModel"/>.
/// </para>
/// </remarks>
public interface IScreenShortcuts
{
    /// <summary>Invoked by Ctrl+A — accept and save whatever the screen is editing.</summary>
    ICommand? AcceptCommand => null;

    /// <summary>Invoked by Alt+X — cancel the current operation.</summary>
    ICommand? CancelCommand => null;

    /// <summary>
    /// Contextual keys for the right-hand bar, in display order. Global entries (Help, Date, Company,
    /// Features, Configure) are added by the shell and must not be repeated here.
    /// </summary>
    IReadOnlyList<ShortcutButton> ButtonBar => [];

    /// <summary>
    /// Actions for the bottom bar. The shell supplies Accept, Cancel and Exit from the commands above;
    /// anything further — Delete, Remove line, Print — belongs here.
    /// </summary>
    IReadOnlyList<ShortcutButton> ActionBar => [];

    /// <summary>
    /// Free-text hint shown at the right of the status line. Most screens leave this null and let the
    /// bars speak for themselves.
    /// </summary>
    string? StatusBarHints => null;
}
