using System.Globalization;
using System.Windows.Data;
using VinayakaAgencies.App.ViewModels;
using VinayakaAgencies.Application.Accounting;

namespace VinayakaAgencies.App.Navigation;

/// <summary>
/// Renders a flattened tree row: two spaces of indentation per level below the root.
/// </summary>
public sealed class GroupIndentConverter : IValueConverter
{
    public static readonly GroupIndentConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is AccountGroupSummary group ? AccountGroupsViewModel.Indent(group) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("The indentation is presentation only.");
}
