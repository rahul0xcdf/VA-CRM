using VinayakaAgencies.Domain.Customers;

namespace VinayakaAgencies.Domain.Tests.Customers;

public class SalesSettingsTests
{
    [Fact]
    public void Constructor_DefaultsAllowOverrideAndWarn()
    {
        // Spec §2: the override gate is on and credit breaches warn until the owner decides
        // otherwise — the single-operator install must not start by blocking its own sales.
        var settings = new SalesSettings(Guid.NewGuid());

        settings.AllowPriceOverride.ShouldBeTrue();
        settings.CreditLimitPolicy.ShouldBe(CreditLimitPolicy.Warn);
    }

    [Fact]
    public void SetCreditLimitPolicy_UnknownValue_Throws()
    {
        var settings = new SalesSettings(Guid.NewGuid());

        Should.Throw<ArgumentOutOfRangeException>(
            () => settings.SetCreditLimitPolicy((CreditLimitPolicy)99));
    }
}
