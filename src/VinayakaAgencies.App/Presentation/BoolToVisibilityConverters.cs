using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VinayakaAgencies.App.Presentation;

/// <summary>
/// True to <see cref="Visibility.Visible"/>, false to <see cref="Visibility.Collapsed"/> (ADR-0006).
/// </summary>
/// <remarks>
/// <see cref="Visibility.Collapsed"/> rather than <see cref="Visibility.Hidden"/>: the Gateway and the
/// active screen occupy the same grid cell, and a hidden element still reserves its space, which would
/// leave the screen laid out around a Gateway nobody can see.
/// </remarks>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>
/// The inverse: true collapses, false shows (ADR-0006).
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}

/// <summary>
/// Shows an element only when a count is zero (ADR-0006).
/// </summary>
/// <remarks>
/// Used for the empty-state message that sits behind a grid. A page whose query returned nothing
/// should say so; a blank rectangle leaves the operator wondering whether it is loading, broken, or
/// genuinely empty.
/// </remarks>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int and 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Empty-state visibility is one-way.");
}
