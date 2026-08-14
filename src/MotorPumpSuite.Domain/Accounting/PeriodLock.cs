using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Domain.Accounting;

/// <summary>
/// The date through which the books are closed to new postings.
/// </summary>
/// <remarks>
/// One row per company. Moving the lock is itself a master change and is edit-logged, because
/// re-opening a filed period is precisely the action an auditor needs to be able to see.
/// </remarks>
public sealed class PeriodLock : Entity, IAuditStamped
{
    private PeriodLock()
    {
        // EF Core materialisation.
    }

    public PeriodLock(Guid companyId, DateOnly? lockedThroughDate = null, string? reason = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        CompanyId = companyId;
        LockedThroughDate = lockedThroughDate;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public Guid CompanyId { get; private set; }

    /// <summary>Null means nothing is locked. Otherwise postings on or before this date are refused.</summary>
    public DateOnly? LockedThroughDate { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public bool IsLocked(DateOnly date) =>
        LockedThroughDate is { } through && date <= through;

    public void LockThrough(DateOnly date, string? reason = null)
    {
        LockedThroughDate = date;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public void Unlock(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        LockedThroughDate = null;
        Reason = reason.Trim();
    }
}
