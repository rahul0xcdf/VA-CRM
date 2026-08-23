using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Accounting;

/// <summary>
/// The high-water mark of a series within one financial year.
/// </summary>
/// <remarks>
/// <see cref="LastAllocatedNumber"/> only ever increases. Cancelling a voucher does not give its
/// number back (owner decision D2): reuse would let two documents share an invoice number, which is
/// a GST problem, while a gap is merely something to explain.
/// <para>
/// Allocation is a read-then-increment, which is safe only because Phases 1–8 are single-user. Phase
/// 9 must replace this with a database-level atomic allocation — flagged in the Phase 1 spec §7.2.
/// </para>
/// </remarks>
public sealed class VoucherNumberSequence : Entity
{
    private VoucherNumberSequence()
    {
        // EF Core materialisation.
    }

    public VoucherNumberSequence(Guid seriesId, Guid financialYearId, long lastAllocatedNumber = 0)
    {
        if (seriesId == Guid.Empty)
        {
            throw new ArgumentException("SeriesId must not be empty.", nameof(seriesId));
        }

        if (financialYearId == Guid.Empty)
        {
            throw new ArgumentException("FinancialYearId must not be empty.", nameof(financialYearId));
        }

        SeriesId = seriesId;
        FinancialYearId = financialYearId;
        LastAllocatedNumber = lastAllocatedNumber;
    }

    public Guid SeriesId { get; private set; }

    public Guid FinancialYearId { get; private set; }

    public long LastAllocatedNumber { get; private set; }

    /// <summary>Takes the next number. Never returns a value it has returned before.</summary>
    public long Allocate(long startNumber)
    {
        var next = Math.Max(LastAllocatedNumber + 1, startNumber);
        LastAllocatedNumber = next;
        return next;
    }

    /// <summary>
    /// Raises the mark past a manually entered number so the automatic series cannot later collide
    /// with it.
    /// </summary>
    public void ObserveManualNumber(long number)
    {
        if (number > LastAllocatedNumber)
        {
            LastAllocatedNumber = number;
        }
    }
}
