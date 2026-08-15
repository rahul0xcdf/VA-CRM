namespace VinayakaAgencies.Domain.Common;

/// <summary>
/// Marks an entity whose creation and last modification are stamped centrally on save.
/// </summary>
/// <remarks>
/// These stamps are <b>not</b> the audit trail. The full before/after edit log required by
/// project.md §4.1 (MCA Rule 3(1)) is built in Phase 1; it hangs off this same interception point
/// in <c>SaveChangesAsync</c> so that it cannot be bypassed by a caller forgetting to log.
/// </remarks>
public interface IAuditStamped
{
    DateTimeOffset CreatedAtUtc { get; set; }

    string CreatedBy { get; set; }

    DateTimeOffset? ModifiedAtUtc { get; set; }

    string? ModifiedBy { get; set; }
}
