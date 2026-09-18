namespace VinayakaAgencies.App.Presentation.Controls;

/// <summary>
/// Chooses and orders the candidates a lookup field offers for what has been typed so far
/// (ADR-0007).
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately a plain static class rather than logic inside <see cref="LookupBox"/>. The
/// control needs a WPF dispatcher and an STA thread to exist at all, so anything living inside it is
/// effectively untestable; the rule an operator actually feels — which ledger is highlighted after
/// typing three letters — is arithmetic over strings and belongs somewhere a unit test can reach it.
/// </para>
/// <para>
/// The ordering rule is prefix matches first, then interior matches, each keeping the order the
/// caller supplied. Prefix-first is what makes blind typing work: an operator who types "ca" for
/// "Cash" must not have "Bank Charges" land under the highlight because it happens to contain the
/// letters. Source order is preserved within each band because the caller has already sorted the
/// list — re-sorting here would silently override a deliberate ordering, such as the most-used
/// ledgers first.
/// </para>
/// </remarks>
public static class LookupFilter
{
    /// <summary>
    /// The candidates for <paramref name="text"/>, best match first.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">Everything the field could offer, in the caller's order.</param>
    /// <param name="text">What has been typed. Null, empty or blank offers the whole list.</param>
    /// <param name="display">Reads the text an item is matched on.</param>
    /// <returns>The matching items, prefix matches before interior matches.</returns>
    public static IReadOnlyList<T> Rank<T>(
        IEnumerable<T>? items,
        string? text,
        Func<T, string?> display)
    {
        ArgumentNullException.ThrowIfNull(display);

        if (items is null)
        {
            return [];
        }

        var needle = text?.Trim();

        // An empty field shows everything, exactly as pressing the key in Tally does. Offering
        // nothing until a letter is typed would hide the list from an operator who does not yet know
        // what is in it, which is the one case the pop-up exists for.
        if (string.IsNullOrEmpty(needle))
        {
            return [.. items];
        }

        var prefix = new List<T>();
        var interior = new List<T>();

        foreach (var item in items)
        {
            var candidate = display(item);

            if (string.IsNullOrEmpty(candidate))
            {
                continue;
            }

            if (candidate.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
            {
                prefix.Add(item);
            }
            else if (candidate.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                interior.Add(item);
            }
        }

        prefix.AddRange(interior);

        return prefix;
    }

    /// <summary>
    /// The one item <paramref name="text"/> unambiguously names, or null.
    /// </summary>
    /// <remarks>
    /// Used when the operator tabs out of a lookup without ever opening the list. An exact
    /// case-insensitive hit always wins, even when other items merely start with the same letters:
    /// typing "Cash" must select "Cash" and not leave the field ambiguous because "Cash in Hand"
    /// exists. Failing that, a single remaining candidate is taken; two or more is not a decision
    /// this method may make on the operator's behalf.
    /// </remarks>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">Everything the field could offer.</param>
    /// <param name="text">What was typed.</param>
    /// <param name="display">Reads the text an item is matched on.</param>
    /// <returns>The resolved item, or null when the text names none or more than one.</returns>
    public static T? Resolve<T>(IEnumerable<T>? items, string? text, Func<T, string?> display)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(display);

        var needle = text?.Trim();

        if (items is null || string.IsNullOrEmpty(needle))
        {
            return null;
        }

        var candidates = Rank(items, needle, display);

        var exact = candidates.FirstOrDefault(item =>
            string.Equals(display(item), needle, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            return exact;
        }

        return candidates.Count == 1 ? candidates[0] : null;
    }
}
