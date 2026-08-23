using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.Domain.Tests.Customers;

public class CustomerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid LedgerId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();

    [Fact]
    public void Constructor_BlankName_Throws()
    {
        Should.Throw<ArgumentException>(
            () => new Customer(CompanyId, "   ", LedgerId, CategoryId));
    }

    [Fact]
    public void Constructor_EmptyLedgerIdOrCategoryId_Throws()
    {
        // The ledger is the customer's account in the books; the category prices every sale line.
        // Neither link may be empty or the receivables and the price engine both go blind.
        Should.Throw<ArgumentException>(
            () => new Customer(CompanyId, "Anand Traders", Guid.Empty, CategoryId));

        Should.Throw<ArgumentException>(
            () => new Customer(CompanyId, "Anand Traders", LedgerId, Guid.Empty));
    }

    [Fact]
    public void Constructor_NormalisesNameForUniqueness()
    {
        var customer = new Customer(CompanyId, "  anand TRADERS ", LedgerId, CategoryId);

        customer.Name.ShouldBe("anand TRADERS");
        customer.NameNormalised.ShouldBe("ANAND TRADERS");
    }

    [Fact]
    public void Constructor_UpperCasesGstinLikeCompanyDoes()
    {
        var customer = new Customer(
            CompanyId, "Anand Traders", LedgerId, CategoryId, gstin: "29aabca1234a1z5");

        customer.Gstin.ShouldBe("29AABCA1234A1Z5");
    }

    [Fact]
    public void SetPlaceOfSupplyStateCode_NotTwoCharacters_Throws()
    {
        var customer = NewCustomer();

        Should.Throw<ArgumentOutOfRangeException>(
            () => customer.SetPlaceOfSupplyStateCode("2900"));

        Should.Throw<ArgumentOutOfRangeException>(
            () => customer.SetPlaceOfSupplyStateCode("2"));
    }

    [Fact]
    public void SetCreditTerms_NegativeLimitOrPeriod_Throws()
    {
        var customer = NewCustomer();

        Should.Throw<ArgumentOutOfRangeException>(
            () => customer.SetCreditTerms(-1m, creditPeriodDays: 30));

        Should.Throw<ArgumentOutOfRangeException>(
            () => customer.SetCreditTerms(100_000m, creditPeriodDays: -1));
    }

    [Fact]
    public void SetCreditTerms_ZeroLimitStoresZeroAsNoLimit()
    {
        var customer = NewCustomer();

        customer.SetCreditTerms(0m, creditPeriodDays: null);

        customer.CreditLimit.ShouldBe(0m);
        customer.CreditLimitMinor.ShouldBe(0L);
        customer.CreditPeriodDays.ShouldBeNull();
    }

    private static Customer NewCustomer() =>
        new(CompanyId, "Anand Traders", LedgerId, CategoryId);
}
