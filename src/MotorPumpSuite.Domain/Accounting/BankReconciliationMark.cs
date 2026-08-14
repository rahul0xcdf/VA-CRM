using MotorPumpSuite.Domain.Common;

namespace MotorPumpSuite.Domain.Accounting;

/// <summary>
/// Records that a journal line has cleared the bank, and on what date.
/// </summary>
/// <remarks>
/// Held separately from <see cref="JournalLine"/> because the journal is insert-only (ADR-0002):
/// marking a line as cleared happens weeks after it was posted, and must not require touching the
/// posted line. Manual only in Phase 1; statement import is a later phase.
/// </remarks>
public sealed class BankReconciliationMark : Entity, IAuditStamped
{
    private BankReconciliationMark()
    {
        // EF Core materialisation.
    }

    public BankReconciliationMark(Guid companyId, Guid journalLineId, DateOnly bankDate)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (journalLineId == Guid.Empty)
        {
            throw new ArgumentException("JournalLineId must not be empty.", nameof(journalLineId));
        }

        CompanyId = companyId;
        JournalLineId = journalLineId;
        BankDate = bankDate;
    }

    public Guid CompanyId { get; private set; }

    public Guid JournalLineId { get; private set; }

    /// <summary>The date the bank shows, which is often not the voucher date.</summary>
    public DateOnly BankDate { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }

    public void SetBankDate(DateOnly bankDate) => BankDate = bankDate;
}
