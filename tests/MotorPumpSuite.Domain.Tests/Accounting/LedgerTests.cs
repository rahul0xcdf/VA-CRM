using MotorPumpSuite.Domain.Accounting;

namespace MotorPumpSuite.Domain.Tests.Accounting;

/// <summary>
/// Spec §4.5/§4.6: a ledger is "fully locked" when it is both system and forbidden manual posting
/// (the Profit &amp; Loss A/c pattern, L2) — that is the only combination that also blocks rename.
/// A merely-system ledger with manual posting allowed (the Cash pattern, L1) may still be renamed.
/// </summary>
public class LedgerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid GroupId = Guid.NewGuid();

    [Fact]
    public void Rename_FullyLockedLedger_ThrowsInvalidOperationException()
    {
        var ledger = new Ledger(
            CompanyId, "Profit & Loss A/c", GroupId, isSystem: true, allowsManualPosting: false);

        Should.Throw<InvalidOperationException>(() => ledger.Rename("New Name"));
    }

    [Fact]
    public void Rename_SystemLedgerThatAllowsManualPosting_Succeeds()
    {
        var ledger = new Ledger(
            CompanyId, "Cash", GroupId, isSystem: true, allowsManualPosting: true);

        ledger.Rename("Petty Cash");

        ledger.Name.ShouldBe("Petty Cash");
    }

    [Fact]
    public void MoveTo_SystemLedger_ThrowsInvalidOperationException_EvenWhenManualPostingAllowed()
    {
        var ledger = new Ledger(
            CompanyId, "Cash", GroupId, isSystem: true, allowsManualPosting: true);

        Should.Throw<InvalidOperationException>(() => ledger.MoveTo(Guid.NewGuid()));
    }

    [Fact]
    public void Deactivate_SystemLedger_ThrowsInvalidOperationException()
    {
        var ledger = new Ledger(
            CompanyId, "Cash", GroupId, isSystem: true, allowsManualPosting: true);

        Should.Throw<InvalidOperationException>(ledger.Deactivate);
    }

    [Fact]
    public void Deactivate_NonSystemLedger_Succeeds()
    {
        var ledger = new Ledger(CompanyId, "Sales", GroupId);

        ledger.Deactivate();

        ledger.IsActive.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankAlias_BecomesNull(string blankAlias)
    {
        var ledger = new Ledger(CompanyId, "Sales", GroupId, alias: blankAlias);

        ledger.Alias.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankDescription_BecomesNull(string blankDescription)
    {
        var ledger = new Ledger(CompanyId, "Sales", GroupId, description: blankDescription);

        ledger.Description.ShouldBeNull();
    }

    [Fact]
    public void SetDescription_BlankValue_ClearsToNull()
    {
        var ledger = new Ledger(CompanyId, "Sales", GroupId, description: "Original description");

        ledger.SetDescription("   ");

        ledger.Description.ShouldBeNull();
    }

    [Fact]
    public void Constructor_AliasWithWhitespace_IsTrimmed()
    {
        var ledger = new Ledger(CompanyId, "Sales", GroupId, alias: "  SAL  ");

        ledger.Alias.ShouldBe("SAL");
    }

    [Fact]
    public void Constructor_EmptyAccountGroupId_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => new Ledger(CompanyId, "Sales", Guid.Empty));
    }

    [Fact]
    public void Constructor_EmptyCompanyId_ThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() => new Ledger(Guid.Empty, "Sales", GroupId));
    }
}
