using System.Windows;
using MotorPumpSuite.App.ViewModels;

namespace MotorPumpSuite.App.Views;

/// <summary>
/// The main application window.
/// </summary>
public partial class ShellWindow : Window
{
    public ShellWindow(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }
}
