using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VinayakaAgencies.App.Presentation.Controls;

/// <summary>
/// A Tally-style lookup field: type to filter, a list under the caret, Enter to take the highlighted
/// row, <c>Alt+C</c> to create the master you find is missing (ADR-0007).
/// </summary>
/// <remarks>
/// <para>
/// This replaces the <c>ComboBox</c> that voucher and document screens used for ledgers, items,
/// customers and godowns. A <c>ComboBox</c> is a mouse control wearing a keyboard costume: it matches
/// on first letter only, so an operator reaching "Vinayaka Motors" types V and gets whichever V came
/// first, then reaches for the mouse. With five thousand items — which the Phase 2 checkpoint
/// requires the item master to hold — that is not a workflow. This filters on every keystroke,
/// anywhere in the name.
/// </para>
/// <para>
/// <b>Alt+C is the other half.</b> An operator entering an invoice discovers a missing ledger at the
/// moment they need it, not before. Sending them to the Ledgers screen means abandoning a half-typed
/// voucher, so in practice they type something wrong instead and the books carry it. Creating from
/// the field is what stops that.
/// </para>
/// <para>
/// <b>Key routing.</b> The shell window claims Escape, Enter and the rest on the way down the tree,
/// which is the right default — a DataGrid would otherwise swallow them. But a field with an open
/// drop-down must win: Escape there means "close this list", not "leave the screen". Rather than
/// scatter exceptions through the shell, the focused lookup is published as <see cref="Focused"/>
/// and the shell offers it every keystroke first, through <see cref="TryHandleKey"/>. One registry,
/// one call site, and the shell keeps its single router.
/// </para>
/// </remarks>
public sealed class LookupBox : Control
{
    private const string TextBoxPart = "PART_TextBox";
    private const string ListPart = "PART_List";

    /// <summary>How far Page Up / Page Down jump through the list.</summary>
    private const int PageSize = 10;

    private TextBox? _textBox;
    private ListBox? _list;

    /// <summary>Guards the two-way pull between typed text and the selected item.</summary>
    private bool _isSyncing;

    /// <summary>Caches the reflected display property; lookups re-read it on every keystroke.</summary>
    private Type? _displayCacheType;
    private string _displayCachePath = string.Empty;
    private PropertyInfo? _displayCacheProperty;

