namespace VinayakaAgencies.App.Presentation.Keyboard;

/// <summary>
/// A menu label split around its single-key accelerator, ready to render with that letter
/// highlighted (ADR-0006).
/// </summary>
/// <remarks>
/// Tally shows the accelerator inside the word — "C<b>h</b>art of Accounts" is reached with H, not C,
/// because C already belongs to "Create". Splitting the label here keeps that presentation detail out
/// of the view, which only has to draw three runs.
/// </remarks>
/// <param name="Text">The full label.</param>
/// <param name="Index">Index of the accelerator character, or -1 when the label has none left to give.</param>
public sealed record MenuHotkey(string Text, int Index)
{
    /// <summary>True when this label has an accelerator.</summary>
    public bool HasKey => Index >= 0;

    /// <summary>The accelerator character, uppercased. <c>'\0'</c> when there is none.</summary>
    public char Key => HasKey ? char.ToUpperInvariant(Text[Index]) : '\0';

    /// <summary>The run before the accelerator.</summary>
    public string Before => HasKey ? Text[..Index] : Text;

    /// <summary>The accelerator itself, as a string.</summary>
    public string Letter => HasKey ? Text.Substring(Index, 1) : string.Empty;

    /// <summary>The run after the accelerator.</summary>
    public string After => HasKey ? Text[(Index + 1)..] : string.Empty;
}

/// <summary>
/// Assigns unique single-key accelerators across a menu (ADR-0006).
/// </summary>
/// <remarks>
/// <para>
/// The rule: walk each label left to right and take the first letter not already spoken for. So a menu
/// of Create / Alter / Chart of Accounts yields C, A, and — because C is taken — the H of "Chart".
/// That is the behaviour Tally users expect, and it is why accelerators appear mid-word.
/// </para>
/// <para>
/// A screen may <em>claim</em> a letter explicitly, and claims are honoured before anything is computed.
/// This matters because computed letters are positional: inserting one screen into a module would
/// otherwise silently re-letter every screen below it, and an operator who has learned a key would
/// find it moved. Anything an operator is likely to memorise should claim its letter.
/// </para>
/// <para>
/// Uniqueness is per menu, not per application — two different menus may both use C, because only one
/// is ever on screen.
/// </para>
/// </remarks>
public static class MenuHotkeyAllocator
{
    /// <summary>
    /// Allocates accelerators for <paramref name="labels"/>, honouring <paramref name="claims"/>.
    /// </summary>
    /// <param name="labels">Menu labels, in display order.</param>
    /// <param name="claims">
    /// Optional per-label claimed letters, keyed by label. A claim that collides with an earlier claim
    /// loses and is re-computed, so a typo degrades rather than silently stealing another screen's key.
    /// </param>
    public static IReadOnlyList<MenuHotkey> Allocate(
        IEnumerable<string> labels,
        IReadOnlyDictionary<string, char>? claims = null)
    {
        ArgumentNullException.ThrowIfNull(labels);

        var ordered = labels.ToList();
        var taken = new HashSet<char>();
        var result = new MenuHotkey?[ordered.Count];

        // Claims first, so a computed letter can never take a key a screen asked for by name.
        if (claims is not null)
        {
            for (var i = 0; i < ordered.Count; i++)
            {
                if (!claims.TryGetValue(ordered[i], out var claimed))
                {
                    continue;
                }

                var upper = char.ToUpperInvariant(claimed);
                var index = ordered[i].IndexOf(upper, StringComparison.OrdinalIgnoreCase);

                if (index >= 0 && taken.Add(upper))
                {
                    result[i] = new MenuHotkey(ordered[i], index);
                }
            }
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            if (result[i] is not null)
            {
                continue;
            }

            result[i] = Compute(ordered[i], taken);
        }

        return result.Select(hotkey => hotkey!).ToList();
    }

    private static MenuHotkey Compute(string label, HashSet<char> taken)
    {
        for (var index = 0; index < label.Length; index++)
        {
            if (!char.IsLetterOrDigit(label[index]))
            {
                continue;
            }

            var candidate = char.ToUpperInvariant(label[index]);

            if (taken.Add(candidate))
            {
                return new MenuHotkey(label, index);
            }
        }

        // Every letter in this label is already spoken for. The item stays reachable by arrow keys and
        // by Go To; silently stealing a key from another item would be worse than having none.
        return new MenuHotkey(label, -1);
    }
}
