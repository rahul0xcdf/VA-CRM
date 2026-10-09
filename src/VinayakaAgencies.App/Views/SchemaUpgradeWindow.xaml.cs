using System.Windows;
using VinayakaAgencies.App.ViewModels;

namespace VinayakaAgencies.App.Views;

/// <summary>
/// Interaction logic for SchemaUpgradeWindow.
/// </summary>
public partial class SchemaUpgradeWindow : Window
{
    public SchemaUpgradeWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SchemaUpgradeViewModel oldViewModel)
        {
            oldViewModel.CloseRequested -= OnCloseRequested;
        }

        if (e.NewValue is SchemaUpgradeViewModel newViewModel)
        {
            newViewModel.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, System.EventArgs e) =>
        DialogResult = DataContext is SchemaUpgradeViewModel { IsUpToDate: true };
}
