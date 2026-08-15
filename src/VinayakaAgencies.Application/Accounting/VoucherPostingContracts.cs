using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Application.Accounting;

/// <summary>
/// One line of a voucher being posted. Exactly one of <see cref="Debit"/> or <see cref="Credit"/>
/// carries a non-zero value.
/// </summary>
/// <remarks>
/// The UI works in two columns because that is how an accountant reads a voucher; the domain
/// collapses them to a single signed amount (see <c>JournalLine</c>) because one column makes the
/// balance invariant a single exact comparison.
/// </remarks>
public sealed record PostVoucherLine(
    Guid LedgerId,
    decimal Debit,
    decimal Credit,
    string? Narration = null,
    Guid? CostCentreId = null);

public sealed record PostVoucherRequest(
    Guid VoucherTypeId,
    DateOnly VoucherDate,
    IReadOnlyList<PostVoucherLine> Lines,
    string? Narration = null,
    string? ReferenceNumber = null,
    DateOnly? ReferenceDate = null,
    string? ManualNumber = null);

public sealed record AlterVoucherRequest(
    Guid VoucherId,
    DateOnly VoucherDate,
    IReadOnlyList<PostVoucherLine> Lines,
    string ChangeReason,
    string? Narration = null,
    string? ReferenceNumber = null,
    DateOnly? ReferenceDate = null);

public sealed record PostedVoucherSummary(
    Guid VoucherId,
    Guid RevisionId,
    int RevisionNumber,
    string Number,
    DateOnly VoucherDate,
    VoucherStatus Status,
    string VoucherTypeName,
    VoucherKind Kind,
    decimal TotalDebit,
    decimal TotalCredit,
    string? Narration);

/// <summary>Read model for a voucher type, used to populate the entry screen's picker.</summary>
public sealed record VoucherTypeSummary(
    Guid Id,
    string Name,
    VoucherKind Kind,
    Guid? DefaultSeriesId,
    bool IsActive);

/// <summary>
/// The journal engine: the only sanctioned way a voucher reaches the books.
/// </summary>
/// <remarks>
/// Posting is refused unless debits equal credits exactly (spec §8.1), the date falls in an open
/// period (§12), and every named ledger permits manual posting. Altering writes a new revision and
/// supersedes the old one rather than mutating it (ADR-0002); cancelling writes a revision with no
/// lines, which keeps the voucher's number allocated so the series has no reusable gap.
/// </remarks>
public interface IVoucherPostingService
{
    Task<IReadOnlyList<VoucherTypeSummary>> ListVoucherTypesAsync(
        CancellationToken cancellationToken = default);

    Task<PostedVoucherSummary> PostAsync(
        PostVoucherRequest request,
        CancellationToken cancellationToken = default);

    Task<PostedVoucherSummary> AlterAsync(
        AlterVoucherRequest request,
        CancellationToken cancellationToken = default);

    Task<PostedVoucherSummary> CancelAsync(
        Guid voucherId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<PostedVoucherSummary?> GetCurrentAsync(
        Guid voucherId,
        CancellationToken cancellationToken = default);
}
