using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace VinayakaAgencies.App.Presentation.Controls;

/// <summary>
/// Makes an <see cref="ItemsControl"/> of voucher lines behave like Tally's entry grid: Enter walks
/// across the fields of a line and wraps onto the next, adding a line when it runs out (ADR-0007).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not a DataGrid.</b> The screens this replaces used a <c>DataGrid</c> whose cells enter edit
/// mode on click. That costs the operator a mouse gesture per cell, and it makes Enter ambiguous —
/// WPF's <c>DataGrid</c> commits the cell and moves <em>down</em>, which is right for a spreadsheet
/// and wrong for a voucher, where the fields of one line are filled left to right. The lines are now
/// an <c>ItemsControl</c> of permanently live fields, which is both what Tally shows and what makes
/// focus traversal ordinary WPF instead of a fight with edit-mode state.
/// </para>
/// <para>
/// <b>Why a behaviour and not code-behind.</b> Six screens need this. Put in code-behind it would be
/// six copies drifting apart; here it is one, and the conventions' ban on code-behind logic stands.
/// </para>
/// <para>
/// The arithmetic — which field is next, and when a line must be added — lives in
/// <see cref="EntryGridNavigator"/> so it can be tested without a window.
/// </para>
/// </remarks>
public static class EntryGrid
{
    /// <summary>Turns the behaviour on for an items control of entry lines.</summary>
    public static readonly DependencyProperty EnableProperty =
        DependencyProperty.RegisterAttached(
            "Enable",
            typeof(bool),
            typeof(EntryGrid),
            new PropertyMetadata(false, OnEnableChanged));

    /// <summary>Invoked when Enter runs off the end of the last line; must append one line.</summary>
    public static readonly DependencyProperty AppendRowCommandProperty =
        DependencyProperty.RegisterAttached(
            "AppendRowCommand",
            typeof(ICommand),
            typeof(EntryGrid),
            new PropertyMetadata(null));

    /// <summary>
    /// The item of the line that holds focus, pushed back to the view model.
    /// </summary>
    /// <remarks>
    /// This is how "remove the line I am on" (Ctrl+D) works without the behaviour knowing anything
    /// about removal. The shell routes Ctrl+D to the screen's own command, exactly as it routes every
    /// other advertised key; the screen only needs to know which line the caret is in, and that is
    /// this.
    /// </remarks>
    public static readonly DependencyProperty CurrentItemProperty =
        DependencyProperty.RegisterAttached(
            "CurrentItem",
            typeof(object),
            typeof(EntryGrid),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static void SetEnable(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(EnableProperty, value);
    }

    public static bool GetEnable(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(EnableProperty);
    }

    public static void SetAppendRowCommand(DependencyObject element, ICommand? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AppendRowCommandProperty, value);
    }

    public static ICommand? GetAppendRowCommand(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (ICommand?)element.GetValue(AppendRowCommandProperty);
    }

