namespace VinayakaAgencies.Application.Accounting;

/// <summary>
/// Serialises voucher-number allocation on the sequence row, inside the caller's transaction.
/// </summary>
/// <remarks>
/// `phase-9-multiuser.md` §2 requires "a server-side allocation strategy, not a client-side
/// read-max-then-increment". Phase 1 got half of that right already: the high-water mark is a row
/// (<c>VoucherNumberSequence</c>), not a running maximum taken over the vouchers. What was still
/// client-side was the read-modify-write around it, which two counters can interleave.
/// <para>
/// <b>Why this is a port rather than a reporting query.</b> The lock has to be taken on the
/// connection the voucher is being written on, and held until that transaction commits. The Dapper
/// path opens its own connection, so a lock taken through <c>ISqlQueryExecutor</c> would be held by
/// a connection that commits immediately and protect nothing — on SQLite it would deadlock against
/// the very transaction it was meant to guard. So this is satisfied in Infrastructure, which is the
/// only layer allowed to know a connection exists.
/// </para>
/// <para>
/// <b>Why not a SQL Server <c>SEQUENCE</c>.</b> `open-questions.md` #11 is Confirmed that gaps are
/// blocked, and sequences lose numbers by design — on rollback, and on cache eviction at restart.
/// A gap in an invoice series is a GST conversation; a slow allocation is not.
/// </para>
/// </remarks>
public interface IVoucherNumberSequenceLock
{
    /// <summary>
    /// Takes an exclusive lock on the sequence row and reads its high-water mark under that lock.
    /// </summary>
    /// <returns>
    /// The stored <c>LastAllocatedNumber</c>, or <c>null</c> if no row exists for this series and
    /// year yet — which is the first allocation of a financial year.
    /// </returns>
    /// <remarks>
    /// Returning the mark rather than only locking is deliberate. After waiting for the lock, the
    /// caller's tracked copy of the row may be stale by exactly the amount the other counter
    /// allocated while it waited, and reading it again through the repository would hand back the
    /// cached instance. The value read here comes from the database under the lock, so it is the
    /// only one worth allocating from.
    /// </remarks>
    Task<long?> LockAndReadAsync(
        Guid seriesId,
        Guid financialYearId,
        CancellationToken cancellationToken = default);
}
