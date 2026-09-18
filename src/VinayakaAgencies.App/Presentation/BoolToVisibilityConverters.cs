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

/// <summary>
/// Shows an element only while a string is empty (ADR-0007).
/// </summary>
/// <remarks>
/// This is the watermark rule for <see cref="Controls.LookupBox"/>. The placeholder sits in the same
/// cell as the text, so it must disappear on the first keystroke — a hint left visible underneath a
/// typed value reads as part of the value.
/// </remarks>
public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Watermark visibility is one-way.");
}

/// <summary>
/// Shows an element only when a value is present (ADR-0007).
/// </summary>
/// <remarks>
/// Used for the "Alt+C creates it" footer on a lookup's drop-down, which must not appear on a field
/// that has no create command behind it. ADR-0006's rule that a bar never advertises a key it cannot
/// run applies just as much inside a pop-up.
/// </remarks>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Presence visibility is one-way.");
}

/// <summary>
/// A money or quantity field that shows nothing rather than a zero (ADR-0007).
/// </summary>
/// <remarks>
/// <para>
/// Tally leaves an unfilled amount blank, and a voucher grid is much easier to read for it: the eye
/// finds the three lines that carry figures instead of sorting real zeroes from placeholder ones. A
/// column of "0.00" in every empty cell is noise that hides the data.
/// </para>
/// <para>
/// Unparseable text returns <see cref="System.Windows.Data.Binding.DoNothing"/> rather than zero. A
/// mistyped amount must not silently become nothing — that is the one failure mode in an accounting
/// entry grid that the operator would never notice, because the cell would look exactly like a field
/// they had not reached yet.
/// </para>
/// </remarks>
public sealed class BlankZeroDecimalConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal amount && amount != 0m
            ? amount.ToString("0.##", culture)
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = (value as string)?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            return 0m;
        }

        return decimal.TryParse(text, NumberStyles.Number, culture, out var parsed)
            ? parsed
            : Binding.DoNothing;
    }
}

/// <summary>
/// Shows an element only when a count is non-zero (ADR-0007).
/// </summary>
/// <remarks>
/// The drill-down pane on a report. It stays out of the way until Ctrl+Enter fills it, so the report
/// itself gets the whole page until the operator asks a question of it — the opposite of a permanent
/// empty panel taking a third of the height on every screen that might one day have something in it.
/// </remarks>
public sealed class NonZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int and not 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Drill-down visibility is one-way.");
}
