using VinayakaAgencies.Domain.Accounting;
using VinayakaAgencies.Domain.Common;

namespace VinayakaAgencies.Domain.Tests.Accounting;

/// <summary>
/// Every master whose name carries a unique index also carries the normalised comparison form the
/// index is built over, and keeps it in step with the display name. Without it, SQLite (case-
/// sensitive BINARY collation) would accept both "Cash" and "cash" through Phase 8 and the Phase 9
/// SQL Server index creation would fail on data that is already duplicated.
/// </summary>
public class MasterNameNormalisationTests
{
    [Fact]
    public void Ledger_NamesDifferingOnlyByCase_ShareOneNameNormalised()
    {
        var companyId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var upper = new Ledger(companyId, "Cash", groupId);
        var lower = new Ledger(companyId, " cash ", groupId);

        upper.NameNormalised.ShouldBe(lower.NameNormalised);
        upper.NameNormalised.ShouldBe(NameNormaliser.Normalise("Cash"));
        upper.Name.ShouldBe("Cash");
        lower.Name.ShouldBe("cash");
    }

    [Fact]
    public void Ledger_Rename_UpdatesNameNormalised()
    {
        var ledger = new Ledger(Guid.NewGuid(), "Cash", Guid.NewGuid());

        ledger.Rename("petty  cash");

        ledger.Name.ShouldBe("petty  cash");
        ledger.NameNormalised.ShouldBe("PETTY CASH");
    }

    [Fact]
    public void AccountGroup_NamesDifferingOnlyByCase_ShareOneNameNormalised()
    {
        var companyId = Guid.NewGuid();

        var upper = new AccountGroup(companyId, "Sundry Debtors", AccountNature.Asset);
        var lower = new AccountGroup(companyId, "sundry debtors", AccountNature.Asset);

        upper.NameNormalised.ShouldBe(lower.NameNormalised);
    }

    [Fact]
    public void AccountGroup_Rename_UpdatesNameNormalised()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Sundry Debtors", AccountNature.Asset);

        group.Rename(" Trade Receivables ");

        group.Name.ShouldBe("Trade Receivables");
        group.NameNormalised.ShouldBe("TRADE RECEIVABLES");
    }

    [Fact]
    public void VoucherType_NamesDifferingOnlyByCase_ShareOneNameNormalised()
    {
        var companyId = Guid.NewGuid();

        var upper = new VoucherType(companyId, "Bank Payment", VoucherKind.Payment);
        var lower = new VoucherType(companyId, "bank payment", VoucherKind.Payment);

        upper.NameNormalised.ShouldBe(lower.NameNormalised);
    }

    [Fact]
    public void VoucherType_Rename_UpdatesNameNormalised()
    {
        var type = new VoucherType(Guid.NewGuid(), "Bank Payment", VoucherKind.Payment);

        type.Rename("Cash Payment");

        type.NameNormalised.ShouldBe("CASH PAYMENT");
    }

    [Fact]
    public void VoucherNumberSeries_NamesDifferingOnlyByCase_ShareOneNameNormalised()
    {
        var companyId = Guid.NewGuid();
        var voucherTypeId = Guid.NewGuid();

        var upper = new VoucherNumberSeries(companyId, voucherTypeId, "Default Series", "PMT/");
        var lower = new VoucherNumberSeries(companyId, voucherTypeId, "default series", "PMT/");

        upper.NameNormalised.ShouldBe(lower.NameNormalised);
        upper.NameNormalised.ShouldBe("DEFAULT SERIES");
    }

    [Fact]
    public void CostCategory_NamesDifferingOnlyByCase_ShareOneNameNormalised()
    {
        var companyId = Guid.NewGuid();

        var upper = new CostCategory(companyId, "Branch");
        var lower = new CostCategory(companyId, "branch");

        upper.NameNormalised.ShouldBe(lower.NameNormalised);
    }

    [Fact]
    public void CostCategory_Rename_UpdatesNameNormalised()
    {
        var category = new CostCategory(Guid.NewGuid(), "Branch");

        category.Rename("Salesperson");

        category.NameNormalised.ShouldBe("SALESPERSON");
    }

    [Fact]
    public void CostCentre_NamesDifferingOnlyByCase_ShareOneNameNormalised()
    {
        var companyId = Guid.NewGuid();

        var upper = new CostCentre(companyId, "Head Office");
        var lower = new CostCentre(companyId, "head  office");

        upper.NameNormalised.ShouldBe(lower.NameNormalised);
    }

    [Fact]
    public void CostCentre_Rename_UpdatesNameNormalised()
    {
        var centre = new CostCentre(Guid.NewGuid(), "Head Office");

        centre.Rename("Bengaluru Branch");

        centre.NameNormalised.ShouldBe("BENGALURU BRANCH");
    }
}
