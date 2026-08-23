using VinayakaAgencies.Domain.Accounting;

namespace VinayakaAgencies.Domain.Tests.Accounting;

/// <summary>
/// <see cref="Voucher"/> is a permanently immutable identity (spec §8.2, ADR-0002). Its four ids are
/// copied onto every <see cref="VoucherRevision"/> (§5) and four of them are key columns of the
/// filtered unique index that enforces voucher numbering, so none of them may be empty.
/// </summary>
public class VoucherTests
{
    [Fact]
    public void Constructor_ValidIds_CopiesAllFour()
    {
        var companyId = Guid.NewGuid();
        var voucherTypeId = Guid.NewGuid();
        var seriesId = Guid.NewGuid();
        var financialYearId = Guid.NewGuid();

        var voucher = new Voucher(companyId, voucherTypeId, seriesId, financialYearId);

        voucher.CompanyId.ShouldBe(companyId);
        voucher.VoucherTypeId.ShouldBe(voucherTypeId);
        voucher.SeriesId.ShouldBe(seriesId);
        voucher.FinancialYearId.ShouldBe(financialYearId);
    }

    [Fact]
    public void Constructor_EmptyCompanyId_IsRejected() =>
        Should.Throw<ArgumentException>(() =>
            new Voucher(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

    [Fact]
    public void Constructor_EmptyVoucherTypeId_IsRejected() =>
        Should.Throw<ArgumentException>(() =>
            new Voucher(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Guid.NewGuid()));

    [Fact]
    public void Constructor_EmptySeriesId_IsRejected() =>
        Should.Throw<ArgumentException>(() =>
            new Voucher(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Guid.NewGuid()));

    /// <summary>
    /// The sharpest of the four. An empty financial year would not fail loudly: it would collapse
    /// every affected revision into one bucket of the
    /// <c>(CompanyId, VoucherTypeId, SeriesId, FinancialYearId, NumberNormalised)</c> unique index,
    /// so two vouchers carrying genuinely different numbers would collide.
    /// </summary>
    [Fact]
    public void Constructor_EmptyFinancialYearId_IsRejected() =>
        Should.Throw<ArgumentException>(() =>
            new Voucher(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty));

    [Fact]
    public void Constructor_AlwaysGeneratesAnId()
    {
        var voucher = new Voucher(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        voucher.Id.ShouldNotBe(Guid.Empty);
    }
}
