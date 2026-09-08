using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Data;
using VinayakaAgencies.App.ViewModels;

namespace VinayakaAgencies.App.Views;

/// <summary>
/// The main application window.
/// </summary>
/// <remarks>
/// The code-behind is input routing only (ADR-0005): Alt+G opens the Go To palette, Esc walks
/// back the shell's history stack, Ctrl+A/Ctrl+Q invoke the focused screen's
/// <see cref="IScreenShortcuts"/>, and digits 1..9 jump to the numbered module â€” the latter only
/// when a text control does not own keyboard focus. Everything else stays in the view model.
/// </remarks>
public partial class ShellWindow : Window
{
    private GoToPaletteWindow? _palette;

    public ShellWindow(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;

        // Group the home menu by module caption ("1 Masters", "2 Vouchers", â€¦) so the gateway
        // reads as Tally's does: numbered modules, screens beneath them.
        var menuView = (ListCollectionView)CollectionViewSource.GetDefaultView(viewModel.Screens);
        menuView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Navigation.NavigationItem.ModuleLabel)));
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        if (e.KeyboardDevice.Modifiers == ModifierKeys.Alt && e.Key == Key.G)
        {
            OpenPalette();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            shell.NavigateBackCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control)
        {
            var handled = e.Key switch
            {
                Key.A => InvokeScreenShortcut(shell, shortcuts => shortcuts.AcceptCommand),
                Key.Q => InvokeScreenShortcut(shell, shortcuts => shortcuts.CancelCommand),
                _ => false,
            };

            e.Handled = handled;
            return;
        }

        if (e.Key is Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.D5 or Key.D6 or Key.D7 or Key.D8 or Key.D9
            && e.KeyboardDevice.Modifiers == ModifierKeys.None
            && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase)
        {
            shell.SelectModuleByNumber(e.Key - Key.D1 + 1);
            e.Handled = true;
        }
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

    private static bool InvokeScreenShortcut(
        ShellViewModel shell,
        Func<IScreenShortcuts, ICommand?> selector)
    {
        if (shell.ActiveView is FrameworkElement { DataContext: IScreenShortcuts shortcuts }
            && selector(shortcuts) is { } command
            && command.CanExecute(null))
        {
            command.Execute(null);
            return true;
        }

        return false;
    }
}
