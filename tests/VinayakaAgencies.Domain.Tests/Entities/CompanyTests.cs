using VinayakaAgencies.Domain.Entities;

namespace VinayakaAgencies.Domain.Tests.Entities;

public class CompanyTests
{
    private const string ValidName = "Shakti Motors & Pumps";
    private const string ValidLegalName = "Shakti Motors & Pumps Private Limited";
    private const string ValidStateCode = "27";

    [Fact]
    public void Constructor_ValidArguments_TrimsAndAssignsFields()
    {
        var company = new Company(
            $"  {ValidName}  ",
            $"  {ValidLegalName}  ",
            $"  {ValidStateCode}  ");

        company.Name.ShouldBe(ValidName);
        company.LegalName.ShouldBe(ValidLegalName);
        company.StateCode.ShouldBe(ValidStateCode);
    }

    [Fact]
    public void Constructor_NoOverridesGiven_DefaultsToAprilFinancialYearAndInrCurrency()
    {
        var company = new Company(ValidName, ValidLegalName, ValidStateCode);

        company.FinancialYearStartMonth.ShouldBe(4);
        company.BaseCurrencyCode.ShouldBe("INR");
    }

    [Fact]
    public void Constructor_NewCompany_IsActiveByDefault()
    {
        var company = new Company(ValidName, ValidLegalName, ValidStateCode);

        company.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_NewCompany_AssignsNonEmptyId()
    {
        var company = new Company(ValidName, ValidLegalName, ValidStateCode);

        company.Id.ShouldNotBe(Guid.Empty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankName_ThrowsArgumentException(string? blankName)
    {
        Should.Throw<ArgumentException>(
            () => new Company(blankName!, ValidLegalName, ValidStateCode));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankLegalName_ThrowsArgumentException(string? blankLegalName)
    {
        Should.Throw<ArgumentException>(
            () => new Company(ValidName, blankLegalName!, ValidStateCode));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankStateCode_ThrowsArgumentException(string? blankStateCode)
    {
        Should.Throw<ArgumentException>(
            () => new Company(ValidName, ValidLegalName, blankStateCode!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankCurrency_ThrowsArgumentException(string? blankCurrency)
    {
        Should.Throw<ArgumentException>(
            () => new Company(
                ValidName,
                ValidLegalName,
                ValidStateCode,
                baseCurrencyCode: blankCurrency!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    [InlineData(100)]
    public void Constructor_FinancialYearStartMonthOutOfRange_ThrowsArgumentOutOfRangeException(
        int invalidMonth)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new Company(
                ValidName,
                ValidLegalName,
                ValidStateCode,
                financialYearStartMonth: invalidMonth));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void Constructor_FinancialYearStartMonthAtBoundary_IsAccepted(int boundaryMonth)
    {
        var company = new Company(
            ValidName,
            ValidLegalName,
            ValidStateCode,
            financialYearStartMonth: boundaryMonth);

        company.FinancialYearStartMonth.ShouldBe(boundaryMonth);
    }

    [Fact]
    public void Constructor_GstinWithLowerCaseAndWhitespace_IsNormalisedToTrimmedUpperCase()
    {
        var company = new Company(
            ValidName,
            ValidLegalName,
            ValidStateCode,
            gstin: "  27aaaaa0000a1z5  ");

        company.Gstin.ShouldBe("27AAAAA0000A1Z5");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankGstin_BecomesNull(string? blankGstin)
    {
        var company = new Company(ValidName, ValidLegalName, ValidStateCode, gstin: blankGstin);

        company.Gstin.ShouldBeNull();
    }

    [Fact]
    public void Rename_ValidArguments_TrimsAndUpdatesNameAndLegalName()
    {
        var company = new Company(ValidName, ValidLegalName, ValidStateCode);

        company.Rename("  New Trading Name  ", "  New Legal Name Pvt Ltd  ");

        company.Name.ShouldBe("New Trading Name");
        company.LegalName.ShouldBe("New Legal Name Pvt Ltd");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_BlankName_ThrowsArgumentException(string? blankName)
    {
        var company = new Company(ValidName, ValidLegalName, ValidStateCode);

        Should.Throw<ArgumentException>(() => company.Rename(blankName!, ValidLegalName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_BlankLegalName_ThrowsArgumentException(string? blankLegalName)
    {
        var company = new Company(ValidName, ValidLegalName, ValidStateCode);

        Should.Throw<ArgumentException>(() => company.Rename(ValidName, blankLegalName!));
    }

    [Fact]
    public void Rename_ThrowsBeforeMutating_LeavesOriginalNameIntact()
    {
        var company = new Company(ValidName, ValidLegalName, ValidStateCode);

        Should.Throw<ArgumentException>(() => company.Rename(string.Empty, ValidLegalName));

        company.Name.ShouldBe(ValidName);
        company.LegalName.ShouldBe(ValidLegalName);
    }

    [Fact]
    public void SetGstin_LowerCaseWithWhitespace_NormalisesToTrimmedUpperCase()
    {
        var company = new Company(ValidName, ValidLegalName, ValidStateCode);

        company.SetGstin("  29bbbbb1111b1z6  ");

        company.Gstin.ShouldBe("29BBBBB1111B1Z6");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetGstin_BlankValue_ClearsGstinToNull(string? blankGstin)
    {
        var company = new Company(ValidName, ValidLegalName, ValidStateCode, gstin: "27AAAAA0000A1Z5");

        company.SetGstin(blankGstin);

        company.Gstin.ShouldBeNull();
    }

    [Fact]
    public void Deactivate_ActiveCompany_SetsIsActiveFalse()
    {
        var company = new Company(ValidName, ValidLegalName, ValidStateCode);

        company.Deactivate();

        company.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Activate_DeactivatedCompany_SetsIsActiveTrue()
    {
        var company = new Company(ValidName, ValidLegalName, ValidStateCode);
        company.Deactivate();

        company.Activate();

        company.IsActive.ShouldBeTrue();
    }
}
