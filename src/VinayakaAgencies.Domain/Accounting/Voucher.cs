using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Accounting;

/// <summary>
/// The immutable identity of a voucher. Everything that can change lives on
/// <see cref="VoucherRevision"/>.
/// </summary>
/// <remarks>
/// ADR-0002. This split is what makes the journal append-only: altering a voucher writes a new
/// revision and supersedes the old one, so the original is still readable rather than overwritten.
/// This GUID is also the stable id Phase 8 exports to Tally, whose import overwrites by GUID — so it
/// must survive every alteration untouched.
/// </remarks>
public sealed class Voucher : Entity, IAuditStamped
{
    private Voucher()
    {
        // EF Core materialisation.
    }

    public Voucher(Guid companyId, Guid voucherTypeId, Guid seriesId, Guid financialYearId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId must not be empty.", nameof(companyId));
        }

        if (voucherTypeId == Guid.Empty)
        {
            throw new ArgumentException("VoucherTypeId must not be empty.", nameof(voucherTypeId));
        }

        CompanyId = companyId;
        VoucherTypeId = voucherTypeId;
        SeriesId = seriesId;
        FinancialYearId = financialYearId;
    }

    public Guid CompanyId { get; private set; }

    public Guid VoucherTypeId { get; private set; }

    public Guid SeriesId { get; private set; }

    public Guid FinancialYearId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset? ModifiedAtUtc { get; set; }

    public string? ModifiedBy { get; set; }
}
