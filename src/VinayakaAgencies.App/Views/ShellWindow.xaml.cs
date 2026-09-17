using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using VinayakaAgencies.App.Presentation.Keyboard;
using VinayakaAgencies.App.ViewModels;

namespace VinayakaAgencies.App.Views;

/// <summary>
/// Interaction logic for ShellWindow.xaml — the application's single keyboard router (ADR-0006).
/// </summary>
/// <remarks>
/// <para>
/// All global key handling happens here, in <see cref="OnPreviewKeyDown"/>, rather than in per-view
/// <c>InputBinding</c>s. Preview is the point: a <c>DataGrid</c> or a <c>TextBox</c> will happily
/// swallow Escape, Ctrl+A or the arrow keys before a bubbling handler ever sees them, so shortcuts
/// that must work everywhere have to be claimed on the way down.
/// </para>
/// <para>
/// This is code-behind, which the conventions otherwise forbid. It qualifies as interaction logic
/// rather than behaviour: every branch either forwards to a command on the view model or opens a
/// window. No decision about what a screen does is taken here.
/// </para>
/// </remarks>
public partial class ShellWindow : Window
{
    private GoToPaletteWindow? _palette;

    public ShellWindow(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;

        viewModel.GoToRequested += (_, _) => OpenPalette();
        viewModel.SwitchToRequested += (_, _) => OpenPalette();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e is null || DataContext is not ShellViewModel shell)
        {
            return;
        }

        var modifiers = e.KeyboardDevice.Modifiers;
        var key = e.Key is Key.System ? e.SystemKey : e.Key;

        // Navigation and chrome.
        if (AppShortcuts.GoTo.Matches(key, modifiers) || AppShortcuts.SwitchTo.Matches(key, modifiers))
        {
            OpenPalette();
            e.Handled = true;
            return;
        }

        if (AppShortcuts.Back.Matches(key, modifiers))
        {
            // Never handled at the Gateway, so Escape there falls through to the focused control
            // rather than being silently eaten.
            if (!shell.IsGatewayVisible)
            {
                shell.NavigateBackCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (AppShortcuts.Exit.Matches(key, modifiers))
        {
            shell.NavigateBackCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (AppShortcuts.Help.Matches(key, modifiers))
        {
            shell.ShowHelpCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // Screen actions.
        if (AppShortcuts.Accept.Matches(key, modifiers))
        {
            e.Handled = Invoke(shell.ActiveAcceptCommand);
            return;
        }

        if (AppShortcuts.Cancel.Matches(key, modifiers))
        {
            e.Handled = Invoke(shell.ActiveCancelCommand);
            return;
        }

        // Anything the active screen declared on either bar.
        if (TryInvokeBarShortcut(shell, key, modifiers))
        {
            e.Handled = true;
            return;
        }

        // Gateway accelerators: a bare letter opens the entry that claims it. Only when the Gateway is
        // showing and no text box has the caret, or typing into a filter would navigate instead.
        if (shell.IsGatewayVisible
            && modifiers == ModifierKeys.None
            && key is >= Key.A and <= Key.Z
            && Keyboard.FocusedElement is not TextBoxBase
            && shell.TryOpenByHotkey((char)('A' + (key - Key.A))))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Runs a gesture the active screen put on one of its bars. The bars are the single source of
    /// truth for what a screen's keys do, so there is nowhere else a screen shortcut can be declared.
    /// </summary>
    private static bool TryInvokeBarShortcut(ShellViewModel shell, Key key, ModifierKeys modifiers)
    {
        foreach (var button in shell.ButtonBar.Concat(shell.ActionBar))
        {
            if (button.Shortcut.Matches(key, modifiers))
            {
                return Invoke(button.Command);
            }
        }

        return false;
    }

    private static bool Invoke(ICommand? command)
    {
        if (command is null || !command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);

        return true;
    }

    private void OpenPalette()
    {
        if (_palette is not null)
        {
            _palette.Activate();
            return;
        }

        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        _palette = new GoToPaletteWindow(shell) { Owner = this };
        _palette.Closed += (_, _) => _palette = null;
        _palette.Show();
    }
}
