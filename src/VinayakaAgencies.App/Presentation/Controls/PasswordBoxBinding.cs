using System.Windows;
using System.Windows.Controls;

namespace VinayakaAgencies.App.Presentation.Controls;

/// <summary>
/// Lets a <see cref="PasswordBox"/> bind its value, which it cannot do on its own.
/// </summary>
/// <remarks>
/// <see cref="PasswordBox.Password"/> is deliberately not a dependency property — WPF's authors did
/// not want a password sitting in the binding engine's caches. That leaves three options for a
/// view model that needs one: a plain <c>TextBox</c>, code-behind, or this.
/// <para>
/// A <c>TextBox</c> is what the Phase 6 e-invoice settings screen uses for the IRP secret, and it
/// is defensible there — that field is typed once at installation by the person who owns the
/// credentials. It is not defensible on a login prompt at a shop counter, where the whole point is
/// that the person at the next till cannot read it off the screen.
/// </para>
/// <para>
/// Code-behind is ruled out by the convention that a view holds nothing beyond
/// <c>InitializeComponent()</c>, and for a better reason than tidiness: a password handler in a
/// <c>.xaml.cs</c> is a password handler no unit test can reach.
/// </para>
/// <para>
/// So: an attached property, write-through only. It pushes into the view model on each keystroke
/// and deliberately does <b>not</b> read back — a one-way-out channel cannot be used to populate a
/// password box from a binding, which is exactly the operation nothing should ever want.
/// </para>
/// </remarks>
public static class PasswordBoxBinding
{
    /// <summary>The bound value. Set it on a <see cref="PasswordBox"/> with a two-way binding.</summary>
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxBinding),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnBoundPasswordChanged));

    public static string GetBoundPassword(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (string)element.GetValue(BoundPasswordProperty);
    }

    public static void SetBoundPassword(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(BoundPasswordProperty, value);
    }

    private static void OnBoundPasswordChanged(
        DependencyObject element,
        DependencyPropertyChangedEventArgs e)
    {
        if (element is not PasswordBox box)
        {
            return;
        }

        box.PasswordChanged -= OnPasswordChanged;

        // Only honoured when it is a clear: the view model resetting the field after a failed
        // attempt has to work, while anything trying to push a *value* in is ignored. Attaching
        // without this check would also re-enter on every keystroke, since the handler below sets
        // the same property it is reacting to.
        if (e.NewValue is null or "" && box.Password.Length > 0)
        {
            box.Password = string.Empty;
        }

        box.PasswordChanged += OnPasswordChanged;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
        {
            SetBoundPassword(box, box.Password);
        }
    }
}
