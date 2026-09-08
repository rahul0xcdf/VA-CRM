using System.Windows.Input;

namespace VinayakaAgencies.App.ViewModels;

/// <summary>
/// The keyboard-shortcut contract a screen view model opts into (ADR-0005): the shell routes
/// <c>Ctrl+A</c> and <c>Ctrl+Q</c> to these commands and shows the hints in its status bar.
/// </summary>
/// <remarks>
/// Screens that do not implement this interface simply keep the default shell hints
/// ("Alt+G Go To · Esc Back"); no registration is needed either way.
/// </remarks>
public interface IScreenShortcuts
{
    /// <summary>Invoked by Ctrl+A — accept/save whatever the screen is editing. May be null.</summary>
    ICommand? AcceptCommand => null;

    /// <summary>Invoked by Ctrl+Q — cancel/close the current operation. May be null.</summary>
    ICommand? CancelCommand => null;

    /// <summary>Shortcut hints for the shell status bar; empty/null falls back to the default.</summary>
    string? StatusBarHints => null;
}
