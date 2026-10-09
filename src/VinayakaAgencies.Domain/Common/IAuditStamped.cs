namespace VinayakaAgencies.Domain.Common;

/// <summary>
/// Marks an entity whose creation and last modification are stamped centrally on save.
/// </summary>
/// <remarks>
/// These stamps are <b>not</b> the audit trail. The full before/after edit log required by
/// project.md §4.1 (MCA Rule 3(1)) is built in Phase 1; it hangs off this same interception point
/// in <c>SaveChangesAsync</c> so that it cannot be bypassed by a caller forgetting to log.
/// <para>
/// Extends <see cref="ICreationStamped"/>: a mutable entity records who created it <i>and</i> who
/// last changed it, whereas an append-only type records only the former.
/// </para>
/// </remarks>
public interface IAuditStamped : ICreationStamped
{
    DateTimeOffset? ModifiedAtUtc { get; set; }

    string? ModifiedBy { get; set; }

    /// <summary>
    /// Optimistic-concurrency token, replaced on every insert and update.
    /// </summary>
    /// <remarks>
    /// `phase-9-multiuser.md` §2 requires a token on every mutable entity, and this interface is
    /// already where the model says "mutable" — so it lives here beside <see cref="ModifiedBy"/>
    /// rather than on <c>Entity</c>, which would put one on append-only types that ADR-0002 forbids
    /// updating at all.
    /// <para>
    /// <b>A real property, not an EF shadow property.</b> It was written as a shadow property first,
    /// and that broke every service which reads with <c>AsNoTracking</c> and then calls
    /// <c>Update</c> on the detached instance — <c>GstRateService.AddAsync</c> was the one the suite
    /// caught. A no-tracking read does not carry shadow values, so re-attaching sent an all-zero
    /// token as the original value, matched no row, and turned an ordinary save into a spurious
    /// lost-update. Keeping the token on the entity means it survives the round trip through a
    /// detached object, which is how half this codebase updates things.
    /// </para>
    /// <para>
    /// Settable because <c>AppDbContext.SaveChangesAsync</c> assigns it centrally, for the same
    /// reason the stamps above are settable: a caller must not be able to forget.
    /// </para>
    /// </remarks>
    Guid ConcurrencyToken { get; set; }
}
