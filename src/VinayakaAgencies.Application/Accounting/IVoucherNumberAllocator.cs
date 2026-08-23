namespace VinayakaAgencies.Application.Accounting;

/// <summary>
/// Claims the next number in a voucher type's series (spec §7.2, decision D2).
/// </summary>
/// <remarks>
/// Allocation happens at post time, inside the same unit-of-work transaction that writes the voucher
/// and its lines, and after validation — so a voucher that fails validation never consumes a number.
/// This allocator therefore never opens a transaction and never commits one; it participates in the
/// caller's.
/// <para>
/// Numbers are never released. Cancelling a voucher keeps its number, because the cancelled revision
/// is still the current one and still occupies the filtered unique index, and vouchers are never
/// deleted. A gap can consequently only come from a manual override that skips ahead or from a
/// future data import, and both are surfaced by the <c>Accounting.VoucherNumberGaps</c> report rather
/// than quietly backfilled.
/// </para>
/// <para>
/// Concurrency is deferred to Phase 9 (<c>open-questions.md</c> #4). Read-modify-write of the
/// sequence's high-water mark is sufficient while Phases 1–8 are single-user; under SQL Server with
/// concurrent clients this needs an <c>UPDATE … OUTPUT</c> or a row lock, and the filtered unique
/// index stops being a formality and becomes the last line of defence.
/// </para>
/// </remarks>
public interface IVoucherNumberAllocator
{
    /// <summary>
    /// Claims a number for a voucher of <paramref name="voucherTypeId"/> dated
    /// <paramref name="voucherDate"/>.
    /// </summary>
    /// <param name="manualNumber">
    /// An operator-supplied number, or null to let the series allocate. When supplied and numerically
    /// above the series' high-water mark, the mark advances to it so later automatic numbers continue
    /// past it and cannot collide.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The voucher type has no active series; or a manual number was supplied to a series with
    /// <c>AllowManualOverride = false</c>; or the supplied number is already held by a current
    /// revision in the same series and financial year.
    /// </exception>
    Task<AllocatedVoucherNumber> AllocateAsync(
        Guid companyId,
        Guid voucherTypeId,
        DateOnly voucherDate,
        string? manualNumber = null,
        CancellationToken cancellationToken = default);
}
