using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VinayakaAgencies.App.Navigation;
using VinayakaAgencies.App.ViewModels;

namespace VinayakaAgencies.App.Views;

/// <summary>
/// The Alt+G command palette: every registered screen, filtered by typed text.
/// </summary>
/// <remarks>
/// The code-behind only routes input and filters the list; navigation itself is a plain
/// assignment of <see cref="ShellViewModel.SelectedScreen"/>, so the shell's history stack and
/// status bar behave exactly as for a mouse click on the home menu.
/// </remarks>
public partial class GoToPaletteWindow : Window
{
    private readonly ShellViewModel _shell;

    public GoToPaletteWindow(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        InitializeComponent();
        _shell = shell;
        Results.ItemsSource = shell.Screens;

        Loaded += (_, _) => FilterBox.Focus();
        FilterBox.TextChanged += (_, _) => ApplyFilter(FilterBox.Text);
    }

    private void ApplyFilter(string text)
    {
        var query = text.Trim();

        Results.ItemsSource = string.IsNullOrEmpty(query)
            ? _shell.Screens
            : _shell.Screens
                .Where(screen => screen.Screen.Header.Contains(query, StringComparison.OrdinalIgnoreCase)
                                 || screen.Group.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (Results.Items.Count > 0)
        {
            Results.SelectedIndex = 0;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        switch (e.Key)
        {
            case Key.Down when Results.Items.Count > 0:
                MoveSelection(+1);
                e.Handled = true;
                break;

            case Key.Up when Results.Items.Count > 0:
                MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.Enter when Results.SelectedItem is NavigationItem item:
                NavigateTo(item);
                e.Handled = true;
                break;

            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void OnResultsMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Results.SelectedItem is NavigationItem item)
        {
            NavigateTo(item);
        }
    }

    private void MoveSelection(int delta) =>
        Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + delta, 0, Results.Items.Count - 1);

    private void NavigateTo(NavigationItem item)
    {
        _shell.SelectedScreen = item;
        Close();
    }
}
