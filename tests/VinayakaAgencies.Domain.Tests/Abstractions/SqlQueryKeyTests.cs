using VinayakaAgencies.Domain.Abstractions;

namespace VinayakaAgencies.Domain.Tests.Abstractions;

public class SqlQueryKeyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankName_ThrowsArgumentException(string? blankName)
    {
        Should.Throw<ArgumentException>(() => new SqlQueryKey(blankName!));
    }

    [Fact]
    public void Constructor_ValidName_AssignsName()
    {
        var key = new SqlQueryKey("TrialBalance.ByLedger");

        key.Name.ShouldBe("TrialBalance.ByLedger");
    }

    [Fact]
    public void ToString_ReturnsName()
    {
        var key = new SqlQueryKey("TrialBalance.ByLedger");

        key.ToString().ShouldBe("TrialBalance.ByLedger");
    }

    [Fact]
    public void ImplicitStringConversion_ReturnsName()
    {
        var key = new SqlQueryKey("TrialBalance.ByLedger");

        string asString = key;

        asString.ShouldBe("TrialBalance.ByLedger");
    }

    [Fact]
    public void Equals_SameName_ReturnsTrue()
    {
        // Record-struct value equality: the seam that lets Infrastructure key a dictionary of
        // per-provider SQL statements by SqlQueryKey without a custom comparer.
        var first = new SqlQueryKey("TrialBalance.ByLedger");
        var second = new SqlQueryKey("TrialBalance.ByLedger");

        first.Equals(second).ShouldBeTrue();
        (first == second).ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentName_ReturnsFalse()
    {
        var first = new SqlQueryKey("TrialBalance.ByLedger");
        var second = new SqlQueryKey("TrialBalance.ByStockItem");

        first.Equals(second).ShouldBeFalse();
        (first == second).ShouldBeFalse();
    }

    [Fact]
    public void Equals_NameDiffersOnlyByCase_ReturnsFalse()
    {
        // Query key names resolve to dictionary lookups in the dialect layer; silently treating
        // case variants as the same key would hide a real lookup miss instead of failing loudly.
        var first = new SqlQueryKey("TrialBalance");
        var second = new SqlQueryKey("trialbalance");

        first.Equals(second).ShouldBeFalse();
    }
}
