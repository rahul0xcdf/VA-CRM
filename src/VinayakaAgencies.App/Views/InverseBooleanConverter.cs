using System.Globalization;
using System.Windows.Data;

namespace VinayakaAgencies.App.Views;

/// <summary>
/// Enables a cell editor while its gate flag is false — the price cell stays editable only
/// while overrides are allowed (phase-4-customer-pricing.md §2).
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public static readonly InverseBooleanConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool flag ? !flag : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool flag ? !flag : value;
}
