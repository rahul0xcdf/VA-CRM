namespace VinayakaAgencies.Domain.Common;

/// <summary>
/// Marks a row whose updates are checked for a lost update.
/// </summary>
/// <remarks>
/// `phase-9-multiuser.md` §2: "optimistic concurrency tokens on every mutable entity; a lost-update
/// must be detected and surfaced, never silently overwritten."
/// <para>
/// Split out of <see cref="IAuditStamped"/> rather than living on it, because the two sets are not
/// quite the same. Every audit-stamped entity needs a token — that interface is what the model uses
/// to mean "mutable". But <c>VoucherNumberSequence</c> needs one too and is not a master anybody
/// edits: it is a counter the posting path bumps, with no author worth recording beyond the audit
/// row the save already writes. Giving it <c>Created*</c> and <c>Modified*</c> columns to obtain a
/// token would be schema for the sake of a type hierarchy.
/// </para>
/// <para>
/// <b>A real property, not an EF shadow property.</b> It was written as a shadow property first,
/// and that broke every service which reads with <c>AsNoTracking</c> and then calls <c>Update</c>
/// on the detached instance — <c>GstRateService.AddAsync</c> was the one the suite caught. A
/// no-tracking read does not carry shadow values, so re-attaching sent an all-zero token as the
/// original value, matched no row, and turned an ordinary save into a spurious lost-update. Keeping
/// the token on the entity means it survives the round trip through a detached object, which is how
/// much of this codebase updates things.
/// </para>
/// </remarks>
public interface IConcurrencyStamped
{
    /// <summary>
    /// Optimistic-concurrency token, replaced on every insert and update.
    /// </summary>
    /// <remarks>
    /// Settable because <c>AppDbContext.SaveChangesAsync</c> assigns it centrally, for the same
    /// reason the audit stamps are settable: a caller must not be able to forget.
    /// </remarks>
    Guid ConcurrencyToken { get; set; }
}
