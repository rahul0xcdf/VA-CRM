namespace VinayakaAgencies.Domain.Common;

/// <summary>
/// Marks a type whose creation is stamped centrally on save with who created it and when.
/// </summary>
/// <remarks>
/// Split out of <see cref="IAuditStamped"/> for the append-only types. A
/// <see cref="Accounting.VoucherRevision"/> is never updated (ADR-0002), so it has no
/// <c>Modified*</c> pair and cannot implement <see cref="IAuditStamped"/> — but it still needs an
/// author, and an unattributed voucher revision is an audit hole rather than a cosmetic gap.
/// <para>
/// These stamps are <b>not</b> the audit trail. The full before/after edit log required by
/// project.md §4.1 (MCA Rule 3(1)) is separate; both hang off the same <c>SaveChangesAsync</c>
/// interception point so that neither can be bypassed by a caller forgetting to call it.
/// </para>
/// </remarks>
public interface ICreationStamped
{
    DateTimeOffset CreatedAtUtc { get; set; }

    string CreatedBy { get; set; }
}