    public static void SetCurrentItem(DependencyObject element, object? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CurrentItemProperty, value);
    }

    public static object? GetCurrentItem(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(CurrentItemProperty);
    }

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl items)
        {
            return;
        }

        items.PreviewKeyDown -= OnPreviewKeyDown;
        items.GotKeyboardFocus -= OnGotKeyboardFocus;

        if (e.NewValue is true)
        {
            items.PreviewKeyDown += OnPreviewKeyDown;
            items.GotKeyboardFocus += OnGotKeyboardFocus;
        }
    }

    /// <summary>Records which line the caret is in, so the screen's line commands have a target.</summary>
    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not ItemsControl items || e.NewFocus is not DependencyObject focused)
        {
            return;
        }

        var container = ContainerOf(items, focused);

        if (container is not null)
        {
            SetCurrentItem(items, items.ItemContainerGenerator.ItemFromContainer(container));
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ItemsControl items || e.Key is not Key.Enter
            || e.KeyboardDevice.Modifiers is not ModifierKeys.None)
        {
            return;
        }

        // A lookup with its list open owns Enter: it means "take this row", and the field itself
        // moves the caret on afterwards. Stepping in here would commit nothing and skip a field.
        if (LookupBox.Focused is { IsDropDownOpen: true })
        {
            return;
        }

        // Fully qualified: this assembly has a Presentation.Keyboard namespace, which otherwise wins
        // the name and resolves to a namespace rather than WPF's Keyboard class.
        if (System.Windows.Input.Keyboard.FocusedElement is not DependencyObject focused)
        {
            return;
        }

        var container = ContainerOf(items, focused);

        if (container is null)
        {
            return;
        }

        var rowIndex = IndexOfContainer(items, container);
        var fields = FieldsIn(container);
        var columnIndex = fields.FindIndex(field => ReferenceEquals(field, focused));

        if (rowIndex < 0 || columnIndex < 0)
        {
            return;
        }

        var move = EntryGridNavigator.Advance(rowIndex, columnIndex, items.Items.Count, fields.Count);

        e.Handled = true;

        if (move.NeedsNewRow)
        {
            var append = GetAppendRowCommand(items);

            if (append is null || !append.CanExecute(null))
            {
                return;
            }

            append.Execute(null);
        }

        // The appended line has no container until layout runs, so the focus move waits for it. At
        // Input priority this is the same frame as far as the operator is concerned.
        items.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => FocusField(items, move.Row, move.Column)));
    }

    private static void FocusField(ItemsControl items, int row, int column)
    {
        if (row < 0 || row >= items.Items.Count)
        {
            return;
        }

        if (items.ItemContainerGenerator.ContainerFromIndex(row) is not DependencyObject container)
        {
            return;
        }

        var fields = FieldsIn(container);

        if (column >= 0 && column < fields.Count)
        {
            fields[column].Focus();
        }
    }

    /// <summary>The line container that holds <paramref name="element"/>, or null.</summary>
    private static DependencyObject? ContainerOf(ItemsControl items, DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualParentOf(current))
        {
            if (items.ItemContainerGenerator.ItemFromContainer(current) != DependencyProperty.UnsetValue
                && !ReferenceEquals(current, items))
            {
                return current;
            }
        }

        return null;
    }

    private static int IndexOfContainer(ItemsControl items, DependencyObject container)
    {
        var item = items.ItemContainerGenerator.ItemFromContainer(container);

        return item == DependencyProperty.UnsetValue ? -1 : items.Items.IndexOf(item);
    }

    private static DependencyObject? VisualParentOf(DependencyObject element) =>
        element is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);

    /// <summary>
    /// The fields of one line, left to right.
    /// </summary>
    /// <remarks>
    /// Visual order is taken as field order, which holds because a line is laid out left to right and
    /// is what an operator reads. Buttons are excluded deliberately: the remove-line button at the end
    /// of a row is chrome, and stopping the caret on it would put a destructive action one stray Enter
    /// away in the middle of typing a voucher.
    /// </remarks>
    private static List<UIElement> FieldsIn(DependencyObject container)
    {
        var fields = new List<UIElement>();

        Collect(container, fields);

        return fields;

        static void Collect(DependencyObject node, List<UIElement> into)
        {
            var count = VisualTreeHelper.GetChildrenCount(node);

            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);

                if (child is LookupBox lookup)
                {
                    // The lookup's own text box is the focus target; its template is not walked, so
                    // the list inside the drop-down can never become an entry stop.
                    var box = FindTextBox(lookup);

                    if (box is not null)
                    {
                        into.Add(box);
                    }

                    continue;
                }

                if (IsField(child))
                {
                    into.Add((UIElement)child);
                    continue;
                }

                Collect(child, into);
            }
        }
    }

    private static bool IsField(DependencyObject node) => node switch
    {
        TextBox { IsReadOnly: false, IsEnabled: true, Visibility: Visibility.Visible } => true,
        DatePicker { IsEnabled: true, Visibility: Visibility.Visible } => true,
        ComboBox { IsEnabled: true, Visibility: Visibility.Visible } => true,
        CheckBox { IsEnabled: true, Visibility: Visibility.Visible } => true,
        _ => false,
    };

    private static TextBox? FindTextBox(DependencyObject node)
    {
        if (node is TextBox box)
        {
            return box;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);

        for (var i = 0; i < count; i++)
        {
            if (FindTextBox(VisualTreeHelper.GetChild(node, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
