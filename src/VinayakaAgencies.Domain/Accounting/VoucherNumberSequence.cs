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
/// Allocation is a read-then-increment. Through Phase 8 that was safe only because the product was
/// single-user, which the Phase 1 spec §7.2 flagged. Phase 9 closes it two ways: the allocator takes
/// a database-level lock on this row before reading it (<c>Accounting.LockVoucherNumberSequence</c>),
/// and the row carries a concurrency token so a race that somehow slipped the lock is refused rather
/// than handing two counters the same number.
/// </para>
/// </remarks>
public sealed class VoucherNumberSequence : Entity, IConcurrencyStamped
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

    /// <inheritdoc />
    public Guid ConcurrencyToken { get; set; }

    /// <summary>Takes the next number. Never returns a value it has returned before.</summary>
    public long Allocate(long startNumber)
    {
        var next = Math.Max(LastAllocatedNumber + 1, startNumber);
        LastAllocatedNumber = next;
        return next;
    }

    /// <summary>
    /// Raises the mark to <paramref name="number"/>, or leaves it alone if it is already higher.
    /// </summary>
    /// <remarks>
    /// How the allocator reconciles its tracked copy with what it read under the row lock. Waiting
    /// for that lock is exactly the window in which the other counter allocated, so the in-memory
    /// value can be behind by however many numbers it took — and re-reading through the repository
    /// would hand back the same cached instance rather than the truth.
    /// <para>
    /// Never lowers the mark. A number that has been handed out is spent whatever happens next, so
    /// moving the mark backwards is the one operation that could issue it twice.
    /// </para>
    /// </remarks>
    public void AdvanceTo(long number)
    {
        if (number > LastAllocatedNumber)
        {
            LastAllocatedNumber = number;
        }
    }

    /// <summary>
    /// Raises the mark past a manually entered number so the automatic series cannot later collide
    /// with it.
    /// </summary>
    public void ObserveManualNumber(long number) => AdvanceTo(number);
}
