using System.Windows;
using VinayakaAgencies.App.ViewModels;

namespace VinayakaAgencies.App.Views;

/// <summary>
/// Interaction logic for LoginWindow.
/// </summary>
public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LoginViewModel oldViewModel)
        {
            oldViewModel.CloseRequested -= OnCloseRequested;
        }

        if (e.NewValue is LoginViewModel newViewModel)
        {
            newViewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, System.EventArgs e) =>
        DialogResult = DataContext is LoginViewModel { IsSignedIn: true };
}
