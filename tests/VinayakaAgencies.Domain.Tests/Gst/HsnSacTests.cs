using VinayakaAgencies.Domain.Gst;
using Shouldly;

namespace VinayakaAgencies.Domain.Tests.Gst;

/// <summary>Guard and behaviour tests for the HSN/SAC master (phase-5-gst-returns.md §2).</summary>
public sealed class HsnSacTests
{
    [Fact]
    public void Constructor_ValidCode_NormalisesAndActivates()
    {
        var code = new HsnSac(Guid.NewGuid(), " 84137010 ", " centrifugal pumps ");

        code.Code.ShouldBe("84137010");
        code.Description.ShouldBe("centrifugal pumps");
        code.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_BlankCode_Throws()
    {
        Should.Throw<ArgumentException>(() => new HsnSac(Guid.NewGuid(), "   "));
    }

    [Theory]
    [InlineData("84")]
    [InlineData("123456789")]
    public void Constructor_CodeOutsideLengthBounds_Throws(string code)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new HsnSac(Guid.NewGuid(), code));
    }

    [Fact]
    public void Deactivate_Activate_RoundTrips()
    {
        var code = new HsnSac(Guid.NewGuid(), "84137010");

        code.Deactivate();
        code.IsActive.ShouldBeFalse();

        code.Activate();
        code.IsActive.ShouldBeTrue();
    }
}
