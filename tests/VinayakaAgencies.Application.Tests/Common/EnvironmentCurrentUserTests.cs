using VinayakaAgencies.Application.Common;
using Shouldly;

namespace VinayakaAgencies.Application.Tests.Common;

/// <summary>
/// <see cref="EnvironmentCurrentUser"/> takes its two names through injected accessors, because
/// <see cref="Environment.MachineName"/> and <see cref="Environment.UserName"/> call Win32 directly
/// and ignore the process environment block — with the statics hard-wired the fallback branches
/// below would be unreachable from any test on a real host.
/// </summary>
/// <remarks>
/// The never-blank guarantee is the one worth the most care: <c>ICurrentUser</c> exists so that
/// every audited change names an author, and an unattributable change is not an audit trail.
/// </remarks>
public sealed class EnvironmentCurrentUserTests
{
    [Fact]
    public void UserName_MachineAndUserBothPresent_ReturnsMachineBackslashUser()
    {
        var sut = new EnvironmentCurrentUser(() => "SHOP-PC", () => "rahul");

        sut.UserName.ShouldBe(@"SHOP-PC\rahul");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UserName_MachineNameBlank_FallsBackToTheBareUserName(string? machineName)
    {
        var sut = new EnvironmentCurrentUser(() => machineName, () => "rahul");

        sut.UserName.ShouldBe("rahul");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UserName_UserNameBlank_ReturnsUnknownUser(string? userName)
    {
        var sut = new EnvironmentCurrentUser(() => "SHOP-PC", () => userName);

        sut.UserName.ShouldBe(EnvironmentCurrentUser.UnknownUser);
    }

    [Fact]
    public void UserName_BothBlank_ReturnsUnknownUserRatherThanBlank()
    {
        var sut = new EnvironmentCurrentUser(() => null, () => null);

        sut.UserName.ShouldBe(EnvironmentCurrentUser.UnknownUser);
        sut.UserName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void UserName_OnTheRealHost_IsNeverBlankOrWhitespace()
    {
        var sut = new EnvironmentCurrentUser();

        sut.UserName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void UserName_OnTheRealHost_ReturnsMachineBackslashUser()
    {
        var sut = new EnvironmentCurrentUser();

        sut.UserName.ShouldBe($@"{Environment.MachineName}\{Environment.UserName}");
    }

    [Fact]
    public void Constructor_WithNullAccessor_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new EnvironmentCurrentUser(null!, () => "rahul"));
        Should.Throw<ArgumentNullException>(() => new EnvironmentCurrentUser(() => "SHOP-PC", null!));
    }
}