    static LookupBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(LookupBox),
            new FrameworkPropertyMetadata(typeof(LookupBox)));
    }

    /// <summary>
    /// The lookup that currently holds keyboard focus, or null.
    /// </summary>
    /// <remarks>
    /// Static because the shell's router needs to ask "is a lookup live?" without walking the visual
    /// tree on every keystroke, and because exactly one control can hold keyboard focus at a time —
    /// so a single slot cannot get out of step with reality the way a collection could.
    /// </remarks>
    public static LookupBox? Focused { get; private set; }

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(LookupBox),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(object),
            typeof(LookupBox),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedItemChanged));

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(
            nameof(DisplayMemberPath),
            typeof(string),
            typeof(LookupBox),
            new PropertyMetadata(string.Empty, OnDisplayMemberPathChanged));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(LookupBox),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextChanged));

    public static readonly DependencyProperty WatermarkProperty =
        DependencyProperty.Register(
            nameof(Watermark),
            typeof(string),
            typeof(LookupBox),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CreateCommandProperty =
        DependencyProperty.Register(
            nameof(CreateCommand),
            typeof(ICommand),
            typeof(LookupBox),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CreateLabelProperty =
        DependencyProperty.Register(
            nameof(CreateLabel),
            typeof(string),
            typeof(LookupBox),
            new PropertyMetadata("Create"));

    private static readonly DependencyPropertyKey FilteredItemsPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(FilteredItems),
            typeof(IReadOnlyList<object>),
            typeof(LookupBox),
            new PropertyMetadata(Array.Empty<object>()));

    public static readonly DependencyProperty FilteredItemsProperty = FilteredItemsPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey HasNoMatchesPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HasNoMatches),
            typeof(bool),
            typeof(LookupBox),
            new PropertyMetadata(false));

    public static readonly DependencyProperty HasNoMatchesProperty = HasNoMatchesPropertyKey.DependencyProperty;

    public static readonly DependencyProperty HighlightedItemProperty =
        DependencyProperty.Register(
            nameof(HighlightedItem),
            typeof(object),
            typeof(LookupBox),
            new PropertyMetadata(null, OnHighlightedItemChanged));

    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(
            nameof(IsDropDownOpen),
            typeof(bool),
            typeof(LookupBox),
            new PropertyMetadata(false));

    /// <summary>Everything the field can offer.</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>The committed selection.</summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>The property of an item that is shown and matched on.</summary>
    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    /// <summary>What has been typed so far.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Placeholder shown while the field is empty.</summary>
    public string Watermark
    {
        get => (string)GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    /// <summary>Invoked by Alt+C, with the typed text as its parameter.</summary>
    public ICommand? CreateCommand
    {
        get => (ICommand?)GetValue(CreateCommandProperty);
        set => SetValue(CreateCommandProperty, value);
    }

    /// <summary>What Alt+C is called on the drop-down's footer, e.g. "Create ledger".</summary>
    public string CreateLabel
    {
        get => (string)GetValue(CreateLabelProperty);
        set => SetValue(CreateLabelProperty, value);
    }

    /// <summary>The candidates for what has been typed, best match first.</summary>
    public IReadOnlyList<object> FilteredItems => (IReadOnlyList<object>)GetValue(FilteredItemsProperty);

    /// <summary>True when text has been typed and nothing matches it.</summary>
    public bool HasNoMatches => (bool)GetValue(HasNoMatchesProperty);

    /// <summary>The row under the highlight, which Enter takes.</summary>
    public object? HighlightedItem
    {
        get => GetValue(HighlightedItemProperty);
        set => SetValue(HighlightedItemProperty, value);
    }

    /// <summary>True while the list is showing.</summary>
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_textBox is not null)
        {
            _textBox.GotKeyboardFocus -= OnTextBoxGotKeyboardFocus;
            _textBox.LostKeyboardFocus -= OnTextBoxLostKeyboardFocus;
        }

        if (_list is not null)
        {
            _list.PreviewMouseLeftButtonUp -= OnListClicked;
        }

        _textBox = GetTemplateChild(TextBoxPart) as TextBox;
        _list = GetTemplateChild(ListPart) as ListBox;

        if (_textBox is not null)
        {
            _textBox.GotKeyboardFocus += OnTextBoxGotKeyboardFocus;
            _textBox.LostKeyboardFocus += OnTextBoxLostKeyboardFocus;
        }

        if (_list is not null)
        {
            _list.PreviewMouseLeftButtonUp += OnListClicked;
        }

        Refilter();
    }

    /// <summary>
    /// Offers this lookup a keystroke before the shell acts on it. Returns true when the lookup used
    /// it, in which case the shell must not.
    /// </summary>
    /// <remarks>
    /// Every branch returns false when the key would do nothing here, so that a gesture the field has
    /// no use for — Escape with the list already shut, Alt+C where nothing can be created — still
    /// reaches the shell and behaves as it does everywhere else. A field that swallowed keys silently
    /// would make Escape stop working at random points in a voucher.
    /// </remarks>
    /// <param name="key">The key pressed.</param>
    /// <param name="modifiers">Modifiers held.</param>
    /// <returns>True when handled here.</returns>
    public bool TryHandleKey(Key key, ModifierKeys modifiers)
    {
        if (IsCreateOnTheFly(key, modifiers))
        {
            return TryCreate();
        }

        if (modifiers is not ModifierKeys.None)
        {
            return false;
        }

        switch (key)
        {
            case Key.Down:
                OpenDropDown();
                MoveHighlight(1);
                return true;

            case Key.Up when IsDropDownOpen:
                MoveHighlight(-1);
                return true;

            case Key.PageDown when IsDropDownOpen:
                MoveHighlight(PageSize);
                return true;

            case Key.PageUp when IsDropDownOpen:
                MoveHighlight(-PageSize);
                return true;

            case Key.Enter:
                CommitAndAdvance();
                return true;

            case Key.Escape when IsDropDownOpen:
                CloseDropDown();
                SyncTextToSelection();
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// True for the Alt+C gesture. Written out rather than referencing
    /// <c>AppShortcuts.CreateOnTheFly</c> so that <c>Presentation.Controls</c> does not depend on
    /// <c>Presentation.Keyboard</c> — the control is a widget, and the key map is application policy.
    /// A test pins the two together so they cannot drift.
    /// </summary>
    private static bool IsCreateOnTheFly(Key key, ModifierKeys modifiers) =>
        key is Key.C && modifiers is ModifierKeys.Alt;

    /// <summary>Runs the create command with whatever has been typed, so the new master is pre-named.</summary>
    private bool TryCreate()
    {
        var command = CreateCommand;

        if (command is null || !command.CanExecute(Text))
        {
            return false;
        }

        CloseDropDown();
        command.Execute(Text);

        return true;
    }

    /// <summary>
    /// Takes the highlighted row — or, if the list is shut, whatever the typed text unambiguously
    /// names — and moves on to the next field, which is what makes a whole line typeable in one pass.
    /// </summary>
    private void CommitAndAdvance()
    {
        var chosen = IsDropDownOpen && HighlightedItem is not null
            ? HighlightedItem
            : Resolve();

        if (chosen is not null)
        {
            Select(chosen);
        }

        CloseDropDown();

        _textBox?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    /// <summary>The single item the typed text names, or null when it names none or several.</summary>
    private object? Resolve() =>
        LookupFilter.Resolve(ItemsSource?.Cast<object>(), Text, DisplayOf);

    private void Select(object item)
    {
        _isSyncing = true;

        try
        {
            SelectedItem = item;
            Text = DisplayOf(item) ?? string.Empty;
            HighlightedItem = item;
        }
        finally
        {
            _isSyncing = false;
        }

        Refilter();
    }

    private void MoveHighlight(int delta)
    {
        var items = FilteredItems;

        if (items.Count == 0)
        {
            HighlightedItem = null;
            return;
        }

        var current = HighlightedItem is null ? -1 : IndexOf(items, HighlightedItem);

        // From nowhere, a downward move lands on the first row rather than the second.
        var target = current < 0
            ? (delta > 0 ? 0 : items.Count - 1)
            : Math.Clamp(current + delta, 0, items.Count - 1);

        HighlightedItem = items[target];
    }

    private static int IndexOf(IReadOnlyList<object> items, object target)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], target) || Equals(items[i], target))
            {
                return i;
            }
        }

        return -1;
    }

    private void OpenDropDown()
    {
        if (!IsDropDownOpen)
        {
            Refilter();
            IsDropDownOpen = true;
        }
    }

    private void CloseDropDown() => IsDropDownOpen = false;

    private void OnTextBoxGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Focused = this;

        // The list shows for as long as the field is live, which is how Tally behaves and what makes
        // Enter unambiguous: while a lookup has focus, Enter always means "take this row".
        OpenDropDown();

        _textBox?.SelectAll();
    }

    private void OnTextBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ReferenceEquals(Focused, this))
        {
            Focused = null;
        }

        CloseDropDown();

        // Tabbing out of a field that was typed but never committed still selects, when the text
        // names exactly one master. Leaving it uncommitted would lose what the operator typed without
        // saying so.
        if (Resolve() is { } resolved)
        {
            Select(resolved);
        }
        else
        {
            SyncTextToSelection();
        }
    }

    /// <summary>Clicking a row is the same act as highlighting it and pressing Enter.</summary>
    private void OnListClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_list?.SelectedItem is { } clicked)
        {
            Select(clicked);
            CloseDropDown();
            _textBox?.Focus();
        }
    }

    /// <summary>Puts the field back to what is actually selected, discarding abandoned typing.</summary>
    private void SyncTextToSelection()
    {
        _isSyncing = true;

        try
        {
            Text = DisplayOf(SelectedItem) ?? string.Empty;
        }
        finally
        {
            _isSyncing = false;
        }

        Refilter();
    }

    private void Refilter()
    {
        var source = ItemsSource?.Cast<object>();
        var matches = LookupFilter.Rank(source, Text, DisplayOf);

        SetValue(FilteredItemsPropertyKey, matches);
        SetValue(HasNoMatchesPropertyKey, matches.Count == 0 && !string.IsNullOrWhiteSpace(Text));

        if (HighlightedItem is null || IndexOf(matches, HighlightedItem) < 0)
        {
            HighlightedItem = matches.Count > 0 ? matches[0] : null;
        }
    }

    /// <summary>
    /// The text an item is shown and matched by.
    /// </summary>
    /// <remarks>
    /// Reflection over a single property name, cached per item type, rather than a full WPF binding
    /// path. Every lookup in this application points at a flat DTO property ("Name", "Code"), and a
    /// binding evaluator would cost a <c>FrameworkElement</c> per row to read one string.
    /// </remarks>
    private string? DisplayOf(object? item)
    {
        if (item is null)
        {
            return null;
        }

        var path = DisplayMemberPath;

        if (string.IsNullOrEmpty(path))
        {
            return item.ToString();
        }

        var type = item.GetType();

        if (_displayCacheType != type || !string.Equals(_displayCachePath, path, StringComparison.Ordinal))
        {
            _displayCacheType = type;
            _displayCachePath = path;
            _displayCacheProperty = type.GetProperty(path);
        }

        return _displayCacheProperty?.GetValue(item)?.ToString();
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((LookupBox)d).Refilter();

    private static void OnDisplayMemberPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var lookup = (LookupBox)d;

        lookup._displayCacheType = null;
        lookup._displayCachePath = string.Empty;
        lookup._displayCacheProperty = null;

        lookup.SyncTextToSelection();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var lookup = (LookupBox)d;

        if (lookup._isSyncing)
        {
            return;
        }

        lookup.Refilter();

        if (lookup._textBox?.IsKeyboardFocusWithin == true)
        {
            lookup.OpenDropDown();
        }
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var lookup = (LookupBox)d;

        if (!lookup._isSyncing)
        {
            lookup.SyncTextToSelection();
        }
    }

    private static void OnHighlightedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var lookup = (LookupBox)d;

        if (e.NewValue is not null)
        {
            lookup._list?.ScrollIntoView(e.NewValue);
        }
    }
}
