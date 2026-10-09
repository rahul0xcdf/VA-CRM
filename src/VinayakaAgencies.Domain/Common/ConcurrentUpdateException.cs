namespace VinayakaAgencies.Domain.Common;

/// <summary>
/// Thrown when a save would have overwritten somebody else's change.
/// </summary>
/// <remarks>
/// `phase-9-multiuser.md` §2: "a lost-update must be detected and surfaced, never silently
/// overwritten." This is the surfacing. It is a domain type rather than EF Core's
/// <c>DbUpdateConcurrencyException</c> because the application and UI layers must be able to catch
/// it, and neither may reference EF Core (ADR-0001) — without it, the only way to handle a lost
/// update in a view model would be to break the dependency rule.
/// <para>
/// The failure this guards against is quiet by nature: two people open the same customer, both
/// save, and the second write erases the first with no error anywhere. Nobody finds out until the
/// credit limit is wrong.
/// </para>
/// <para>
/// Use <see cref="For"/> to name the contested entity. The three constructors below exist to keep
/// the standard exception shape and carry no entity name.
/// </para>
/// </remarks>
public sealed class ConcurrentUpdateException : Exception
{
    public ConcurrentUpdateException()
        : base("Somebody else changed this record while you had it open, so your save was not applied.")
    {
    }

    public ConcurrentUpdateException(string message)
        : base(message)
    {
    }

    public ConcurrentUpdateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    private ConcurrentUpdateException(string message, Exception innerException, string entityName)
        : base(message, innerException)
    {
        EntityName = entityName;
    }

    /// <summary>The entity that lost the race, for a message naming what to reload.</summary>
    public string? EntityName { get; }

    /// <summary>
    /// Builds the operator-facing message for a contested <paramref name="entityName"/>.
    /// </summary>
    /// <remarks>
    /// A factory rather than a constructor because <c>(string, Exception)</c> is already the
    /// standard message-and-inner overload, and reusing that signature to mean "entity name and
    /// inner" would make the two indistinguishable at the call site.
    /// </remarks>
    public static ConcurrentUpdateException For(string entityName, Exception innerException) =>
        new(
            $"Somebody else changed this {entityName} while you had it open, so your save was not "
            + "applied. Reload it and make your change again.",
            innerException,
            entityName);
}
