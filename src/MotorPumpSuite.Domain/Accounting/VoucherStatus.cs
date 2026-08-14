namespace MotorPumpSuite.Domain.Accounting;

/// <summary>
/// The state of a voucher revision.
/// </summary>
/// <remarks>
/// There is no <c>Deleted</c> state. The journal is append-only (ADR-0002): cancelling writes a new
/// revision carrying no journal lines, which keeps the voucher's number allocated and its history
/// readable rather than removing evidence that it ever existed.
/// </remarks>
public enum VoucherStatus
{
    Posted = 1,
    Cancelled = 2,
}
